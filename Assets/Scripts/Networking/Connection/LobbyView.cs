using Unity.Netcode;
using UnityEngine;

namespace NavalCommander.Networking
{
    /// <summary>
    /// Responsabilidad única: dibujar la pantalla del Lobby (OnGUI provisional, solo presentación).
    /// Corre en todas las instancias, en la LobbyScene. No contiene lógica de red: delega en SessionService
    /// (ruta principal: servicios de Unity con Relay) o en NetworkLauncher (modo desarrollo por IP directa),
    /// y lee el estado de LobbyFlow (cuenta regresiva) y de PlayerNet.All (jugadores conectados).
    /// </summary>
    [RequireComponent(typeof(NetworkLauncher), typeof(LobbyFlow))]
    public class LobbyView : MonoBehaviour
    {
        private NetworkLauncher _launcher;
        private LobbyFlow _flow;
        private string _joinCodeInput = "";
        private bool _showDirectMode;
        private int _playersChoice; // 0 = 2 jugadores, 1 = 4 jugadores

        private void Awake()
        {
            _launcher = GetComponent<NetworkLauncher>();
            _flow = GetComponent<LobbyFlow>();
        }

        private void Start()
        {
            _playersChoice = _flow.RequiredPlayers >= 4 ? 1 : 0;
        }

        private void OnGUI()
        {
            var manager = NetworkManager.Singleton;
            var sessions = SessionService.Instance;

            GUILayout.BeginArea(new Rect(10, 10, 400, Screen.height - 20));
            GUILayout.Label("STRATEGY NAVAL - Lobby");

            if (manager == null) GUILayout.Label("Falta el NetworkManager en esta escena.");
            else if (!manager.IsListening) DrawEntryMenu(sessions);
            else DrawWaitingRoom(manager, sessions);

            GUILayout.Space(8);
            if (sessions != null) GUILayout.Label("Servicios: " + sessions.Status);
            GUILayout.Label("Red: " + _launcher.Status);
            GUILayout.EndArea();
        }

        // ---------------- Antes de conectar ----------------

        private void DrawEntryMenu(SessionService sessions)
        {
            GUI.enabled = !sessions.IsBusy;

            GUILayout.Label("Jugadores por partida (al crear):");
            int choice = GUILayout.Toolbar(_playersChoice, new[] { "2", "4" });
            if (choice != _playersChoice)
            {
                _playersChoice = choice;
                _flow.SetRequiredPlayers(choice == 1 ? 4 : 2);
            }

            if (GUILayout.Button("Crear partida (Relay)")) _ = sessions.CreateSessionAsync(_flow.RequiredPlayers);
            if (GUILayout.Button("Buscar partida")) _ = sessions.QuickJoinAsync(_flow.RequiredPlayers);

            GUILayout.Space(4);
            GUILayout.Label("Código de partida:");
            _joinCodeInput = GUILayout.TextField(_joinCodeInput, 12);
            if (GUILayout.Button("Unirse con código")) _ = sessions.JoinByCodeAsync(_joinCodeInput);

            if (sessions.PreviousSessionCount > 0)
            {
                GUILayout.Space(4);
                GUILayout.Label($"El servidor te tiene registrado en {sessions.PreviousSessionCount} partida(s) anterior(es).");
                if (GUILayout.Button("Limpiar partidas anteriores")) _ = sessions.CleanUpPreviousSessionsAsync();
            }

            GUI.enabled = true;
            GUILayout.Space(6);
            _showDirectMode = GUILayout.Toggle(_showDirectMode, "Modo desarrollo (IP directa)");
            if (_showDirectMode) DrawDirectMode();
        }

        private void DrawDirectMode()
        {
            GUILayout.Label($"IP de esta PC: {_launcher.LocalIps}");
            GUILayout.Label("IP del servidor / host a la que conectar:");
            _launcher.Ip = GUILayout.TextField(_launcher.Ip);
            GUILayout.Label($"Puerto: {_launcher.Port}   |   Jugadores requeridos: {_flow.RequiredPlayers}");

            if (GUILayout.Button("Host (IP directa)")) _launcher.StartHost();
            if (GUILayout.Button("Client (IP directa)")) _launcher.StartClient();
            if (GUILayout.Button("Servidor dedicado (sin jugador)")) _launcher.StartDedicatedServer();
        }

        // ---------------- Sala de espera ----------------

        private void DrawWaitingRoom(NetworkManager manager, SessionService sessions)
        {
            string role = manager.IsHost ? "Host" : manager.IsServer ? "Servidor dedicado" : "Cliente";
            GUILayout.Label(role);

            if (sessions != null && sessions.Current != null)
                GUILayout.Label($"Código de la partida: {sessions.JoinCode}   (compartilo con tu rival)");
            else if (manager.IsServer)
                GUILayout.Label($"Los clientes deben conectarse a: {_launcher.LocalIps} (puerto {_launcher.Port})");

            if (manager.IsClient && !manager.IsServer && !manager.IsConnectedClient)
            {
                GUILayout.Label("Conectando...");
            }
            else
            {
                if (manager.IsServer) GUILayout.Label($"Jugadores: {PlayerNet.All.Count} / {_flow.RequiredPlayers}");
                else GUILayout.Label($"Jugadores conectados: {PlayerNet.All.Count}");

                foreach (var player in PlayerNet.All)
                    if (player != null)
                        GUILayout.Label($"  Slot {player.Slot.Value}{(player.IsOwner ? "  (vos)" : "")}");

                if (PlayerNet.All.Count == 0 && (manager.IsConnectedClient || manager.IsServer))
                    GUILayout.Label("Conectado pero no se creó ningún jugador: revisá Player Prefab y la lista de Network Prefabs.");

                if (manager.IsServer && _flow.Countdown >= 0f)
                    GUILayout.Label($"La partida empieza en {Mathf.Ceil(_flow.Countdown)}...");
                else GUILayout.Label("Esperando jugadores...");
            }

            if (GUILayout.Button("Salir")) Leave(sessions);
        }

        private void Leave(SessionService sessions)
        {
            if (sessions != null && sessions.Current != null) _ = sessions.LeaveAsync();
            else _launcher.Disconnect();
        }
    }
}
