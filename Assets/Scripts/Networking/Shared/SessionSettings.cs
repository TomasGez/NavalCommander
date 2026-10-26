namespace NavalCommander.Networking
{
    /// <summary>
    /// Constantes de la sesión. La cantidad de jugadores requeridos de cada partida NO vive acá:
    /// es configuración de LobbyFlow y se pasa de forma explícita a quien la necesite.
    /// </summary>
    public static class SessionSettings
    {
        public const int MinPlayers = 2;
        public const int MaxPlayers = 4;
    }
}
