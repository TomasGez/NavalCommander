using Unity.Netcode;
using UnityEngine;

namespace NavalCommander.Presentation
{
    /// <summary>
    /// Evita que quede un segundo NetworkManager vivo al volver de la partida al menú.
    /// El NetworkManager pasa a DontDestroyOnLoad al activarse y sobrevive a la partida, de modo que el que
    /// trae la MenuScene sobra cuando la escena se carga por segunda vez. Corre antes que el NetworkManager:
    /// desactivar el objeto ahora impide que llegue a ejecutar su Awake y su OnEnable.
    /// </summary>
    [DefaultExecutionOrder(-1000)]
    public class MenuNetworkManagerGuard : MonoBehaviour
    {
        [SerializeField] private GameObject sceneNetworkManager;

        private void Awake()
        {
            var survivor = NetworkManager.Singleton;
            if (survivor == null || sceneNetworkManager == null) return;
            if (survivor.gameObject == sceneNetworkManager) return;

            sceneNetworkManager.SetActive(false);
            Destroy(sceneNetworkManager);
        }
    }
}
