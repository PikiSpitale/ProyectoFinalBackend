using System.Text.RegularExpressions;

namespace ProyectAPI.Services
{
    // Validación de filtros de rango para equipos públicos.
    // Soporta: tiers con subdivisión (Valorant/LoL/RL), Premier numérico (CS2), divisiones Rivals (EA FC/FIFA).
    public static class GameRankLadders
    {
        private static readonly string[] ValorantTiers = ["Hierro", "Bronce", "Plata", "Oro", "Platino", "Diamante", "Ascendente", "Inmortal", "Radiante"];
        private static readonly string[] LolTiers = ["Hierro", "Bronce", "Plata", "Oro", "Platino", "Esmeralda", "Diamante", "Maestro", "Gran Maestro", "Retador", "Aspirante", "Challenger"];
        private static readonly string[] RlTiers = ["Bronce", "Plata", "Oro", "Platino", "Diamante", "Campeón", "Champion", "Gran Campeón", "Grand Champion", "Supersónico", "Supersonic Legend", "SSL"];

        public static bool HasLadder(string gameName)
        {
            var g = NormGame(gameName);
            return IsValorant(g) || IsLol(g) || IsRl(g) || IsCsPremier(g) || IsFifa(g);
        }

        public static bool IsRecognizedRank(string gameName, string rank)
        {
            if (string.IsNullOrWhiteSpace(rank)) return false;
            var g = NormGame(gameName);
            if (IsValorant(g)) return TryParseValorant(rank, out _);
            if (IsLol(g)) return TryParseLol(rank, out _);
            if (IsRl(g)) return TryParseRl(rank, out _);
            if (IsCsPremier(g)) return TryParsePremier(rank, out _);
            if (IsFifa(g)) return TryParseFifaDivision(rank, out _);
            return false;
        }

        public static bool MeetsMinimum(string gameName, string minRank, string userRank)
        {
            if (string.IsNullOrWhiteSpace(minRank) || string.IsNullOrWhiteSpace(userRank)) return false;
            var g = NormGame(gameName);
            if (IsValorant(g)) return TryParseValorant(minRank, out var min) && TryParseValorant(userRank, out var usr) && usr >= min;
            if (IsLol(g)) return TryParseLol(minRank, out var minL) && TryParseLol(userRank, out var usrL) && usrL >= minL;
            if (IsRl(g)) return TryParseRl(minRank, out var minR) && TryParseRl(userRank, out var usrR) && usrR >= minR;
            if (IsCsPremier(g)) return TryParsePremier(minRank, out var minP) && TryParsePremier(userRank, out var usrP) && usrP >= minP;
            if (IsFifa(g)) return TryParseFifaDivision(minRank, out var minF) && TryParseFifaDivision(userRank, out var usrF) && usrF >= minF; // mayor score = mejor (Div 1)
            return false;
        }

        public static bool MatchesFixed(string gameName, string fixedRank, string userRank)
        {
            if (string.IsNullOrWhiteSpace(fixedRank) || string.IsNullOrWhiteSpace(userRank)) return false;
            var g = NormGame(gameName);
            if (IsCsPremier(g))
                return TryParsePremier(fixedRank, out var f) && TryParsePremier(userRank, out var u) && f == u;
            if (IsFifa(g))
                return TryParseFifaDivision(fixedRank, out var f) && TryParseFifaDivision(userRank, out var u) && f == u;
            // Tiers: comparar score normalizado (tolerante a case / I vs 1)
            if (IsValorant(g)) return TryParseValorant(fixedRank, out var fv) && TryParseValorant(userRank, out var uv) && fv == uv;
            if (IsLol(g)) return TryParseLol(fixedRank, out var fl) && TryParseLol(userRank, out var ul) && fl == ul;
            if (IsRl(g)) return TryParseRl(fixedRank, out var fr) && TryParseRl(userRank, out var ur) && fr == ur;
            return Canon(fixedRank) == Canon(userRank);
        }

        private static string NormGame(string? game) => (game ?? "").Trim().ToLowerInvariant();
        private static bool IsValorant(string g) => g == "valorant";
        private static bool IsLol(string g) => g is "league of legends" or "lol";
        private static bool IsRl(string g) => g == "rocket league";
        private static bool IsCsPremier(string g) => g is "counter-strike 2" or "counter strike 2" or "cs2" or "cs:go" or "counter-strike: global offensive";
        private static bool IsFifa(string g) => g is "ea fc 27" or "ea fc 26" or "ea fc 25" or "ea fc" or "fifa" or "fifa 23" || g.Contains("fc 2") || g.StartsWith("fifa");

        private static bool TryParseValorant(string rank, out int score)
        {
            // score = tierIndex*10 + sub (1-3). Radiante = max.
            score = -1;
            var s = Canon(rank);
            if (s.Contains("radiante") || s.Contains("radiant")) { score = 8 * 10 + 9; return true; }
            var tier = FindTierIndex(s, ValorantTiers);
            if (tier < 0) return false;
            var sub = ExtractSubRank(s);
            if (sub is < 1 or > 3) sub = 1;
            score = tier * 10 + sub;
            return true;
        }

