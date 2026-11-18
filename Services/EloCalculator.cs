namespace ProyectAPI.Services;

public static class EloCalculator
{
    public const int Initial = 1000;
    public const int K = 32;
    public const int Version = 1;
    public static double Expected(int rating, double opponent) => 1.0 / (1.0 + Math.Pow(10, (opponent - rating) / 400.0));
    public static int Next(int rating, double opponent, bool won) => checked((int)Math.Round(rating + K * ((won ? 1 : 0) - Expected(rating, opponent)), MidpointRounding.AwayFromZero));
    public static string Game(string? value) => value?.Trim().ToLowerInvariant() switch
    {
        "league of legends" => "League of Legends",
        "valorant" => "Valorant",
        "cs:go" => "CS:GO",
        "rocket league" => "Rocket League",
        _ => throw new WalletOperationException(400, "invalid_game", "Choose League of Legends, Valorant, CS:GO or Rocket League.")
    };
}
