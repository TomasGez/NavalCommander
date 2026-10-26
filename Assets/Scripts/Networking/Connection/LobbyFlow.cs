using Unity.Netcode;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace NavalCommander.Networking
{
    /// <summary>
    /// Responsabilidad única: decidir CUÁNDO empieza la partida. Vive en la LobbyScene.
    /// Solo actúa en el servidor (host o servidor dedicado): cuando hay suficientes jugadores con slot
    /// asignado (PlayerNet.All), hace una cuenta regresiva y carga la GameScene para todos con el
    /// NetworkSceneManager. Al terminar la carga, MatchController arranca la partida.
    /// Es dueño de la cantidad de jugadores requeridos (RequiredPlayers): LobbyView la lee para crear la sesión
    /// y el ConnectionGate la usa como capacidad al aprobar conexiones (sala llena / partida ya empezada).
    /// LobbyView lee Countdown para mostrarlo.
    /// </summary>
    [DefaultExecutionOrder(-50)] // su Start corre antes que el de NetworkLauncher (el servidor dedicado arranca solo)
    public class LobbyFlow : MonoBehaviour
    {
        [SerializeField] private int requiredPlayers = 2;      // 2 o 4
        [SerializeField] private string gameSceneName = "GameScene";
        [SerializeField] private float startDelaySeconds = 2f;

        private float _countdown = -1f;
        private bool _loading;
        private ConnectionGate _gate;

        /// <summary>Jugadores necesarios para empezar (2 o 4); también es la capacidad de la sala.</summary>
        public int RequiredPlayers => requiredPlayers;

        /// <summary>Segundos que faltan para empezar; negativo si no está contando.</summary>
        public float Countdown => _countdown;

        private void Start()
        {
            InstallGate(); // antes de que cualquier ruta (Relay o IP directa) inicie el host o el servidor
        }

        /// <summary>Cambia la cantidad de jugadores. Solo antes de conectar: después la sala ya tiene su capacidad.</summary>
        public void SetRequiredPlayers(int value)
        {
            var manager = NetworkManager.Singleton;
            if (manager != null && manager.IsListening) return;

            requiredPlayers = Mathf.Clamp(value, SessionSettings.MinPlayers, SessionSettings.MaxPlayers);
            InstallGate();
        }

        // El ConnectionGate es inmutable: si cambia la capacidad se crea uno nuevo.
        private void InstallGate()
        {
            var manager = NetworkManager.Singleton;
            if (manager == null || manager.IsListening) return;

            _gate = new ConnectionGate(manager, requiredPlayers);
            _gate.Install();
        }

        private void Update()
        {
            var manager = NetworkManager.Singleton;
            if (manager == null || !manager.IsServer || _loading) return;

            if (CountReadyPlayers() >= requiredPlayers)
            {
                if (_countdown < 0f) _countdown = startDelaySeconds;
                _countdown -= Time.deltaTime;
                if (_countdown <= 0f) LoadGameScene(manager);
            }
            else _countdown = -1f;
        }

        private static int CountReadyPlayers()
        {
            int ready = 0;
            foreach (var player in PlayerNet.All)
                if (player != null && player.Slot.Value >= 0) ready++;
            return ready;
        }

        private void LoadGameScene(NetworkManager manager)
        {
            _loading = true;
            if (_gate != null) _gate.CloseJoining(); // desde acá nadie más puede entrar
            manager.SceneManager.LoadScene(gameSceneName, LoadSceneMode.Single); // todos los clientes la cargan
        }
    }
}
