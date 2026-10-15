using ProyectAPI.Models;

namespace ProyectAPI.Services
{
    // Arma el bracket de eliminación simple. Es lógica pura: no toca la base de datos.
    public static class BracketGenerator
    {
        public static List<TournamentMatch> Generate(Tournament tournament, Random? random = null)
        {
            var participants = tournament.Participants.Where(p => !p.IsDisqualified).ToList();
            var count = participants.Count;

            if (count < 2)
            {
                throw new InvalidOperationException("Se necesitan al menos 2 inscritos para generar las llaves.");
            }

            Shuffle(participants, random ?? Random.Shared);

            // Tamaño del bracket: la potencia de 2 igual o mayor a la cantidad de inscritos
            var size = 1;
            var totalRounds = 0;
            while (size < count)
            {
                size *= 2;
                totalRounds++;
            }

            // Se crean todas las rondas vacías hasta la final
            var rounds = new List<List<TournamentMatch>>();
            for (var round = 1; round <= totalRounds; round++)
            {
                var matchesInRound = size >> round;
                var list = new List<TournamentMatch>(matchesInRound);
                for (var position = 0; position < matchesInRound; position++)
                {
                    list.Add(new TournamentMatch
                    {
                        TournamentId = tournament.Id,
                        Round = round,
                        Position = position
                    });
                }
                rounds.Add(list);
            }

            // Primera ronda: los seeds mayores a la cantidad real de inscritos son BYE.
            // Con este orden nunca se enfrentan dos BYE entre sí.
            var seedOrder = BuildSeedOrder(size);
            for (var position = 0; position < size / 2; position++)
            {
                var seedA = seedOrder[position * 2];
                var seedB = seedOrder[position * 2 + 1];
                var match = rounds[0][position];

                match.PlayerA = seedA <= count ? ToSlot(participants[seedA - 1]) : null;
                match.PlayerB = seedB <= count ? ToSlot(participants[seedB - 1]) : null;

                if (match.PlayerA is null || match.PlayerB is null)
                {
                    var winner = match.PlayerA ?? match.PlayerB!;
                    match.IsBye = true;
                    match.Status = MatchStatuses.Finished;
                    match.WinnerUserId = winner.UserId;
                    AdvanceWinner(rounds, match, winner);
                }
            }

            return rounds.SelectMany(r => r).ToList();
        }

        // Pasa al ganador al partido que le corresponde en la ronda siguiente
        private static void AdvanceWinner(List<List<TournamentMatch>> rounds, TournamentMatch match, MatchSlot winner)
        {
            if (match.Round >= rounds.Count) return; // es la final

            var next = rounds[match.Round][match.Position / 2];
            if (match.Position % 2 == 0) next.PlayerA = winner;
            else next.PlayerB = winner;
        }

        // Orden estándar de seeds: 1 vs último, y así hasta cruzar los mejores al final
        private static List<int> BuildSeedOrder(int size)
        {
            var order = new List<int> { 1 };
            while (order.Count < size)
            {
                var next = order.Count * 2;
                order = order.SelectMany(seed => new[] { seed, next + 1 - seed }).ToList();
            }
            return order;
        }

        private static MatchSlot ToSlot(TournamentParticipant p) => new()
        {
            UserId = p.UserId,
            Username = p.Username,
            InGameName = p.InGameName
        };

        private static void Shuffle<T>(IList<T> list, Random random)
        {
            for (var i = list.Count - 1; i > 0; i--)
            {
                var j = random.Next(i + 1);
                (list[i], list[j]) = (list[j], list[i]);
            }
        }
    }
}