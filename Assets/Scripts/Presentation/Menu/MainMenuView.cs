using NavalCommander.Networking;
using NavalCommander.Progression;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.UI;

namespace NavalCommander.Presentation
{
    /// <summary>
    /// Responsabilidad única: dibujar el menú inicial (uGUI, solo presentación). Vive en la MenuScene.
    /// Muestra uno de tres estados según la red: inicio con "Buscar partida", búsqueda en curso o sala de
    /// espera. No decide nada: delega la búsqueda en SessionService y lee el inicio de partida de LobbyFlow.
    /// El interruptor de desarrollo habilita LobbyView, que conserva la ruta por IP directa.
    /// </summary>
    public class MainMenuView : MonoBehaviour
    {
        [Header("Jugador")]
        [SerializeField] private InputField nameInput;
        [SerializeField] private Text levelText;
        [SerializeField] private Text xpText;
        [SerializeField] private RectTransform xpFill;

        [Header("Estados")]
        [SerializeField] private GameObject idlePanel;
        [SerializeField] private GameObject searchingPanel;
        [SerializeField] private GameObject waitingPanel;

        [Header("Inicio")]
        [SerializeField] private Button searchButton;
        [SerializeField] private Text idleStatusText;

        [Header("Búsqueda")]
        [SerializeField] private Text searchingStatusText;

        [Header("Sala de espera")]
        [SerializeField] private Text roomCodeText;
        [SerializeField] private Text[] playerRows;
        [SerializeField] private Text countdownText;
        [SerializeField] private Button leaveButton;

        [Header("Desarrollo")]
        [SerializeField] private Toggle developmentToggle;
        [SerializeField] private GameObject mainColumn;

        [Header("Red")]
        [SerializeField] private LobbyFlow flow;
        [SerializeField] private LobbyView directRouteView;
        [SerializeField] private NetworkLauncher launcher;

        // El estado de los servicios solo se muestra después de intentar buscar: antes no le dice nada al jugador.
        private bool _searchAttempted;

        private void Awake()
        {
            searchButton.onClick.AddListener(Search);
            leaveButton.onClick.AddListener(Leave);
            // El interruptor es opcional: sin él, la escena no ofrece la ruta de desarrollo.
            if (developmentToggle != null) developmentToggle.onValueChanged.AddListener(SetDevelopmentMode);
            SetDevelopmentMode(developmentToggle != null && developmentToggle.isOn);

            nameInput.onEndEdit.AddListener(SaveName);
            PlayerProgression.Changed += DrawPlayer;
            DrawPlayer();
            PlaceSearchingPanelBelowSearchButton();
        }

        // Durante la búsqueda "Buscar partida" sigue a la vista, desactivado: el estado va donde estaría el texto de inicio.
        private void PlaceSearchingPanelBelowSearchButton()
        {
            var idle = (RectTransform)idlePanel.transform;
            var searching = (RectTransform)searchingPanel.transform;
            searching.anchoredPosition = idle.anchoredPosition + new Vector2(0f, idleStatusText.rectTransform.anchoredPosition.y);
        }

        private void OnDestroy()
        {
            searchButton.onClick.RemoveListener(Search);
            leaveButton.onClick.RemoveListener(Leave);
            if (developmentToggle != null) developmentToggle.onValueChanged.RemoveListener(SetDevelopmentMode);
            nameInput.onEndEdit.RemoveListener(SaveName);
            PlayerProgression.Changed -= DrawPlayer;
        }

        private void DrawPlayer()
        {
            nameInput.SetTextWithoutNotify(PlayerProgression.DisplayName);
            levelText.text = PlayerProgression.Level.ToString();

            int xp = PlayerProgression.Xp;
            int needed = PlayerProgression.XpForNextLevel;
            int gain = PlayerProgression.LastXpGain;
            xpText.text = gain > 0 ? $"{xp} / {needed} XP   (+{gain} en la última victoria)" : $"{xp} / {needed} XP";

            float progress = needed > 0 ? Mathf.Clamp01((float)xp / needed) : 0f;
            xpFill.anchorMax = new Vector2(progress, 1f);
        }

        private static void SaveName(string value)
        {
            PlayerProgression.DisplayName = value;
        }

        private void Update()
        {
            var manager = NetworkManager.Singleton;
            var sessions = SessionService.ExistingInstance;
            bool connected = manager != null && manager.IsListening;
            bool busy = sessions != null && sessions.IsBusy;

            MenuScreenState screen = MenuScreenState.For(connected, busy);
            idlePanel.SetActive(screen.ShowIdlePanel);
            searchingPanel.SetActive(screen.ShowSearchingPanel);
            waitingPanel.SetActive(screen.ShowWaitingPanel);
            searchButton.interactable = screen.SearchButtonInteractable;

            if (screen.ShowWaitingPanel) DrawWaitingRoom(manager, sessions);
            if (screen.ShowSearchingPanel) searchingStatusText.text = sessions.Status;
            if (screen.ShowIdlePanel) idleStatusText.text = !busy && _searchAttempted && sessions != null ? sessions.Status : "";
        }

        private void DrawWaitingRoom(NetworkManager manager, SessionService sessions)
        {
            bool inSession = sessions != null && sessions.Current != null;
            int capacity = inSession ? sessions.Current.MaxPlayers : flow.RequiredPlayers;

            roomCodeText.text = inSession ? $"Código de la partida: {sessions.JoinCode}" : "";

            for (int row = 0; row < playerRows.Length; row++)
            {
                playerRows[row].gameObject.SetActive(row < capacity);
                playerRows[row].text = row < PlayerNet.All.Count ? DescribePlayer(PlayerNet.All[row]) : "Esperando jugador...";
            }

            countdownText.text = DescribeMatchStart(manager, capacity);
        }

        private static string DescribePlayer(PlayerNet player)
        {
            if (player == null || player.Slot.Value < 0) return "Uniéndose...";
            return player.IsOwner ? $"{PlayerProgression.DisplayName} (tú)" : $"Jugador {player.Slot.Value + 1}";
        }

        // LobbyFlow solo cuenta en el servidor: los clientes no conocen los segundos que faltan.
        private string DescribeMatchStart(NetworkManager manager, int capacity)
        {
            if (manager.IsClient && !manager.IsServer && !manager.IsConnectedClient) return "Conectando...";
            if (manager.IsServer && flow.Countdown >= 0f) return $"La partida comienza en {Mathf.Ceil(flow.Countdown)}...";
            return PlayerNet.All.Count >= capacity ? "La partida está por comenzar..." : "Esperando jugadores...";
        }

        private void Search()
        {
            _searchAttempted = true;
            _ = SessionService.Instance.QuickJoinAsync(flow.RequiredPlayers);
        }

        private void Leave()
        {
            var sessions = SessionService.ExistingInstance;
            if (sessions != null && sessions.Current != null) _ = sessions.LeaveAsync();
            else launcher.Disconnect();
        }

        // LobbyView dibuja con OnGUI en una franja fija y sus clics también llegan a uGUI:
        // se oculta la columna para que un botón de desarrollo no presione "Buscar partida" debajo.
        private void SetDevelopmentMode(bool enabledMode)
        {
            directRouteView.enabled = enabledMode;
            mainColumn.SetActive(!enabledMode);
        }
    }
}
