using System;

namespace NavalCommander.Networking
{
    /// <summary>
    /// Eventos locales que la capa de red dispara y la UI escucha.
    /// Así la presentación no depende de los RPC ni de Netcode.
    /// Se disparan en cada cliente (y en el host) cuando llega un RPC: PlayerNet (mano privada) y
    /// MatchController (informe del turno). Los escuchan MatchHud y TurnEffectsView; nunca viajan por la red.
    /// </summary>
    public static class MatchEvents
    {
        public static event Action<string[]> HandReceived;
        public static event Action<ResolutionDto[]> TurnResolved;
        public static event Action<ResolutionDto[], HitDto[]> TurnResolvedDetailed; // con los impactos, para animar

        public static void RaiseHandReceived(string[] cardIds) => HandReceived?.Invoke(cardIds);

        public static void RaiseTurnResolved(ResolutionDto[] resolutions, HitDto[] hits)
        {
            TurnResolved?.Invoke(resolutions);
            TurnResolvedDetailed?.Invoke(resolutions, hits);
        }
    }
}
