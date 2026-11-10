using ProyectAPI.DTOs;
using ProyectAPI.Models;
using ProyectAPI.Repositories;

namespace ProyectAPI.Services
{
    public sealed class TeamService
    {
        private readonly ITeamRepository _teamRepository;
        private readonly IUserRepository _userRepository;
        private readonly ITeamInvitationRepository _invitationRepository;
        private readonly RankingService _rankingService;

        public TeamService(
            ITeamRepository teamRepository,
            IUserRepository userRepository,
            ITeamInvitationRepository invitationRepository,
            RankingService rankingService)
        {
            _teamRepository = teamRepository;
            _userRepository = userRepository;
            _invitationRepository = invitationRepository;
            _rankingService = rankingService;
        }

        public async Task<List<Team>> GetAllTeamsAsync(CancellationToken cancellationToken) =>
            await _teamRepository.GetAllAsync(cancellationToken);

        public async Task<Team?> GetTeamByIdAsync(string id, CancellationToken cancellationToken)
        {
            var team = await _teamRepository.GetByIdAsync(id, cancellationToken);
            if (team == null) return null;

            foreach (var member in team.Members)
            {
                var user = await _userRepository.GetByIdAsync(member.UserId, cancellationToken);
                if (user != null)
                {
                    member.LastActive = user.LastActive;
                }
            }

            return team;
        }

        public async Task<bool> UserBelongsToAnyTeamAsync(string userId, CancellationToken cancellationToken)
        {
            var allTeams = await _teamRepository.GetAllAsync(cancellationToken);
            return allTeams.Any(t => t.Members.Any(m => m.UserId == userId));
        }

        public async Task<Team> CreateTeamAsync(TeamCreateDto dto, string userId, string fallbackUsername, CancellationToken cancellationToken)
        {
            if (await UserBelongsToAnyTeamAsync(userId, cancellationToken))
            {
                throw new InvalidOperationException("Ya perteneces a un equipo.");
            }

            if (string.IsNullOrWhiteSpace(dto.GameName))
            {
                throw new InvalidOperationException("El videojuego es obligatorio para crear un equipo.");
            }

            var user = await _userRepository.GetByIdAsync(userId, cancellationToken);
            string realUsername = user?.Username ?? fallbackUsername;

            // Validar que el usuario tenga asociado el juego con in-game name y rango (requisito para torneos)
            var userGame = user?.Games?.FirstOrDefault(g => g.GameName.Equals(dto.GameName, StringComparison.OrdinalIgnoreCase));
            if (userGame is null || string.IsNullOrWhiteSpace(userGame.InGameName) || string.IsNullOrWhiteSpace(userGame.Rank))
            {
                throw new InvalidOperationException("Debes tener el videojuego asociado a tu perfil con nombre de usuario y rango para crear un equipo de ese juego.");
            }

            var (joinPolicy, rankFilterType, rankFilterValue) = NormalizeJoinSettings(dto.GameName.Trim(), dto.JoinPolicy, dto.RankFilterType, dto.RankFilterValue, defaultInviteOnly: true);

            var newTeam = new Team
            {
                Name = dto.Name?.Trim() ?? string.Empty,
                Tag = dto.Tag?.Trim().ToUpperInvariant() ?? string.Empty,
                LogoUrl = string.IsNullOrWhiteSpace(dto.LogoUrl) ? "https://images.unsplash.com/photo-1542751371-adc38448a05e?q=80&w=600&auto=format&fit=crop" : dto.LogoUrl,
                GameName = dto.GameName.Trim(),
                CaptainId = userId,
                JoinPolicy = joinPolicy,
                RankFilterType = rankFilterType,
                RankFilterValue = rankFilterValue,
                CreatedAt = DateTime.UtcNow,
                Members = new List<TeamMember>
                {
                    new TeamMember
                    {
                        UserId = userId,
                        Username = realUsername,
                        Role = "Captain",
                        IsStarter = true,
                        JoinedAt = DateTime.UtcNow,
                        LastActive = DateTime.UtcNow
                    }
                }
            };

            await _teamRepository.CreateAsync(newTeam, cancellationToken);
            return newTeam;
        }

