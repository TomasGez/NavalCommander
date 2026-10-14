using Unity.Netcode;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace NavalCommander.Networking
{
    /// <summary>
    /// Responsabilidad única: decidir CUÁNDO empieza la partida.
    /// Solo actúa en el servidor: cuando hay suficientes jugadores con slot asignado,
    /// hace una cuenta regresiva y carga la GameScene para todos.
    /// </summary>
    public class LobbyFlow : MonoBehaviour
    {
        [SerializeField] private int requiredPlayers = 2;      // 2 o 4
        [SerializeField] private string gameSceneName = "GameScene";
        [SerializeField] private float startDelaySeconds = 2f;

        private float _countdown = -1f;
        private bool _loading;

        /// <summary>Segundos que faltan para empezar; negativo si no está contando.</summary>
        public float Countdown => _countdown;

        private void Awake()
        {
            SessionSettings.RequiredPlayers = requiredPlayers;
        }

        private void Update()
        {
            var manager = NetworkManager.Singleton;
            if (manager == null || !manager.IsServer || _loading) return;

            if (CountReadyPlayers() >= SessionSettings.RequiredPlayers)
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
            manager.SceneManager.LoadScene(gameSceneName, LoadSceneMode.Single); // todos los clientes la cargan
        }
    }
}
