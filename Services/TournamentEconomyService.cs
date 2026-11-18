using MongoDB.Bson;
using MongoDB.Driver;
using ProyectAPI.Models;
using ProyectAPI.Repositories;
namespace ProyectAPI.Services;

public sealed class TournamentEconomyService(TournamentEconomyRepository repository, WalletRepository wallets, TimeProvider clock, EloService elo)
{
    public DateTime UtcNow => clock.GetUtcNow().UtcDateTime;
    private static InvalidOperationException Error(string text) => new(text);
    public async Task<T> Run<T>(string id, Func<Tournament?, WalletTransaction, CancellationToken, Task<T>> action, CancellationToken ct, bool allowClosed = false)
    {
        if (TournamentEconomyRepository.Session is not null) throw Error("Operación de torneo anidada no permitida.");
        return await wallets.TransactionAsync((wallet, token) => repository.WithSession(wallet.Session, async () =>
        {
            var tournament = await repository.Fence(WalletRepository.UserId(id), token);
            if (!allowClosed && tournament?.ClosedAt != null) throw Error("El torneo está cerrado definitivamente.");
            if (!allowClosed && tournament?.Status == "Cancelado") throw Error("El torneo está cancelado.");
            return await action(tournament, wallet, token);
        }), ct);
    }
    private void Open(Tournament t)
    {
        var now = clock.GetUtcNow().UtcDateTime;
        if (t.Status != TournamentStatuses.Upcoming || now < t.StartDate.AddMinutes(-30) || now >= t.StartDate)
            throw Error("Las inscripciones no están abiertas.");
        if (t.Participants.Count >= 8) throw Error("El torneo no tiene cupo.");
    }
    private async Task<TournamentPayer> Player(string id, Tournament t, CancellationToken ct)
    {
        var user = await repository.Database.GetCollection<User>("users").Find(TournamentEconomyRepository.Session!, x => x.Id == id && x.IsActive).FirstOrDefaultAsync(ct);
        var game = user?.Games.FirstOrDefault(x => x.GameName.Equals(t.GameName, StringComparison.OrdinalIgnoreCase));
        if (user == null || game == null || string.IsNullOrWhiteSpace(game.InGameName) || string.IsNullOrWhiteSpace(game.Rank))
            throw Error("Cada titular debe tener una cuenta activa y el juego con nombre y rango en su perfil.");
        ValidateRank(t.GameName, t.RankConditions, game.Rank);
        return new TournamentPayer { UserId = id, Username = user.Username, InGameName = game.InGameName, Rank = game.Rank };
    }
    internal static void ValidateRank(string game, string conditions, string rank)
    {
        if (string.IsNullOrWhiteSpace(conditions) || conditions.StartsWith("Sin restric", StringComparison.OrdinalIgnoreCase)) return;
        if (!conditions.StartsWith("Mínimo:", StringComparison.OrdinalIgnoreCase) && !conditions.StartsWith("Minimo:", StringComparison.OrdinalIgnoreCase))
            throw Error("La condición de rango debe ser 'Sin restricción' o 'Mínimo: <rango>'.");
        var ranks = game switch
        {
            "Valorant" => new[] { "Hierro", "Bronce", "Plata", "Oro", "Platino", "Diamante", "Ascendente", "Inmortal", "Radiante" },
            "League of Legends" => new[] { "Hierro", "Bronce", "Plata", "Oro", "Platino", "Esmeralda", "Diamante", "Maestro", "Gran Maestro", "Retador" },
            "Rocket League" => new[] { "Bronce", "Plata", "Oro", "Platino", "Diamante", "Campeón", "Gran Campeón", "Leyenda supersónica" },
            _ => Array.Empty<string>()
        };
        int Index(string value) { var i = Array.FindLastIndex(ranks, x => value.StartsWith(x, StringComparison.OrdinalIgnoreCase)); return i; }
        var minimum = conditions[(conditions.IndexOf(':') + 1)..].Trim();
        var actual = Index(rank); var expected = Index(minimum);
        if (actual < 0 || expected < 0) throw Error("Rango o videojuego no admitido para el mínimo especificado.");
        int Division(string value) => value.Split(' ').Last().ToUpperInvariant() switch
        { "I" => 1, "II" => 2, "III" => 3, "IV" => 4, var tail => int.TryParse(tail, out var n) ? n : 0 };
        var worseDivision = game == "League of Legends" ? Division(rank) > Division(minimum) : Division(rank) < Division(minimum);
        if (actual < expected || actual == expected && Division(minimum) != 0 && worseDivision) throw Error("El jugador no cumple el rango mínimo.");
    }
    private static TournamentParticipant Participant(TournamentEnrollment e) => new()
    {
        UserId = e.Players[0].UserId, Username = e.Players[0].Username, InGameName = e.Players[0].InGameName,
        Rank = e.Players[0].Rank, TeamId = e.TeamId, TeamName = e.TeamName
    };
    private static async Task Pay(Tournament t, TournamentEnrollment e, TournamentPayer p, WalletTransaction wallet, CancellationToken ct)
    {
        if (p.Confirmed) return;
        if (!t.IsFree) p.DebitId = (await wallet.DebitAsync(p.UserId, t.EntryPoints, "tournament_entry", t.Id, "entry:" + t.Id + ":" + e.Id, ct)).Id;
        p.Confirmed = true;
    }
    private static async Task Refund(TournamentEnrollment e, WalletTransaction wallet, CancellationToken ct)
    {
        foreach (var p in e.Players.Where(x => x.DebitId != null)) await wallet.RefundAsync(p.UserId, p.DebitId!, ct);
        e.Status = "Cancelled";
    }
    private static async Task ClosePending(Tournament t, WalletTransaction wallet, CancellationToken ct)
    {
        foreach (var e in t.Enrollments.Where(x => x.Status == "Pending")) await Refund(e, wallet, ct);
    }
    private async Task Admit(Tournament t, TournamentEnrollment e, WalletTransaction wallet, CancellationToken ct)
    {
        e.Status = "Admitted"; t.Participants.Add(Participant(e));
        if (t.Participants.Count == 8) await ClosePending(t, wallet, ct);
        await repository.Save(t, ct);
    }
    public Task<bool> Register(string id, string actor, string? teamId, CancellationToken ct) => Run(id, async (t, wallet, token) =>
    {
        if (t == null) return false;
        actor = WalletRepository.UserId(actor);
        var previous = t.Enrollments.FirstOrDefault(x => x.Status != "Cancelled" && (teamId == null ? x.TeamId == null && x.Players.Any(p => p.UserId == actor) : x.TeamId == teamId));
        if (previous != null) return true;
        Open(t);
        if ((teamId == null) != (t.TournamentType == "Individual")) throw Error("Tipo de inscripción incorrecto.");
        var e = new TournamentEnrollment();
        if (teamId == null) e.Players.Add(await Player(actor,t,token));
        else
        {
            teamId = WalletRepository.UserId(teamId);
            var team = await repository.Database.GetCollection<Team>("Teams").Find(wallet.Session, x => x.Id == teamId).FirstOrDefaultAsync(token);
            if (team == null || team.CaptainId != actor || !team.GameName.Equals(t.GameName,StringComparison.OrdinalIgnoreCase)) throw Error("Solo el capitán puede inscribir su equipo del mismo juego.");
            var starters = team.Members.Where(x => x.IsStarter).OrderByDescending(x => x.UserId == actor).ToList();
            if (starters.Count != 5 || starters.Select(x => x.UserId).Distinct().Count() != 5 || starters[0].UserId != actor) throw Error("Se requieren cinco titulares, incluido el capitán.");
            e.TeamId = team.Id; e.TeamName = team.Name;
            foreach (var member in starters) e.Players.Add(await Player(member.UserId,t,token));
        }
        if (e.Players.Any(p => t.Enrollments.Any(x => x.Status != "Cancelled" && x.Players.Any(q => q.UserId == p.UserId)))) throw Error("Un titular ya tiene una inscripción activa.");
        await Pay(t,e,e.Players[0],wallet,token); t.Enrollments.Add(e);
        if (teamId == null) await Admit(t,e,wallet,token); else await repository.Save(t,token);
        return true;
    },ct);
    public Task<object> Confirm(string id, string requestId, string actor, CancellationToken ct) => Run<object>(id, async (t,w,token) =>
    {
        if (t == null) throw Error("Torneo no encontrado.");
        var e = t.Enrollments.SingleOrDefault(x => x.Id == requestId) ?? throw Error("Solicitud no encontrada.");
        var p = e.Players.SingleOrDefault(x => x.UserId == actor) ?? throw Error("No sos titular de esta solicitud.");
        if (e.Status == "Admitted") return new { status = e.Status };
        if (e.Status != "Pending") return new { status = e.Status };
        // Closing the window is a successful state transition so refunds commit.
        if (clock.GetUtcNow().UtcDateTime >= t.StartDate || t.Participants.Count == 8)
        { await ClosePending(t,w,token); await repository.Save(t,token); return new { status = e.Status }; }
        Open(t); await Player(actor,t,token); await Pay(t,e,p,w,token);
        if (e.Players.All(x => x.Confirmed)) await Admit(t,e,w,token); else await repository.Save(t,token);
        return new { status = e.Status };
    },ct);
    public Task<object> Requests(string id,string actor,CancellationToken ct) => Run<object>(id,(t,w,token) => Task.FromResult<object>(t?.Enrollments.Where(x => x.Players.Any(p => p.UserId == actor)).ToList() ?? new()),ct,true);
    public Task<bool> CancelRequest(string id,string requestId,string actor,CancellationToken ct) => Run(id,async(t,w,token)=>
    {
        var e=t?.Enrollments.SingleOrDefault(x=>x.Id==requestId) ?? throw Error("Solicitud no encontrada.");
        if(!e.Players.Any(x=>x.UserId==actor)) throw Error("No sos titular de esta solicitud.");
        if(e.Status=="Admitted") throw Error("El equipo ya está admitido.");
        await Refund(e,w,token); await repository.Save(t!,token); return true;
    },ct);
    public async Task Cancel(Tournament t,WalletTransaction w,CancellationToken ct)
    {
        if(t.ClosedAt!=null) throw Error("El torneo está cerrado definitivamente.");
        foreach(var e in t.Enrollments.Where(x=>x.Status!="Cancelled")) await Refund(e,w,ct);
        t.Status="Cancelado"; await repository.Save(t,ct);
    }
    public Task<bool> Cancel(string id,CancellationToken ct)=>Run(id,async(t,w,token)=>{if(t==null)return false;await Cancel(t,w,token);return true;},ct,true);
    public async Task<bool> PrepareStart(Tournament t,WalletTransaction wallet,CancellationToken ct)
    {
        if(clock.GetUtcNow().UtcDateTime<t.StartDate) throw Error("El torneo todavía no alcanzó su fecha de inicio.");
        await ClosePending(t,wallet,ct);
        if(t.Participants.Count(x=>!x.IsDisqualified)!=8) { await Cancel(t,wallet,ct);return false; }
        await repository.Save(t,ct); return true;
    }
    public async Task<bool> Close(string id,string actor,CancellationToken ct)
    {
        await elo.Prepare(id,ct);
        return await Run(id,async(t,w,token)=>
    {
        if(t==null)return false; if(t.ClosedAt!=null)return true;
        if(t.Status!=TournamentStatuses.Finished)throw Error("El torneo no terminó.");
        var matches=await repository.Database.GetCollection<TournamentMatch>("Matches").Find(w.Session,x=>x.TournamentId==id).ToListAsync(token);
        if(matches.Count!=7 || matches.Any(m=>m.Status!=MatchStatuses.Finished || m.PlayerA==null || m.PlayerB==null || (m.WinnerUserId!=m.PlayerA.UserId && m.WinnerUserId!=m.PlayerB.UserId)))throw Error("Bracket incompleto o inválido.");
        foreach(var m in matches)
        {
            if(m.Round<1||m.Round>3||m.Position<0||m.Position>=(8>>m.Round))throw Error("Posición inválida.");
            if(m.Round>1) foreach(var source in matches.Where(x=>x.Round==m.Round-1 && x.Position/2==m.Position))
                if((source.Position%2==0?m.PlayerA:m.PlayerB)!.UserId!=source.WinnerUserId)throw Error("Avance de bracket inválido.");
        }
        if(matches.Select(x=>(x.Round,x.Position)).Distinct().Count()!=7)throw Error("Bracket duplicado.");
        var field = matches.Where(x=>x.Round==1).SelectMany(x=>new[]{x.PlayerA!.UserId,x.PlayerB!.UserId}).ToList();
        var entrants = t.Participants.Select(x=>x.UserId).ToHashSet();
        if (t.Participants.Count!=8 || entrants.Count!=8 || field.Distinct().Count()!=8 || field.Any(x=>!entrants.Contains(x)) || matches.Any(x=>x.IsBye)) throw Error("Participantes del bracket inválidos.");
        var admitted = t.Enrollments.Where(x=>x.Status=="Admitted").ToList();
        if (admitted.Count!=8 || admitted.Any(x=>!entrants.Contains(x.Players[0].UserId) || x.Players.Count!=(t.TournamentType=="Equipo"?5:1) || x.Players.Any(p=>!p.Confirmed)) || admitted.SelectMany(x=>x.Players.Select(p=>p.UserId)).Distinct().Count()!=(t.TournamentType=="Equipo"?40:8)) throw Error("Roster de liquidación inválido.");
        var final=matches.Single(x=>x.Round==3); var champion=final.WinnerUserId;
        foreach(var e in t.Enrollments.Where(x=>x.Status=="Admitted"))
        {
            var representative=e.Players[0].UserId;
            if(t.Participants.Single(x=>x.UserId==representative).IsDisqualified)continue;
            var played=matches.Any(m=>!m.IsBye&&!m.IsWalkover&&(m.PlayerA!.UserId==representative||m.PlayerB!.UserId==representative));
            var place=representative==champion?1:(final.PlayerA!.UserId==representative||final.PlayerB!.UserId==representative)?2:matches.Any(m=>m.Round==2&&(m.PlayerA!.UserId==representative||m.PlayerB!.UserId==representative))?3:0;
            foreach(var p in e.Players)
            {
                long amount=t.IsFree?(played?50+(place==1?100:0):0):place==1?t.ChampionPoints:place==2?t.RunnerUpPoints:place==3?t.SemifinalPoints:0;
                if(t.IsFree&&amount>0) amount=await Cap(p.UserId,amount,w,token);
                if(amount>0)await w.CreditAsync(p.UserId,amount,"tournament_prize",id,"prize:"+id,token);
            }
        }
        await elo.Process(t,matches,w.Session,token);
        t.ClosedAt=clock.GetUtcNow().UtcDateTime;t.ClosedBy=actor;t.ClosedResults=matches;await repository.Save(t,token);return true;
    },ct,true);
    }
    private async Task<long> Cap(string user,long amount,WalletTransaction w,CancellationToken ct)
    {
        var day=TimeZoneInfo.ConvertTime(clock.GetUtcNow(),TimeZoneInfo.FindSystemTimeZoneById("America/Argentina/Cordoba")).ToString("yyyy-MM-dd");
        var counters=repository.Database.GetCollection<BsonDocument>("TournamentFreeDaily");var key=user+":"+day;
        var old=await counters.Find(w.Session,new BsonDocument("_id",key)).FirstOrDefaultAsync(ct);var used=old?["Points"].ToInt64()??0;
        amount=Math.Min(amount,Math.Max(0,200-used));
        await counters.UpdateOneAsync(w.Session,new BsonDocument("_id",key),new BsonDocument("$inc",new BsonDocument("Points",amount)),new UpdateOptions{IsUpsert=true},ct);
        return amount;
    }
}