        // Unión pública (si el equipo es Open) o rechazo con guía a invitación (InviteOnly).
        public async Task<bool> JoinTeamAsync(string teamId, string userId, string fallbackUsername, CancellationToken cancellationToken)
        {
            if (await UserBelongsToAnyTeamAsync(userId, cancellationToken))
            {
                throw new InvalidOperationException("Ya formas parte de otro equipo.");
            }

            var team = await _teamRepository.GetByIdAsync(teamId, cancellationToken);
            if (team == null) return false;

            if (EffectiveJoinPolicy(team) == TeamJoinPolicy.InviteOnly)
            {
                throw new InvalidOperationException("Este equipo es solo por invitación. El capitán debe enviarte una invitación.");
            }

            if (team.Members.Any(m => m.UserId == userId))
            {
                return true;
            }

            var user = await _userRepository.GetByIdAsync(userId, cancellationToken);
            string realUsername = user?.Username ?? fallbackUsername;

            if (string.IsNullOrWhiteSpace(team.GameName))
            {
                throw new InvalidOperationException("El equipo no tiene un videojuego asociado.");
            }

            var userGame = user?.Games?.FirstOrDefault(g => g.GameName.Equals(team.GameName, StringComparison.OrdinalIgnoreCase));
            if (userGame is null || string.IsNullOrWhiteSpace(userGame.InGameName) || string.IsNullOrWhiteSpace(userGame.Rank))
            {
                throw new InvalidOperationException("Debes tener el videojuego del equipo asociado a tu perfil con nombre de usuario y rango para unirte.");
            }

            ValidateRankFilter(team, userGame.Rank);

            var newMember = new TeamMember
            {
                UserId = userId,
                Username = realUsername,
                Role = "Member",
                IsStarter = true,
                JoinedAt = DateTime.UtcNow,
                LastActive = DateTime.UtcNow
            };

            return await _teamRepository.AddMemberAsync(teamId, newMember, cancellationToken);
        }

        public async Task<bool> LeaveTeamAsync(string teamId, string userId, CancellationToken cancellationToken)
        {
            var team = await _teamRepository.GetByIdAsync(teamId, cancellationToken);
            if (team == null) return false;

            var memberToRemove = team.Members.FirstOrDefault(m => m.UserId == userId);
            if (memberToRemove == null) return false;

            if (team.CaptainId == userId)
            {
                var remaining = team.Members.Where(m => m.UserId != userId).ToList();
                if (remaining.Count == 0)
                {
                    return await _teamRepository.DeleteAsync(teamId, cancellationToken);
                }

                var newCaptain = remaining.OrderBy(m => m.JoinedAt).First();
                team.CaptainId = newCaptain.UserId;
                newCaptain.Role = "Captain";
                newCaptain.IsStarter = true;
            }

            team.Members.Remove(memberToRemove);
            return await _teamRepository.UpdateAsync(team, cancellationToken);
        }

        public async Task<bool> DeleteTeamAsync(string teamId, string currentUserId, bool isModerator, CancellationToken cancellationToken)
        {
            var team = await _teamRepository.GetByIdAsync(teamId, cancellationToken);
            if (team == null) return false;

            if (!isModerator && team.CaptainId != currentUserId)
            {
                throw new UnauthorizedAccessException("Solo el capitán o un moderador pueden eliminar el equipo.");
            }

            var deleted = await _teamRepository.DeleteAsync(teamId, cancellationToken);
            if (deleted)
            {
                // El equipo deja de existir: se retira su propio historial de ranking.
                // Los puntos individuales de sus integrantes se conservan.
                await _rankingService.RemoveTeamAsync(teamId, cancellationToken);
            }

            return deleted;
        }