        private static bool TryParseLol(string rank, out int score)
        {
            score = -1;
            var s = Canon(rank);
            var tier = FindTierIndex(s, LolTiers);
            if (tier < 0) return false;
            // Maestro+ sin división
            var hasNoDiv = s.Contains("maestro") || s.Contains("master") || s.Contains("gran") || s.Contains("grand") || s.Contains("aspirante") || s.Contains("retador") || s.Contains("challenger");
            if (hasNoDiv) { score = tier * 10 + 5; return true; }
            var sub = ExtractLolDiv(s); // IV=1 (peor) .. I=4 (mejor)
            if (sub is < 1 or > 4) sub = 1;
            score = tier * 10 + sub;
            return true;
        }

        private static bool TryParseRl(string rank, out int score)
        {
            score = -1;
            var s = Canon(rank);
            // Leyenda Supersónica (SSL): tope, sin subdivisiones
            if (s.Contains("leyenda") || s.Contains("superson") || s.Contains("ssl"))
            {
                score = 7 * 10 + 9;
                return true;
            }
            // Map aliases to canonical tier order (Gran Campeón antes que Campeón)
            int tier = s.Contains("gran camp") || s.Contains("grand champ") ? 6
                : s.Contains("campe") || s.Contains("champ") ? 5
                : s.Contains("diamante") || s.Contains("diamond") ? 4
                : s.Contains("platino") || s.Contains("platinum") ? 3
                : s.Contains("oro") || s.Contains("gold") ? 2
                : s.Contains("plata") || s.Contains("silver") ? 1
                : s.Contains("bronce") || s.Contains("bronze") ? 0
                : -1;
            if (tier < 0) return false;
            var sub = ExtractSubRank(s, max: 3);
            if (sub is < 1 or > 3) sub = 1;
            score = tier * 10 + sub;
            return true;
        }

        private static bool TryParsePremier(string rank, out int points)
        {
            points = -1;
            var m = Regex.Match(rank ?? "", @"(\d[\d\s\.,]*)");
            if (!m.Success) return false;
            var digits = Regex.Replace(m.Groups[1].Value, @"[^\d]", "");
            if (!int.TryParse(digits, out points)) return false;
            return points >= 0;
        }

        private static bool TryParseFifaDivision(string rank, out int score)
        {
            // División 9 (baja) -> score 1, ... División 1 (alta) -> score 9
            score = -1;
            var m = Regex.Match(rank ?? "", @"(\d+)");
            if (!m.Success) return false;
            if (!int.TryParse(m.Groups[1].Value, out var div)) return false;
            if (div is < 1 or > 10) return false;
            score = 10 - div;
            return true;
        }

        private static int FindTierIndex(string canon, string[] tiers)
        {
            for (int i = 0; i < tiers.Length; i++)
            {
                var t = Canon(tiers[i]);
                if (canon.Contains(t)) return i;
            }
            // English fallbacks common in profiles
            if (canon.Contains("iron")) return Array.FindIndex(tiers, x => Canon(x).Contains("hierro"));
            if (canon.Contains("bronze")) return Array.FindIndex(tiers, x => Canon(x).Contains("bronce"));
            if (canon.Contains("silver")) return Array.FindIndex(tiers, x => Canon(x).Contains("plata"));
            if (canon.Contains("gold")) return Array.FindIndex(tiers, x => Canon(x).Contains("oro"));
            if (canon.Contains("platinum")) return Array.FindIndex(tiers, x => Canon(x).Contains("platino"));
            if (canon.Contains("diamond")) return Array.FindIndex(tiers, x => Canon(x).Contains("diamante"));
            if (canon.Contains("ascendant")) return Array.FindIndex(tiers, x => Canon(x).Contains("ascend"));
            if (canon.Contains("immortal")) return Array.FindIndex(tiers, x => Canon(x).Contains("inmortal"));
            return -1;
        }

        private static int ExtractSubRank(string canon, int max = 3)
        {
            // roman or arabic at end/token
            if (Regex.IsMatch(canon, @"\biii\b") || canon.TrimEnd().EndsWith("3")) return Math.Min(3, max);
            if (Regex.IsMatch(canon, @"\bii\b") || canon.TrimEnd().EndsWith("2")) return Math.Min(2, max);
            if (Regex.IsMatch(canon, @"\bi\b") || canon.TrimEnd().EndsWith("1")) return 1;
            var m = Regex.Match(canon, @"\b([1-3])\b");
            if (m.Success && int.TryParse(m.Groups[1].Value, out var n)) return n;
            return 1;
        }

        private static int ExtractLolDiv(string canon)
        {
            // IV peor(1) .. I mejor(4)
            if (Regex.IsMatch(canon, @"\biv\b") || canon.Contains(" 4") || canon.EndsWith("4")) return 1;
            if (Regex.IsMatch(canon, @"\biii\b") || canon.Contains(" 3") || canon.EndsWith("3")) return 2;
            if (Regex.IsMatch(canon, @"\bii\b") || canon.Contains(" 2") || canon.EndsWith("2")) return 3;
            if (Regex.IsMatch(canon, @"\bi\b") || canon.Contains(" 1") || canon.EndsWith("1")) return 4;
            var m = Regex.Match(canon, @"\b([1-4])\b");
            if (m.Success && int.TryParse(m.Groups[1].Value, out var n)) return n == 4 ? 1 : n == 3 ? 2 : n == 2 ? 3 : 4; // if someone used 4 as worst
            return 1;
        }

        private static string Canon(string s) => Regex.Replace((s ?? "").Trim().ToLowerInvariant(), @"\s+", " ");
    }
}
