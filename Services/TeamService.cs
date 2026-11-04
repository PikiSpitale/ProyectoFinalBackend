using MongoDB.Bson;
using ProyectAPI.DTOs;
using ProyectAPI.Models;
using ProyectAPI.Repositories;

namespace ProyectAPI.Services;

public sealed class TeamService(ITeamRepository repository, IUserRepository users, TimeProvider? clock = null)
{
    private DateTime Now => (clock ?? TimeProvider.System).GetUtcNow().UtcDateTime;
    public Task<List<Team>> GetAllTeamsAsync(CancellationToken ct) => repository.GetAllAsync(ct);
    public async Task<Team?> GetTeamByIdAsync(string id, CancellationToken ct)
    {
        id = Id(id);
        var team = await repository.GetByIdAsync(id, ct);
        if (team != null)
            foreach (var member in team.Members)
            {
                var user = await users.GetByIdAsync(member.UserId, ct);
                if (user != null) member.LastActive = user.LastActive;
            }
        return team;
    }
    public async Task<bool> UserBelongsToAnyTeamAsync(string userId, CancellationToken ct) =>
        (await repository.GetAllAsync(ct)).Any(t => t.Members.Any(m => m.UserId == userId));

    public Task<Team> CreateTeamAsync(TeamCreateDto dto, string userId, string fallback, CancellationToken ct)
    {
        userId = Id(userId);
        return repository.TransactionAsync(async (tx, token) =>
        {
            var user = await EligibleAsync(tx, userId, dto.GameName, token);
            var team = new Team { Id = ObjectId.GenerateNewId().ToString(), Name = dto.Name.Trim(), Tag = dto.Tag.Trim().ToUpperInvariant(),
                LogoUrl = string.IsNullOrWhiteSpace(dto.LogoUrl) ? "https://images.unsplash.com/photo-1542751371-adc38448a05e?q=80&w=600&auto=format&fit=crop" : dto.LogoUrl, GameName = dto.GameName.Trim(), CaptainId = userId, IsPrivate = dto.IsPrivate,
                CreatedAt = Now, Members = [Member(user, true)] };
            await tx.InsertTeamAsync(team, token);
            return team;
        }, ct);
    }

    public Task<bool> JoinTeamAsync(string teamId, string userId, string fallback, CancellationToken ct)
    {
        teamId = Id(teamId); userId = Id(userId);
        return repository.TransactionAsync(async (tx, token) =>
        {
            var team = await RequiredTeam(tx, teamId, token);
            if (team.IsPrivate) throw Error(403, "Este equipo es privado. Necesitás una invitación del capitán.");
            if (team.Members.Any(x => x.UserId == userId)) return true;
            var user = await EligibleAsync(tx, userId, team.GameName, token);
            team.Members.Add(Member(user));
            await tx.SaveTeamAsync(team, token);
            return true;
        }, ct);
    }

    public Task<bool> LeaveTeamAsync(string teamId, string userId, CancellationToken ct) => ChangeTeam(teamId, async (tx, team, token) =>
    {
        userId = Id(userId);
        if (team.CaptainId == userId) throw Error(409, "El capitán no puede abandonar el equipo.");
        if (team.Members.RemoveAll(x => x.UserId == userId) == 0) throw Error(409, "No pertenecés al equipo.");
        await Task.CompletedTask;
    }, ct);

    public Task<bool> UpdateTeamAsync(string teamId, UpdateTeamDto dto, string userId, CancellationToken ct) => ChangeTeam(teamId, (tx, team, token) =>
    {
        Captain(team, Id(userId));
        if (!string.IsNullOrWhiteSpace(dto.Name)) team.Name = dto.Name.Trim();
        if (!string.IsNullOrWhiteSpace(dto.Tag)) team.Tag = dto.Tag.Trim().ToUpperInvariant();
        if (dto.LogoUrl != null) team.LogoUrl = dto.LogoUrl;
        if (dto.IsPrivate.HasValue) team.IsPrivate = dto.IsPrivate.Value;
        return Task.CompletedTask;
    }, ct);

    public Task<bool> RemoveMemberAsync(string teamId, string memberId, string actor, CancellationToken ct) => ChangeTeam(teamId, (tx, team, token) =>
    {
        actor = Id(actor); memberId = Id(memberId);
        Captain(team, actor);
        if (memberId == team.CaptainId) throw Error(409, "El capitán no puede expulsarse a sí mismo.");
        if (team.Members.RemoveAll(x => x.UserId == memberId) == 0) throw Error(404, "Integrante no encontrado.");
        return Task.CompletedTask;
    }, ct);

    public Task<bool> UpdateMemberRoleAsync(string teamId, string memberId, string role, bool starter, string actor, CancellationToken ct) => ChangeTeam(teamId, (tx, team, token) =>
    {
        actor = Id(actor); memberId = Id(memberId);
        Captain(team, actor);
        var member = team.Members.FirstOrDefault(x => x.UserId == memberId) ?? throw Error(404, "Integrante no encontrado.");
        member.Role = role; member.IsStarter = starter;
        return Task.CompletedTask;
    }, ct);

    private Task<bool> ChangeTeam(string id, Func<ITeamTransaction, Team, CancellationToken, Task> action, CancellationToken ct)
    {
        id = Id(id);
        return repository.TransactionAsync(async (tx, token) =>
        {
            var team = await RequiredTeam(tx, id, token);
            await action(tx, team, token);
            await tx.SaveTeamAsync(team, token);
            return true;
        }, ct);
    }

