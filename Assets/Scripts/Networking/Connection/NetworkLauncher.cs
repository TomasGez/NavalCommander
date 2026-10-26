using System.Net;
using System.Net.Sockets;
using System.Text;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using UnityEngine;

namespace NavalCommander.Networking
{
    /// <summary>
    /// Ruta de DESARROLLO por IP directa (UnityTransport sin servicios de Unity): inicia y corta la conexión
    /// como Host, Client o Servidor dedicado, y explica qué pasa (estado, errores, motivo de desconexión).
    /// La ruta principal del juego es SessionService (Authentication + Lobby + Relay); este componente
    /// se usa para probar en LAN/local o con un servidor dedicado, desde el modo desarrollo de LobbyView.
    /// También ofrece FindConfigurationError, que SessionService reutiliza para validar el NetworkManager.
    /// En un build de servidor dedicado (UNITY_SERVER) arranca solo, leyendo -port y -players.
    /// Vive en la LobbyScene y corre en todas las instancias.
    /// </summary>
    [RequireComponent(typeof(LobbyFlow))]
    public class NetworkLauncher : MonoBehaviour
    {
        [SerializeField] private string ip = "127.0.0.1";
        [SerializeField] private ushort port = 7777;

        private NetworkManager _manager;
        private LobbyFlow _flow;

        public string Ip { get => ip; set => ip = value; }
        public ushort Port => port;
        public string Status { get; private set; } = "Sin conexión";
        public string LocalIps { get; private set; } = "?";

        private void Start()
        {
            LocalIps = ReadLocalIps();
            _flow = GetComponent<LobbyFlow>();
            _manager = NetworkManager.Singleton;
            if (_manager != null)
            {
                _manager.OnClientConnectedCallback += HandleConnected;
                _manager.OnClientDisconnectCallback += HandleDisconnected;
                _manager.OnTransportFailure += HandleTransportFailure;
            }

#if UNITY_SERVER
            ReadCommandLineArgs();
            Application.targetFrameRate = 30;
            StartDedicatedServer();
#endif
        }

        private void OnDestroy()
        {
            if (_manager == null) return;
            _manager.OnClientConnectedCallback -= HandleConnected;
            _manager.OnClientDisconnectCallback -= HandleDisconnected;
            _manager.OnTransportFailure -= HandleTransportFailure;
        }

        // ---------------- Acciones ----------------

        public void StartHost()
        {
            if (!TryPrepare(out var transport)) return;
            transport.SetConnectionData(ip, port, "0.0.0.0"); // escucha en todas las interfaces
            Status = _manager.StartHost() ? "Host iniciado" : "No se pudo iniciar el host";
        }

        public void StartClient()
        {
            if (!TryPrepare(out var transport)) return;
            transport.SetConnectionData(ip, port);
            Status = $"Conectando a {ip}:{port} ...";
            if (!_manager.StartClient()) Status = "No se pudo iniciar el cliente";
        }

        public void StartDedicatedServer()
        {
            if (!TryPrepare(out var transport)) return;
            transport.SetConnectionData("127.0.0.1", port, "0.0.0.0");
            Status = _manager.StartServer()
                ? $"Servidor dedicado escuchando en el puerto {port}"
                : "No se pudo iniciar el servidor";
            Debug.Log($"[Server] {Status}, esperando {_flow.RequiredPlayers} jugadores");
        }

        public void Disconnect()
        {
            if (_manager != null) _manager.Shutdown();
            Status = "Desconectado";
        }

        // ---------------- Validación ----------------

        private bool TryPrepare(out UnityTransport transport)
        {
            transport = null;
            _manager = NetworkManager.Singleton;
            string error = FindConfigurationError(_manager);
            if (error != null)
            {
                Status = "ERROR: " + error;
                Debug.LogError($"[Net] {Status}");
                return false;
            }

            transport = (UnityTransport)_manager.NetworkConfig.NetworkTransport;
            transport.MaxConnectAttempts = 5; // falla en ~5 s en vez de esperar un minuto
            return true;
        }

        /// <summary>Devuelve un texto explicando qué le falta al NetworkManager, o null si está bien configurado.</summary>
        public static string FindConfigurationError(NetworkManager manager)
        {
            if (manager == null) return "Falta el NetworkManager en la escena.";
            if (manager.NetworkConfig.NetworkTransport == null)
                return "El NetworkManager no tiene Transport (Select transport > UnityTransport).";
            if (!(manager.NetworkConfig.NetworkTransport is UnityTransport))
                return "El transport no es UnityTransport.";
            if (manager.NetworkConfig.PlayerPrefab == null)
                return "Falta el Player Prefab en el NetworkManager (arrastrá el prefab PlayerNet).";
            if (!manager.NetworkConfig.EnableSceneManagement)
                return "Tildá 'Enable Scene Management' en el NetworkManager.";
            return null;
        }

        // ---------------- Eventos de red ----------------

        private void HandleConnected(ulong clientId)
        {
            if (_manager != null && clientId == _manager.LocalClientId) Status = "Conectado";
            Debug.Log($"[Net] Cliente conectado: {clientId}");
        }

        private void HandleDisconnected(ulong clientId)
        {
            if (_manager == null) return;

            // En el servidor este evento también avisa de OTROS clientes que se van.
            if (_manager.IsServer && clientId != _manager.LocalClientId)
            {
                Status = $"Se desconectó el cliente {clientId}";
                Debug.Log($"[Net] {Status}");
                return;
            }

            string reason = _manager.DisconnectReason;
            Status = string.IsNullOrEmpty(reason)
                ? "No se pudo conectar o se perdió la conexión. Revisá IP, puerto y firewall."
                : "Desconectado: " + reason;
            Debug.LogWarning($"[Net] {Status}");
        }

        private void HandleTransportFailure()
        {
            Status = "Falló el transporte de red (¿el puerto ya está en uso por otro Unity abierto?)";
            Debug.LogError($"[Net] {Status}");
        }

        // ---------------- Utilidades ----------------

#if UNITY_SERVER
        // Ejemplo: NavalServer.exe -batchmode -nographics -port 7777 -players 2
        private void ReadCommandLineArgs()
        {
            string[] args = System.Environment.GetCommandLineArgs();
            for (int i = 0; i < args.Length - 1; i++)
            {
                if (args[i] == "-port" && ushort.TryParse(args[i + 1], out var p)) port = p;
                if (args[i] == "-players" && int.TryParse(args[i + 1], out var n))
                    _flow.SetRequiredPlayers(n);
            }
        }
#endif

        private static string ReadLocalIps()
        {
            try
            {
                var sb = new StringBuilder();
                foreach (var address in Dns.GetHostEntry(Dns.GetHostName()).AddressList)
                    if (address.AddressFamily == AddressFamily.InterNetwork) sb.Append(address).Append("   ");
                return sb.Length > 0 ? sb.ToString() : "no detectada";
            }
            catch { return "no detectada"; }
        }
    }
}
