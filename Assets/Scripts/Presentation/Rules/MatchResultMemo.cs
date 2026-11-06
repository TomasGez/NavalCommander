namespace NavalCommander.Presentation
{
    /// <summary>
    /// Responsabilidad única: conservar cómo terminó la partida para mostrarlo aunque la conexión se cierre.
    /// Si el host sale enseguida, el jugador local y el estado replicado dejan de estar disponibles.
    /// No dibuja ni decide la experiencia.
    /// </summary>
    public sealed class MatchResultMemo
    {
        private int _localSlot = -1;
        private int _winnerSlot = -1;
        private bool _failed;

        public bool HasResult => ResultText != null;
        public string ResultText { get; private set; }

        public void RememberLocalSlot(int slot)
        {
            if (slot >= 0) _localSlot = slot;
        }

        public void RememberFinish(string failureMessage, int winnerSlot)
        {
            _failed = !string.IsNullOrEmpty(failureMessage);
            _winnerSlot = winnerSlot;

            if (_failed) ResultText = $"La partida terminó con un error: {failureMessage}";
            else ResultText = winnerSlot >= 0 ? $"¡Ganó el slot {winnerSlot}!" : "Empate / sin ganador";
        }

        // LastXpGain conserva la última victoria: solo se muestra si esta partida la ganó este jugador.
        public string XpLine(int gain, int level, int xp, int xpForNextLevel)
        {
            bool won = HasResult && !_failed && _localSlot >= 0 && _winnerSlot == _localSlot;
            return won && gain > 0 ? $"+{gain} XP | Nivel {level} ({xp}/{xpForNextLevel})" : null;
        }
    }
}
