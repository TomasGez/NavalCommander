namespace NavalCommander.Presentation
{
    /// <summary>
    /// Responsabilidad única: decidir qué muestra el menú inicial según la red y la búsqueda.
    /// La sala de espera solo aparece cuando terminó la búsqueda: Netcode ya escucha antes de que exista
    /// la sesión, y salir en ese momento detendría la red debajo de la operación en curso.
    /// </summary>
    public readonly struct MenuScreenState
    {
        public readonly bool ShowIdlePanel;
        public readonly bool ShowSearchingPanel;
        public readonly bool ShowWaitingPanel;
        public readonly bool SearchButtonInteractable;

        private MenuScreenState(bool idle, bool searching, bool waiting, bool interactable)
        {
            ShowIdlePanel = idle;
            ShowSearchingPanel = searching;
            ShowWaitingPanel = waiting;
            SearchButtonInteractable = interactable;
        }

        public static MenuScreenState For(bool connected, bool busy)
        {
            bool waiting = connected && !busy;
            return new MenuScreenState(!waiting, busy, waiting, !busy);
        }
    }
}
