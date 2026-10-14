using Unity.Netcode;
using UnityEngine;

namespace NavalCommander.Networking
{
    /// <summary>
    /// Responsabilidad única: dibujar la pantalla del Lobby (OnGUI provisional).
    /// No contiene lógica de red: delega en NetworkLauncher y lee el estado de LobbyFlow.
    /// </summary>
    [RequireComponent(typeof(NetworkLauncher), typeof(LobbyFlow))]
    public class LobbyView : MonoBehaviour
    {
        private NetworkLauncher _launcher;
        private LobbyFlow _flow;

        private void Awake()
        {
            _launcher = GetComponent<NetworkLauncher>();
            _flow = GetComponent<LobbyFlow>();
        }

        private void OnGUI()
        {
            var manager = NetworkManager.Singleton;

            GUILayout.BeginArea(new Rect(10, 10, 380, Screen.height - 20));
            GUILayout.Label("STRATEGY NAVAL - Lobby");

            if (manager == null) GUILayout.Label("Falta el NetworkManager en esta escena.");
            else if (!manager.IsListening) DrawConnectionMenu();
            else DrawWaitingRoom(manager);

            GUILayout.Space(8);
            GUILayout.Label("Estado: " + _launcher.Status);
            GUILayout.EndArea();
        }

        private void DrawConnectionMenu()
        {
            GUILayout.Label($"IP de esta PC: {_launcher.LocalIps}");
            GUILayout.Label("IP del servidor / host a la que conectar:");
            _launcher.Ip = GUILayout.TextField(_launcher.Ip);
            GUILayout.Label("(misma PC: 127.0.0.1 | otra PC de la red: la IP del host)");
            GUILayout.Label($"Puerto: {_launcher.Port}   |   Jugadores requeridos: {SessionSettings.RequiredPlayers}");

            if (GUILayout.Button("Host (servidor + jugador)")) _launcher.StartHost();
            if (GUILayout.Button("Client")) _launcher.StartClient();
            if (GUILayout.Button("Servidor dedicado (sin jugador)")) _launcher.StartDedicatedServer();
        }

        private void DrawWaitingRoom(NetworkManager manager)
        {
            string role = manager.IsHost ? "Host" : manager.IsServer ? "Servidor dedicado" : "Cliente";
            GUILayout.Label($"{role} | puerto {_launcher.Port}");

            if (manager.IsServer) GUILayout.Label($"Los clientes deben conectarse a: {_launcher.LocalIps}");

            if (manager.IsClient && !manager.IsServer && !manager.IsConnectedClient)
            {
                GUILayout.Label($"Conectando a {_launcher.Ip}:{_launcher.Port} ...");
            }
            else
            {
                GUILayout.Label($"Jugadores: {PlayerNet.All.Count} / {SessionSettings.RequiredPlayers}");
                foreach (var player in PlayerNet.All)
                    if (player != null)
                        GUILayout.Label($"  Slot {player.Slot.Value}{(player.IsOwner ? "  (vos)" : "")}");

                if (PlayerNet.All.Count == 0 && (manager.IsConnectedClient || manager.IsServer))
                    GUILayout.Label("Conectado pero no se creó ningún jugador: revisá Player Prefab y la lista de Network Prefabs.");

                if (manager.IsServer && _flow.Countdown >= 0f)
                    GUILayout.Label($"La partida empieza en {Mathf.Ceil(_flow.Countdown)}...");
                else GUILayout.Label("Esperando jugadores...");
            }

            if (GUILayout.Button("Desconectar")) _launcher.Disconnect();
        }
    }
}