        // RF-10
        public async Task<bool> UpdateTeamAsync(string teamId, UpdateTeamDto dto, string userId, CancellationToken cancellationToken)
        {
            var team = await _teamRepository.GetByIdAsync(teamId, cancellationToken);
            if (team == null) return false;

            if (team.CaptainId != userId)
            {
                throw new InvalidOperationException("Solo el capitán puede modificar la información del equipo.");
            }

            if (!string.IsNullOrWhiteSpace(dto.Name)) team.Name = dto.Name.Trim();
            if (!string.IsNullOrWhiteSpace(dto.Tag)) team.Tag = dto.Tag.Trim().ToUpperInvariant();
            if (!string.IsNullOrWhiteSpace(dto.LogoUrl)) team.LogoUrl = dto.LogoUrl;

            if (dto.JoinPolicy is not null || dto.RankFilterType is not null || dto.RankFilterValue is not null)
            {
                var (joinPolicy, rankFilterType, rankFilterValue) = NormalizeJoinSettings(
                    team.GameName,
                    dto.JoinPolicy ?? EffectiveJoinPolicy(team),
                    dto.RankFilterType ?? team.RankFilterType,
                    dto.RankFilterValue ?? team.RankFilterValue,
                    defaultInviteOnly: EffectiveJoinPolicy(team) == TeamJoinPolicy.InviteOnly);
                team.JoinPolicy = joinPolicy;
                team.RankFilterType = rankFilterType;
                team.RankFilterValue = rankFilterValue;
            }

            return await _teamRepository.UpdateAsync(team, cancellationToken);
        }

        // RF-13
        public async Task<bool> RemoveMemberAsync(string teamId, string memberUserId, string currentUserId, CancellationToken cancellationToken)
        {
            var team = await _teamRepository.GetByIdAsync(teamId, cancellationToken);
            if (team == null) return false;

            if (team.CaptainId != currentUserId)
            {
                throw new InvalidOperationException("Solo el capitán puede expulsar integrantes.");
            }

            if (team.CaptainId == memberUserId)
            {
                throw new InvalidOperationException("El capitán no puede expulsarse a sí mismo.");
            }

            var memberToRemove = team.Members.FirstOrDefault(m => m.UserId == memberUserId);
            if (memberToRemove == null) return false;

            team.Members.Remove(memberToRemove);
            return await _teamRepository.UpdateAsync(team, cancellationToken);
        }

        // RF-14 y RF-15
        public async Task<bool> UpdateMemberRoleAsync(string teamId, string memberUserId, string newRole, bool isStarter, string currentUserId, CancellationToken cancellationToken)
        {
            var team = await _teamRepository.GetByIdAsync(teamId, cancellationToken);
            if (team == null) return false;

            if (team.CaptainId != currentUserId)
            {
                throw new InvalidOperationException("Solo el capitán puede modificar los roles de los integrantes.");
            }

            var member = team.Members.FirstOrDefault(m => m.UserId == memberUserId);
            if (member == null) return false;

            var role = NormalizeTeamRole(newRole);
            var currentCaptain = team.Members.FirstOrDefault(m => m.UserId == team.CaptainId);
            var isCurrentCaptain = member.UserId == team.CaptainId;

            if (role == "Captain")
            {
                // Solo puede haber un capitán: el anterior pasa a miembro.
                foreach (var other in team.Members.Where(m => m.UserId != member.UserId))
                {
                    if (string.Equals(other.Role, "Captain", StringComparison.OrdinalIgnoreCase))
                    {
                        other.Role = "Member";
                    }
                }

                if (currentCaptain is not null && currentCaptain.UserId != member.UserId)
                {
                    currentCaptain.Role = "Member";
                }

                team.CaptainId = member.UserId;
                member.Role = "Captain";
            }
            else
            {
                if (isCurrentCaptain)
                {
                    throw new InvalidOperationException(
                        "El capitán no puede cambiarse a miembro. Transferí la capitanía a otro integrante primero.");
                }

                member.Role = "Member";
            }

            member.IsStarter = isStarter;
            return await _teamRepository.UpdateAsync(team, cancellationToken);
        }

        private static string NormalizeTeamRole(string? role)
        {
            var value = (role ?? string.Empty).Trim();
            if (value.Equals("Captain", StringComparison.OrdinalIgnoreCase) ||
                value.Equals("Capitán", StringComparison.OrdinalIgnoreCase) ||
                value.Equals("Capitan", StringComparison.OrdinalIgnoreCase))
            {
                return "Captain";
            }

            if (value.Equals("Member", StringComparison.OrdinalIgnoreCase) ||
                value.Equals("Miembro", StringComparison.OrdinalIgnoreCase))
            {
                return "Member";
            }

            throw new InvalidOperationException("Rol inválido. Los roles disponibles son Capitán y Miembro.");
        }

