using ProyectAPI.DTOs;
using ProyectAPI.Models;
using ProyectAPI.Repositories;

namespace ProyectAPI.Services
{
    public sealed class TeamService
    {
        private readonly ITeamRepository _teamRepository;
        private readonly IUserRepository _userRepository;

        public TeamService(ITeamRepository teamRepository, IUserRepository userRepository)
        {
            _teamRepository = teamRepository;
            _userRepository = userRepository;
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

            var newTeam = new Team
            {
                Name = dto.Name?.Trim() ?? string.Empty,
                Tag = dto.Tag?.Trim().ToUpperInvariant() ?? string.Empty,
                LogoUrl = string.IsNullOrWhiteSpace(dto.LogoUrl) ? "https://images.unsplash.com/photo-1542751371-adc38448a05e?q=80&w=600&auto=format&fit=crop" : dto.LogoUrl,
                GameName = dto.GameName.Trim(),
                CaptainId = userId,
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

        public async Task<bool> JoinTeamAsync(string teamId, string userId, string fallbackUsername, CancellationToken cancellationToken)
        {
            if (await UserBelongsToAnyTeamAsync(userId, cancellationToken))
            {
                throw new InvalidOperationException("Ya formas parte de otro equipo.");
            }

            var team = await _teamRepository.GetByIdAsync(teamId, cancellationToken);
            if (team == null) return false;

            if (team.Members.Any(m => m.UserId == userId))
            {
                return true;
            }

            var user = await _userRepository.GetByIdAsync(userId, cancellationToken);
            string realUsername = user?.Username ?? fallbackUsername;

            // Validar que el usuario tenga el juego del equipo con in-game name y rango
            if (string.IsNullOrWhiteSpace(team.GameName))
            {
                throw new InvalidOperationException("El equipo no tiene un videojuego asociado.");
            }

            var userGame = user?.Games?.FirstOrDefault(g => g.GameName.Equals(team.GameName, StringComparison.OrdinalIgnoreCase));
            if (userGame is null || string.IsNullOrWhiteSpace(userGame.InGameName) || string.IsNullOrWhiteSpace(userGame.Rank))
            {
                throw new InvalidOperationException("Debes tener el videojuego del equipo asociado a tu perfil con nombre de usuario y rango para unirte.");
            }

            // Según RF-11/RF-12 las invitaciones son el flujo correcto; este endpoint queda por compatibilidad
            // pero exigimos validar que el invitado tenga el juego (coherente con torneos grupales).
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
                throw new InvalidOperationException("El capitán no puede abandonar el equipo. Debe transferir el liderazgo o eliminar el equipo.");
            }

            team.Members.Remove(memberToRemove);
            return await _teamRepository.UpdateAsync(team, cancellationToken);
        }

        // RF-10: Actualizar información del equipo
        public async Task<bool> UpdateTeamAsync(string teamId, UpdateTeamDto dto, string userId, CancellationToken cancellationToken)
        {
            var team = await _teamRepository.GetByIdAsync(teamId, cancellationToken);
            if (team == null) return false;

            if (team.CaptainId != userId)
            {
                throw new InvalidOperationException("Solo el capitán puede modificar la información del equipo.");
            }

            if (!string.IsNullOrWhiteSpace(dto.Name))
            {
                team.Name = dto.Name.Trim();
            }
            if (!string.IsNullOrWhiteSpace(dto.Tag))
            {
                team.Tag = dto.Tag.Trim().ToUpperInvariant();
            }
            if (!string.IsNullOrWhiteSpace(dto.LogoUrl))
            {
                team.LogoUrl = dto.LogoUrl;
            }

            return await _teamRepository.UpdateAsync(team, cancellationToken);
        }

        // RF-13: Expulsar miembro
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

        // RF-14 y RF-15: Cambiar rol y titular/suplente
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

            member.Role = newRole;
            member.IsStarter = isStarter;

            return await _teamRepository.UpdateAsync(team, cancellationToken);
        }
    }
}