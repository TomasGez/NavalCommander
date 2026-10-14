using System;

namespace NavalCommander.Networking
{
    /// <summary>
    /// Eventos locales que la capa de red dispara y la UI escucha.
    /// Así la presentación no depende de los RPC ni de Netcode.
    /// </summary>
    public static class MatchEvents
    {
        public static event Action<string[]> HandReceived;
        public static event Action<ResolutionDto[]> TurnResolved;

        public static void RaiseHandReceived(string[] cardIds) => HandReceived?.Invoke(cardIds);
        public static void RaiseTurnResolved(ResolutionDto[] resolutions) => TurnResolved?.Invoke(resolutions);
    }
}
