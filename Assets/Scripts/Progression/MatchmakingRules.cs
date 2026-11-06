using System.Collections.Generic;

namespace NavalCommander.Progression
{
    /// <summary>Reglas de emparejamiento, sin servicios de red, para poder probarlas solas.</summary>
    public static class MatchmakingRules
    {
        /// <summary>
        /// Sala a la que debe mudarse un host que espera solo, o null si debe quedarse.
        /// Solo se muda hacia un id menor: de dos hosts que se ven entre sí, se mueve exactamente uno.
        /// </summary>
        public static string RoomToJoin(string ownRoomId, IEnumerable<string> visibleRoomIds, ICollection<string> failedRoomIds)
        {
            string target = null;
            foreach (string id in visibleRoomIds)
            {
                if (failedRoomIds.Contains(id)) continue;
                if (string.CompareOrdinal(id, ownRoomId) >= 0) continue;
                if (target == null || string.CompareOrdinal(id, target) < 0) target = id;
            }
            return target;
        }
    }
}
