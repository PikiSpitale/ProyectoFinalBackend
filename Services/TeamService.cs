using ProyectAPI.DTOs;
using ProyectAPI.Models;
using ProyectAPI.Repositories;

namespace ProyectAPI.Services
{
    public sealed class TeamService
    {
        private readonly ITeamRepository _teamRepository;
        private readonly IUserRepository _userRepository;

        // Constructor con repositorios inyectados
        public TeamService(ITeamRepository teamRepository, IUserRepository userRepository)
        {
            _teamRepository = teamRepository;
            _userRepository = userRepository;
        }

        // Obtener lista completa de equipos
        public async Task<List<Team>> GetAllTeamsAsync(CancellationToken cancellationToken) =>
            await _teamRepository.GetAllAsync(cancellationToken);

        // Obtener equipo por ID actualizando el estado de actividad de los miembros
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

        // Validar si el usuario ya pertenece a algún equipo
        public async Task<bool> UserBelongsToAnyTeamAsync(string userId, CancellationToken cancellationToken)
        {
            var allTeams = await _teamRepository.GetAllAsync(cancellationToken);
            return allTeams.Any(t => t.Members.Any(m => m.UserId == userId));
        }

        // Crear un nuevo equipo asignando al creador como capitán
        public async Task<Team> CreateTeamAsync(TeamCreateDto dto, string userId, string fallbackUsername, CancellationToken cancellationToken)
        {
            if (await UserBelongsToAnyTeamAsync(userId, cancellationToken))
            {
                throw new InvalidOperationException("Ya perteneces a un equipo.");
            }

            var user = await _userRepository.GetByIdAsync(userId, cancellationToken);
            string realUsername = user?.Username ?? fallbackUsername;

            var newTeam = new Team
            {
                Name = dto.Name,
                Tag = dto.Tag,
                LogoUrl = string.IsNullOrWhiteSpace(dto.LogoUrl) ? "https://images.unsplash.com/photo-1542751371-adc38448a05e?q=80&w=600&auto=format&fit=crop" : dto.LogoUrl,
                CaptainId = userId,
                Members = new List<TeamMember>
                {
                    new TeamMember
                    {
                        UserId = userId,
                        Username = realUsername,
                        Role = "Captain",
                        JoinedAt = DateTime.UtcNow,
                        LastActive = DateTime.UtcNow
                    }
                }
            };

            await _teamRepository.CreateAsync(newTeam, cancellationToken);
            return newTeam;
        }

        // Unirse a un equipo existente
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

            var newMember = new TeamMember
            {
                UserId = userId,
                Username = realUsername,
                Role = "Member",
                JoinedAt = DateTime.UtcNow,
                LastActive = DateTime.UtcNow
            };

            return await _teamRepository.AddMemberAsync(teamId, newMember, cancellationToken);
        }

        // Abandonar un equipo
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
    }
}