        // RF-11
        public async Task<TeamInvitationDto> InviteMemberAsync(string teamId, string inviteeUsername, string currentUserId, CancellationToken cancellationToken)
        {
            var team = await _teamRepository.GetByIdAsync(teamId, cancellationToken) ?? throw new InvalidOperationException("Equipo no encontrado.");
            if (team.CaptainId != currentUserId)
                throw new InvalidOperationException("Solo el capitán puede invitar jugadores al equipo.");

            var invitee = await _userRepository.GetByUsernameAsync(inviteeUsername.Trim(), cancellationToken)
                ?? throw new InvalidOperationException("No se encontró un usuario con ese nombre.");

            if (invitee.Id == currentUserId)
                throw new InvalidOperationException("No podés invitarte a vos mismo.");

            if (team.Members.Any(m => m.UserId == invitee.Id))
                throw new InvalidOperationException("Ese usuario ya es integrante del equipo.");

            if (await UserBelongsToAnyTeamAsync(invitee.Id, cancellationToken))
                throw new InvalidOperationException("Ese usuario ya pertenece a otro equipo.");

            if (string.IsNullOrWhiteSpace(team.GameName))
                throw new InvalidOperationException("El equipo no tiene un videojuego asociado.");

            var userGame = invitee.Games?.FirstOrDefault(g => g.GameName.Equals(team.GameName, StringComparison.OrdinalIgnoreCase));
            if (userGame is null || string.IsNullOrWhiteSpace(userGame.InGameName) || string.IsNullOrWhiteSpace(userGame.Rank))
                throw new InvalidOperationException("El usuario invitado debe tener el videojuego del equipo asociado en su perfil con nombre de usuario y rango.");

            var existing = await _invitationRepository.GetPendingByTeamAndInviteeAsync(teamId, invitee.Id, cancellationToken);
            if (existing is not null)
                throw new InvalidOperationException("Ya existe una invitación pendiente para ese usuario.");

            var inviter = await _userRepository.GetByIdAsync(currentUserId, cancellationToken);
            var invitation = new TeamInvitation
            {
                TeamId = team.Id,
                TeamName = team.Name,
                TeamTag = team.Tag,
                GameName = team.GameName,
                InviterId = currentUserId,
                InviterUsername = inviter?.Username ?? "Capitán",
                InviteeId = invitee.Id,
                InviteeUsername = invitee.Username,
                Status = TeamInvitationStatus.Pending,
                CreatedAt = DateTime.UtcNow
            };

            await _invitationRepository.CreateAsync(invitation, cancellationToken);
            return MapInvitation(invitation);
        }

        public async Task<List<TeamInvitationDto>> GetTeamInvitationsAsync(string teamId, string currentUserId, CancellationToken cancellationToken)
        {
            var team = await _teamRepository.GetByIdAsync(teamId, cancellationToken) ?? throw new InvalidOperationException("Equipo no encontrado.");
            if (team.CaptainId != currentUserId)
                throw new InvalidOperationException("Solo el capitán puede ver las invitaciones del equipo.");
            var list = await _invitationRepository.GetPendingByTeamIdAsync(teamId, cancellationToken);
            return list.Select(MapInvitation).ToList();
        }

        // RF-12
        public async Task<List<TeamInvitationDto>> GetMyInvitationsAsync(string userId, CancellationToken cancellationToken)
        {
            var list = await _invitationRepository.GetPendingByInviteeIdAsync(userId, cancellationToken);
            return list.Select(MapInvitation).ToList();
        }

