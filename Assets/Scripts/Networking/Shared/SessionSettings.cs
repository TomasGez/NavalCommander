namespace NavalCommander.Networking
{
    /// <summary>Parámetros de la sesión compartidos entre el Lobby y los sistemas de red.</summary>
    public static class SessionSettings
    {
        public const int MaxPlayers = 4;

        /// <summary>Jugadores necesarios para empezar (2 o 4). Lo configura el Lobby.</summary>
        public static int RequiredPlayers = 2;
    }
}
