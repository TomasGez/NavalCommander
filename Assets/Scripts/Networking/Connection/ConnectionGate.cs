using Unity.Netcode;

namespace NavalCommander.Networking
{
    /// <summary>
    /// Responsabilidad única: decidir en el SERVIDOR quién puede entrar (aprobación de conexión de Netcode).
    /// Rechaza al cliente que llega con la sala llena o con la partida ya empezada, ANTES de crearle un jugador.
    /// LobbyFlow lo crea con la capacidad de la sesión (inmutable) y lo registra en
    /// NetworkManager.ConnectionApprovalCallback; el mismo código sirve para el host de Relay y para el
    /// servidor dedicado, porque se registra antes de que cualquiera de los dos arranque. El motivo del rechazo
    /// le llega al cliente en NetworkManager.DisconnectReason.
    /// </summary>
    public sealed class ConnectionGate
    {
        private readonly NetworkManager _manager;
        private readonly int _capacity;
        private bool _joiningClosed;

        public ConnectionGate(NetworkManager manager, int capacity)
        {
            _manager = manager;
            _capacity = capacity;
        }

        /// <summary>Activa la aprobación de conexiones y registra este control (hay que llamarlo antes de iniciar la red).</summary>
        public void Install()
        {
            // La escena no tiene la aprobación activada: se activa por código y igual en todas las instancias
            // (Netcode compara esta opción entre servidor y cliente al conectar).
            _manager.NetworkConfig.ConnectionApproval = true;
            _manager.ConnectionApprovalCallback = Approve;
        }

        /// <summary>Desde ahora nadie más puede entrar (la partida ya empezó).</summary>
        public void CloseJoining() => _joiningClosed = true;

        private void Approve(NetworkManager.ConnectionApprovalRequest request, NetworkManager.ConnectionApprovalResponse response)
        {
            response.CreatePlayerObject = true; // PlayerNet (prefab del NetworkManager); el Slot lo asigna el servidor
            response.Approved = true;

            // El host se aprueba a sí mismo: Netcode no permite rechazarlo.
            if (request.ClientNetworkId == NetworkManager.ServerClientId) return;

            if (_joiningClosed)
            {
                response.Approved = false;
                response.Reason = "La partida ya empezó";
            }
            else if (_manager.ConnectedClientsIds.Count >= _capacity) // incluye al host si lo hay
            {
                response.Approved = false;
                response.Reason = "La sala está llena";
            }
        }
    }
}