        // RF-12
        public async Task<bool> RespondInvitationAsync(string invitationId, string userId, string response, CancellationToken cancellationToken)
        {
            var invitation = await _invitationRepository.GetByIdAsync(invitationId, cancellationToken)
                ?? throw new InvalidOperationException("Invitación no encontrada.");

            if (invitation.InviteeId != userId)
                throw new InvalidOperationException("No podés responder una invitación que no es tuya.");

            if (invitation.Status != TeamInvitationStatus.Pending)
                throw new InvalidOperationException("La invitación ya fue respondida.");

            var normalized = (response ?? string.Empty).Trim();
            var accept = normalized.Equals("Accept", StringComparison.OrdinalIgnoreCase) || normalized.Equals("Aceptar", StringComparison.OrdinalIgnoreCase);
            var reject = normalized.Equals("Reject", StringComparison.OrdinalIgnoreCase) || normalized.Equals("Rechazar", StringComparison.OrdinalIgnoreCase);
            if (!accept && !reject)
                throw new InvalidOperationException("Respuesta inválida. Usá 'Accept' o 'Reject'.");

            if (reject)
            {
                invitation.Status = TeamInvitationStatus.Rejected;
                invitation.RespondedAt = DateTime.UtcNow;
                await _invitationRepository.UpdateAsync(invitation, cancellationToken);
                return true;
            }

            // Accept
            if (await UserBelongsToAnyTeamAsync(userId, cancellationToken))
                throw new InvalidOperationException("Ya pertenecés a un equipo. Salí del equipo actual antes de aceptar esta invitación.");

            var team = await _teamRepository.GetByIdAsync(invitation.TeamId, cancellationToken)
                ?? throw new InvalidOperationException("El equipo ya no existe.");

            var invitee = await _userRepository.GetByIdAsync(userId, cancellationToken)
                ?? throw new InvalidOperationException("Usuario no encontrado.");

            var userGame = invitee.Games?.FirstOrDefault(g => g.GameName.Equals(team.GameName, StringComparison.OrdinalIgnoreCase));
            if (userGame is null || string.IsNullOrWhiteSpace(userGame.InGameName) || string.IsNullOrWhiteSpace(userGame.Rank))
                throw new InvalidOperationException("Debes tener el videojuego del equipo asociado en tu perfil con nombre de usuario y rango para aceptar.");

            if (team.Members.Any(m => m.UserId == userId))
            {
                invitation.Status = TeamInvitationStatus.Accepted;
                invitation.RespondedAt = DateTime.UtcNow;
                await _invitationRepository.UpdateAsync(invitation, cancellationToken);
                return true;
            }

            var newMember = new TeamMember
            {
                UserId = userId,
                Username = invitee.Username,
                Role = "Member",
                IsStarter = true,
                JoinedAt = DateTime.UtcNow,
                LastActive = DateTime.UtcNow
            };

            var added = await _teamRepository.AddMemberAsync(team.Id, newMember, cancellationToken);
            if (!added) throw new InvalidOperationException("No se pudo agregar al equipo.");

            invitation.Status = TeamInvitationStatus.Accepted;
            invitation.RespondedAt = DateTime.UtcNow;
            await _invitationRepository.UpdateAsync(invitation, cancellationToken);
            return true;
        }

        public async Task<List<UserSearchResultDto>> SearchUsersAsync(string query, CancellationToken cancellationToken)
        {
            if (string.IsNullOrWhiteSpace(query) || query.Trim().Length < 2) return new List<UserSearchResultDto>();
            var q = query.Trim();
            var users = await _userRepository.GetAllAsync(cancellationToken);
            return users
                .Where(u => u.Username.Contains(q, StringComparison.OrdinalIgnoreCase) || u.Email.Contains(q, StringComparison.OrdinalIgnoreCase))
                .Take(10)
                .Select(u => new UserSearchResultDto { Id = u.Id, Username = u.Username })
                .ToList();
        }

        private static TeamInvitationDto MapInvitation(TeamInvitation i) => new()
        {
            Id = i.Id,
            TeamId = i.TeamId,
            TeamName = i.TeamName,
            TeamTag = i.TeamTag,
            GameName = i.GameName,
            InviterId = i.InviterId,
            InviterUsername = i.InviterUsername,
            InviteeId = i.InviteeId,
            InviteeUsername = i.InviteeUsername,
            Status = i.Status,
            CreatedAt = i.CreatedAt
        };