    public async Task<List<InviteeDto>> FindInviteesAsync(string teamId, string actor, string username, CancellationToken ct)
    {
        teamId = Id(teamId); actor = Id(actor);
        var team = await repository.GetByIdAsync(teamId, ct) ?? throw Error(404, "Equipo no encontrado.");
        Captain(team, actor);
        if (string.IsNullOrWhiteSpace(username) || username.Trim().Length < 2 || username.Length > 40)
            throw Error(400, "Ingresá entre 2 y 40 caracteres del nombre de usuario.");
        return await users.SearchInviteesAsync(username.Trim(), ct);
    }

    public Task<TeamInvitationDto> InviteAsync(string teamId, string actor, string recipient, CancellationToken ct)
    {
        teamId = Id(teamId); recipient = Id(recipient); actor = Id(actor);
        return repository.TransactionAsync(async (tx, token) =>
        {
            var team = await RequiredTeam(tx, teamId, token); Captain(team, actor);
            if (actor == recipient) throw Error(409, "No podés invitarte a vos mismo.");
            var user = await EligibleAsync(tx, recipient, team.GameName, token);
            var sender = await tx.UserAsync(actor, token) ?? throw Error(404, "Usuario no encontrado.");
            var now = Now;
            await tx.ExpireInvitationsAsync(teamId, recipient, now, token);
            var invitation = new TeamInvitation { TeamId = teamId, TeamName = team.Name, SenderId = actor,
                SenderUsername = sender.Username, RecipientId = recipient, RecipientUsername = user.Username,
                CreatedAt = now, ExpiresAt = now.AddDays(7) };
            await tx.InsertInvitationAsync(invitation, token);
            return Map(invitation, now);
        }, ct);
    }

    public async Task<List<TeamInvitationDto>> ListInvitationsAsync(string id, bool received, string actor, CancellationToken ct)
    {
        actor = Id(actor);
        if (!received)
        {
            id = Id(id);
            var team = await repository.GetByIdAsync(id, ct) ?? throw Error(404, "Equipo no encontrado.");
            Captain(team, actor);
        }
        var now = Now;
        return (await repository.InvitationsAsync(received ? actor : id, received, ct)).Select(x => Map(x, now)).ToList();
    }

    public async Task<TeamInvitationDto> ResolveInvitationAsync(string id, string actor, string action, CancellationToken ct)
    {
        id = Id(id); actor = Id(actor);
        var result = await repository.TransactionAsync(async (tx, token) =>
        {
            var invitation = await tx.InvitationAsync(id, token) ?? throw Error(404, "Invitación no encontrada.");
            var team = await RequiredTeam(tx, invitation.TeamId, token);
            if (action == "cancel") Captain(team, actor);
            else if (invitation.RecipientId != actor) throw Error(403, "Solo el destinatario puede responder la invitación.");
            var now = Now;
            if (invitation.Status == "Expired") return Map(invitation, now);
            if (invitation.Status != "Pending") throw Error(409, "La invitación ya fue procesada.");
            if (invitation.ExpiresAt <= now)
            {
                invitation.Status = "Expired";
                await tx.SaveInvitationAsync(invitation, token);
                return Map(invitation, now);
            }
            if (action == "accept")
            {
                var user = await EligibleAsync(tx, actor, team.GameName, token);
                team.Members.Add(Member(user));
                await tx.SaveTeamAsync(team, token);
                invitation.Status = "Accepted";
            }
            else if (action == "reject") invitation.Status = "Rejected";
            else if (action == "cancel") invitation.Status = "Cancelled";
            else throw Error(400, "Acción no válida.");
            await tx.SaveInvitationAsync(invitation, token);
            return Map(invitation, now);
        }, ct);
        // Throw only after commit, so expiration is durable even for a rejected request.
        if (result.Status == "Expired") throw Error(410, "La invitación venció.");
        return result;
    }

    private async Task<User> EligibleAsync(ITeamTransaction tx, string id, string game, CancellationToken ct)
    {
        id = Id(id);
        var user = await tx.UserAsync(id, ct) ?? throw Error(404, "Usuario no encontrado.");
        if (!user.IsActive) throw Error(409, "El usuario no está activo.");
        if (await tx.HasTeamAsync(id, ct)) throw Error(409, "El usuario ya pertenece a un equipo.");
        var profile = user.Games.FirstOrDefault(x => x.GameName.Equals(game, StringComparison.OrdinalIgnoreCase));
        if (profile == null || string.IsNullOrWhiteSpace(profile.InGameName) || string.IsNullOrWhiteSpace(profile.Rank))
            throw Error(409, "El usuario debe asociar el videojuego del equipo con nombre de jugador y rango.");
        return user;
    }
    private TeamMember Member(User user, bool captain = false) => new() { UserId = user.Id, Username = user.Username,
        Role = captain ? "Captain" : "Member", IsStarter = true, JoinedAt = Now, LastActive = user.LastActive };
    private static Task<Team> RequiredTeam(ITeamTransaction tx, string id, CancellationToken ct) => GetTeam(tx, id, ct);
    private static async Task<Team> GetTeam(ITeamTransaction tx, string id, CancellationToken ct) =>
        await tx.TeamAsync(id, ct) ?? throw Error(404, "Equipo no encontrado.");
    private static void Captain(Team team, string actor)
    { if (team.CaptainId != actor) throw Error(403, "Solo el capitán puede realizar esta operación."); }
    private static string Id(string id)
    {
        if (!ObjectId.TryParse(id, out var value)) throw Error(400, "Identificador no válido.");
        return value.ToString();
    }
    private static TeamOperationException Error(int status, string message) => new(status, message);
    private static TeamInvitationDto Map(TeamInvitation x, DateTime now) => new(x.Id, x.TeamId, x.TeamName, x.SenderId,
        x.SenderUsername, x.RecipientId, x.RecipientUsername, x.Status == "Pending" && x.ExpiresAt <= now ? "Expired" : x.Status, x.CreatedAt, x.ExpiresAt);
}
