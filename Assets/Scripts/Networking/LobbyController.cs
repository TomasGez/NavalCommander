using System.Net;
using System.Net.Sockets;
using System.Text;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace NavalCommander.Networking
{
    /// <summary>
    /// Escena Lobby: conexión (Host / Client / Server), espera de jugadores y carga de la GameScene.
    /// En un build de servidor dedicado (UNITY_SERVER) arranca solo, leyendo -port y -players.
    /// Muestra el estado de la conexión y el motivo si falla.
    /// </summary>
    public class LobbyController : MonoBehaviour
    {
        /// <summary>Cantidad de jugadores que necesita la partida (lo lee PlayerNet para rechazar de más).</summary>
        public static int RequiredPlayers = 2;

        [SerializeField] private string ip = "127.0.0.1";
        [SerializeField] private ushort port = 7777;
        [SerializeField] private int requiredPlayers = 2;      // 2 o 4
        [SerializeField] private string gameSceneName = "GameScene";
        [SerializeField] private float startDelay = 2f;

        private float _countdown = -1f;
        private bool _loading;
        private string _status = "Sin conexión";
        private string _localIps = "?";
        private NetworkManager _subscribedTo;

        private void Awake() { RequiredPlayers = requiredPlayers; }

        private void Start()
        {
            _localIps = GetLocalIps();

            var nm = NetworkManager.Singleton;
            if (nm != null)
            {
                _subscribedTo = nm;
                nm.OnClientConnectedCallback += OnConnected;
                nm.OnClientDisconnectCallback += OnDisconnected;
                nm.OnTransportFailure += OnTransportFailure;
            }

#if UNITY_SERVER
            ParseArgs();
            Application.targetFrameRate = 30;
            StartServerNow();
#endif
        }

        private void OnDestroy()
        {
            if (_subscribedTo == null) return;
            _subscribedTo.OnClientConnectedCallback -= OnConnected;
            _subscribedTo.OnClientDisconnectCallback -= OnDisconnected;
            _subscribedTo.OnTransportFailure -= OnTransportFailure;
        }

#if UNITY_SERVER
        // Ejemplo: NavalServer.exe -batchmode -nographics -port 7777 -players 2
        private void ParseArgs()
        {
            string[] args = System.Environment.GetCommandLineArgs();
            for (int i = 0; i < args.Length - 1; i++)
            {
                if (args[i] == "-port" && ushort.TryParse(args[i + 1], out var p)) port = p;
                if (args[i] == "-players" && int.TryParse(args[i + 1], out var n)) requiredPlayers = Mathf.Clamp(n, 2, 4);
            }
            RequiredPlayers = requiredPlayers;
        }
#endif

        // ---------------- Diagnóstico de conexión ----------------

        private void OnConnected(ulong clientId)
        {
            var nm = NetworkManager.Singleton;
            if (nm != null && clientId == nm.LocalClientId) _status = "Conectado";
            Debug.Log($"[Net] Cliente conectado: {clientId}");
        }

        private void OnDisconnected(ulong clientId)
        {
            var nm = NetworkManager.Singleton;
            if (nm == null) return;

            // En el servidor este evento también avisa de OTROS clientes que se van.
            if (nm.IsServer && clientId != nm.LocalClientId)
            {
                _status = $"Se desconectó el cliente {clientId}";
                Debug.Log($"[Net] {_status}");
                return;
            }

            string reason = nm.DisconnectReason;
            _status = string.IsNullOrEmpty(reason)
                ? "No se pudo conectar o se perdió la conexión. Revisá IP, puerto y firewall."
                : "Desconectado: " + reason;
            Debug.LogWarning($"[Net] {_status}");
        }

        private void OnTransportFailure()
        {
            _status = "Falló el transporte de red (¿el puerto ya está en uso por otro Unity abierto?)";
            Debug.LogError($"[Net] {_status}");
        }

        /// <summary>Devuelve un texto de error si el NetworkManager está mal configurado, o null si está bien.</summary>
        private string Validate(NetworkManager nm)
        {
            if (nm.NetworkConfig == null) return "El NetworkManager no tiene configuración.";
            if (nm.NetworkConfig.NetworkTransport == null)
                return "El NetworkManager no tiene Transport. En el Inspector: 'Select transport' > UnityTransport.";
            if (!(nm.NetworkConfig.NetworkTransport is UnityTransport))
                return "El transport no es UnityTransport.";
            if (nm.NetworkConfig.PlayerPrefab == null)
                return "Falta el Player Prefab en el NetworkManager (arrastrá el prefab PlayerNet).";
            if (!nm.NetworkConfig.EnableSceneManagement)
                return "Tildá 'Enable Scene Management' en el NetworkManager.";
            return null;
        }

        private static string GetLocalIps()
        {
            try
            {
                var sb = new StringBuilder();
                foreach (var addr in Dns.GetHostEntry(Dns.GetHostName()).AddressList)
                    if (addr.AddressFamily == AddressFamily.InterNetwork) sb.Append(addr).Append("   ");
                return sb.Length > 0 ? sb.ToString() : "no detectada";
            }
            catch { return "no detectada"; }
        }

        // ---------------- Arranque de red ----------------

        private bool Prepare(NetworkManager nm, out UnityTransport utp)
        {
            utp = null;
            string error = Validate(nm);
            if (error != null)
            {
                _status = "ERROR: " + error;
                Debug.LogError($"[Net] {_status}");
                return false;
            }
            utp = (UnityTransport)nm.NetworkConfig.NetworkTransport;
            utp.MaxConnectAttempts = 5; // falla en ~5 s en vez de esperar un minuto
            return true;
        }

        private void StartHostNow(NetworkManager nm)
        {
            if (!Prepare(nm, out var utp)) return;
            RequiredPlayers = requiredPlayers;
            utp.SetConnectionData(ip, port, "0.0.0.0"); // escucha en todas las interfaces
            _status = nm.StartHost() ? "Host iniciado" : "No se pudo iniciar el host";
        }

        private void StartClientNow(NetworkManager nm)
        {
            if (!Prepare(nm, out var utp)) return;
            utp.SetConnectionData(ip, port);
            _status = $"Conectando a {ip}:{port} ...";
            if (!nm.StartClient()) _status = "No se pudo iniciar el cliente";
        }

        private void StartServerNow()
        {
            var nm = NetworkManager.Singleton;
            if (nm == null || !Prepare(nm, out var utp)) return;
            RequiredPlayers = requiredPlayers;
            utp.SetConnectionData("127.0.0.1", port, "0.0.0.0");
            _status = nm.StartServer() ? $"Servidor dedicado escuchando en el puerto {port}" : "No se pudo iniciar el servidor";
            Debug.Log($"[Server] {_status}, esperando {RequiredPlayers} jugadores");
        }

        private void Update()
        {
            var nm = NetworkManager.Singleton;
            if (nm == null || !nm.IsServer || _loading) return;

            int ready = 0;
            foreach (var p in PlayerNet.All)
                if (p != null && p.Slot.Value >= 0) ready++;

            if (ready >= RequiredPlayers)
            {
                if (_countdown < 0f) _countdown = startDelay;
                _countdown -= Time.deltaTime;
                if (_countdown <= 0f)
                {
                    _loading = true;
                    nm.SceneManager.LoadScene(gameSceneName, LoadSceneMode.Single); // todos los clientes cargan la escena
                }
            }
            else _countdown = -1f;
        }

        // ---------------- UI ----------------

        private void OnGUI()
        {
            var nm = NetworkManager.Singleton;
            GUILayout.BeginArea(new Rect(10, 10, 380, Screen.height - 20));
            GUILayout.Label("STRATEGY NAVAL - Lobby");

            if (nm == null) GUILayout.Label("Falta el NetworkManager en esta escena.");
            else if (!nm.IsListening) DrawConnect(nm);
            else DrawWaiting(nm);

            GUILayout.Space(8);
            GUILayout.Label("Estado: " + _status);
            GUILayout.EndArea();
        }

        private void DrawConnect(NetworkManager nm)
        {
            GUILayout.Label($"IP de esta PC: {_localIps}");
            GUILayout.Label("IP del servidor / host a la que conectar:");
            ip = GUILayout.TextField(ip);
            GUILayout.Label("(misma PC: 127.0.0.1 | otra PC de la red: la IP del host)");
            GUILayout.Label($"Puerto: {port}   |   Jugadores requeridos: {requiredPlayers}");

            if (GUILayout.Button("Host (servidor + jugador)")) StartHostNow(nm);
            if (GUILayout.Button("Client")) StartClientNow(nm);
            if (GUILayout.Button("Servidor dedicado (sin jugador)")) StartServerNow();
        }

        private void DrawWaiting(NetworkManager nm)
        {
            string role = nm.IsHost ? "Host" : nm.IsServer ? "Servidor dedicado" : "Cliente";
            GUILayout.Label($"{role} | puerto {port}");

            if (nm.IsServer) GUILayout.Label($"Los clientes deben conectarse a: {_localIps}");

            if (nm.IsClient && !nm.IsServer && !nm.IsConnectedClient)
            {
                GUILayout.Label($"Conectando a {ip}:{port} ...");
            }
            else
            {
                GUILayout.Label($"Jugadores: {PlayerNet.All.Count} / {RequiredPlayers}");
                foreach (var p in PlayerNet.All)
                    if (p != null)
                        GUILayout.Label($"  Slot {p.Slot.Value}{(p.IsOwner ? "  (vos)" : "")}");

                if (PlayerNet.All.Count == 0 && (nm.IsConnectedClient || nm.IsServer))
                    GUILayout.Label("Conectado pero no se creó ningún jugador: revisá Player Prefab y la lista de Network Prefabs.");

                if (nm.IsServer && _countdown >= 0f) GUILayout.Label($"La partida empieza en {Mathf.Ceil(_countdown)}...");
                else GUILayout.Label("Esperando jugadores...");
            }

            if (GUILayout.Button("Desconectar")) { nm.Shutdown(); _status = "Desconectado"; }
        }
    }
}
