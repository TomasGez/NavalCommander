namespace NavalCommander.Networking
{
    /// <summary>Fases de la partida. El servidor la cambia y se replica a todos los clientes.</summary>
    public enum MatchPhase : byte
    {
        Lobby,      // la partida todavía no empezó
        Selecting,  // los jugadores eligen cartas en secreto
        Resolving,  // el servidor ya resolvió; se muestra el resultado
        Finished    // hay ganador
    }
}
