using System.Collections;
using System.Collections.Generic;
using Unity.Collections;
using Unity.Netcode;

namespace NavalCommander.Networking
{
    /// <summary>
    /// Representa a un jugador conectado (un objeto por cliente). Se crea al conectarse, en el Lobby,
    /// y sobrevive al cambio de escena. Solo guarda identidad (Slot), el flag "confirmé" y los canales
    /// de comunicación del jugador: no contiene reglas del juego.
    /// </summary>
    public class PlayerNet : NetworkBehaviour
    {
        /// <summary>Todos los jugadores conectados (existe en el servidor y en los clientes).</summary>
        public static readonly List<PlayerNet> All = new List<PlayerNet>();

        /// <summary>El jugador de ESTA instancia del juego (null en un servidor dedicado).</summary>
        public static PlayerNet Local { get; private set; }

        public NetworkVariable<int> Slot = new NetworkVariable<int>(-1);
        public NetworkVariable<bool> Confirmed = new NetworkVariable<bool>(false);

        public override void OnNetworkSpawn()
        {
            All.Add(this);
            if (IsOwner) Local = this;
            if (IsServer) StartCoroutine(AssignSlotWhenReady());
        }

        public override void OnNetworkDespawn()
        {
            All.Remove(this);
            if (Local == this) Local = null;
            if (IsServer && MatchController.Instance != null) MatchController.Instance.OnPlayerLeft(this);
        }

        // ---------------- Servidor: admisión y slot ----------------

        // Se espera un frame: durante el spawn las NetworkVariable todavía no están listas para escribirse.
        private IEnumerator AssignSlotWhenReady()
        {
            yield return null;
            if (this == null || !IsSpawned) yield break;

            bool matchStarted = MatchController.Instance != null; // el MatchController solo existe en la GameScene
            bool roomFull = All.Count > SessionSettings.RequiredPlayers || All.Count > SessionSettings.MaxPlayers;
            if (matchStarted || roomFull)
            {
                NetworkManager.DisconnectClient(OwnerClientId, matchStarted ? "La partida ya empezó" : "La sala está llena");
                yield break;
            }

            Slot.Value = FirstFreeSlot();
        }

        private int FirstFreeSlot()
        {
            for (int slot = 0; slot < SessionSettings.MaxPlayers; slot++)
            {
                bool used = false;
                foreach (var other in All)
                    if (other != null && other != this && other.Slot.Value == slot) used = true;
                if (!used) return slot;
            }
            return 0;
        }

        // ---------------- Canales de comunicación ----------------

        /// <summary>Cliente -> Servidor. Solo el dueño puede llamarlo. Las cartas nunca se replican a otros clientes.</summary>
        [ServerRpc]
        public void SubmitPlaysServerRpc(PlayDto[] plays)
        {
            if (MatchController.Instance != null) MatchController.Instance.ServerReceivePlays(this, plays);
        }

        /// <summary>Servidor -> solo el dueño de este jugador (la mano es privada).</summary>
        public void SendHand(FixedString32Bytes[] cardIds)
        {
            var onlyOwner = new ClientRpcParams
            {
                Send = new ClientRpcSendParams { TargetClientIds = new[] { OwnerClientId } }
            };
            ReceiveHandClientRpc(cardIds, onlyOwner);
        }

        [ClientRpc]
        private void ReceiveHandClientRpc(FixedString32Bytes[] cardIds, ClientRpcParams rpcParams = default)
        {
            var ids = new string[cardIds.Length];
            for (int i = 0; i < ids.Length; i++) ids[i] = cardIds[i].ToString();
            MatchEvents.RaiseHandReceived(ids);
        }
    }
}