        private static string EffectiveJoinPolicy(Team team)
        {
            if (string.IsNullOrWhiteSpace(team.JoinPolicy))
                return TeamJoinPolicy.Open; // legado: antes /join era abierto
            return team.JoinPolicy.Equals(TeamJoinPolicy.InviteOnly, StringComparison.OrdinalIgnoreCase)
                ? TeamJoinPolicy.InviteOnly
                : TeamJoinPolicy.Open;
        }

        private static (string joinPolicy, string rankFilterType, string? rankFilterValue) NormalizeJoinSettings(
            string gameName,
            string? joinPolicy,
            string? rankFilterType,
            string? rankFilterValue,
            bool defaultInviteOnly)
        {
            var policy = (joinPolicy ?? string.Empty).Trim();
            if (policy.Equals(TeamJoinPolicy.Open, StringComparison.OrdinalIgnoreCase))
                policy = TeamJoinPolicy.Open;
            else if (policy.Equals(TeamJoinPolicy.InviteOnly, StringComparison.OrdinalIgnoreCase))
                policy = TeamJoinPolicy.InviteOnly;
            else
                policy = defaultInviteOnly ? TeamJoinPolicy.InviteOnly : TeamJoinPolicy.Open;

            if (policy == TeamJoinPolicy.InviteOnly)
                return (policy, TeamRankFilterType.None, null);

            var filter = (rankFilterType ?? TeamRankFilterType.None).Trim();
            if (filter.Equals(TeamRankFilterType.Fixed, StringComparison.OrdinalIgnoreCase)) filter = TeamRankFilterType.Fixed;
            else if (filter.Equals(TeamRankFilterType.Minimum, StringComparison.OrdinalIgnoreCase)) filter = TeamRankFilterType.Minimum;
            else filter = TeamRankFilterType.None;

            if (filter == TeamRankFilterType.None)
                return (policy, TeamRankFilterType.None, null);

            var value = (rankFilterValue ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(value))
                throw new InvalidOperationException("Debés especificar un rango para el filtro seleccionado.");

            if (filter == TeamRankFilterType.Minimum)
            {
                if (!GameRankLadders.HasLadder(gameName))
                    throw new InvalidOperationException($"El juego \"{gameName}\" no tiene una escalera de rangos configurada para validar un mínimo. Usá 'Rango fijo' o 'Sin mínimo'.");
                if (!GameRankLadders.IsRecognizedRank(gameName, value))
                    throw new InvalidOperationException($"El rango mínimo \"{value}\" no se reconoce para {gameName}. Verificá el valor seleccionado.");
            }

            if (filter == TeamRankFilterType.Fixed && GameRankLadders.HasLadder(gameName) && !GameRankLadders.IsRecognizedRank(gameName, value))
            {
                throw new InvalidOperationException($"El rango \"{value}\" no se reconoce para {gameName}. Verificá el valor seleccionado.");
            }

            return (policy, filter, value);
        }

        private static void ValidateRankFilter(Team team, string userRank)
        {
            var filter = (team.RankFilterType ?? TeamRankFilterType.None).Trim();
            var value = (team.RankFilterValue ?? string.Empty).Trim();

            if (filter.Equals(TeamRankFilterType.None, StringComparison.OrdinalIgnoreCase) || string.IsNullOrWhiteSpace(value))
                return;

            if (filter.Equals(TeamRankFilterType.Fixed, StringComparison.OrdinalIgnoreCase))
            {
                if (!GameRankLadders.MatchesFixed(team.GameName, value, userRank))
                    throw new InvalidOperationException($"Este equipo requiere el rango \"{value}\". Tu rango en {team.GameName} es \"{userRank}\".");
                return;
            }

            if (filter.Equals(TeamRankFilterType.Minimum, StringComparison.OrdinalIgnoreCase))
            {
                if (!GameRankLadders.HasLadder(team.GameName))
                    return; // sin escalera, no bloqueamos (configuración atípica)
                if (!GameRankLadders.MeetsMinimum(team.GameName, value, userRank))
                    throw new InvalidOperationException($"Este equipo requiere un rango mínimo de \"{value}\". Tu rango en {team.GameName} es \"{userRank}\".");
            }
        }
    }
}
