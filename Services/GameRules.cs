namespace ProyectAPI.Services
{
    // Reglas por videojuego. Por ahora solo define el puntaje que gana el partido automáticamente.
    // Para agregar un juego o modo nuevo, sumá un caso acá.
    public static class GameRules
    {
        // Puntaje que gana la partida solo (null = no hay victoria automática,
        // el moderador declara al ganador con el botón ✓)
        public static int? GetWinScore(string gameName) => gameName switch
        {
            "Valorant" => 13,
            "Counter-Strike 2" => 13,
            _ => null
        };
    }
}