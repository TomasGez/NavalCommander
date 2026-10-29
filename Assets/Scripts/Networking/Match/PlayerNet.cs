using System.Collections;
using System.Collections.Generic;
using Unity.Collections;
using Unity.Netcode;

namespace NavalCommander.Networking
{
    /// <summary>
    /// Representa a un jugador conectado (un NetworkObject por cliente, prefab Player del NetworkManager).
    /// Se crea al conectarse (solo si el ConnectionGate aprobó la conexión), en el Lobby, y sobrevive al cambio
    /// de escena. Solo guarda identidad (Slot), el flag "confirmé" y los canales de comunicación del jugador:
    /// no contiene reglas del juego.
    ///
    /// Corre en el servidor y en los clientes. En el servidor asigna el Slot y avisa a
    /// MatchController cuando el jugador se va. Como canal de red, SubmitPlaysRpc lleva las jugadas del cliente
    /// al servidor (que las valida en MatchController/MatchRules) y ReceiveHandRpc entrega la mano privada
    /// al dueño, quien la publica en MatchEvents para que la UI (MatchHud) la muestre.
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

        // ---------------- Servidor: asignación de slot ----------------

        // Se espera un frame: durante el spawn las NetworkVariable todavía no están listas para escribirse.
        private IEnumerator AssignSlotWhenReady()
        {
            yield return null;
            if (this == null || !IsSpawned) yield break;

            // La admisión (sala llena / partida empezada) ya la resolvió el ConnectionGate antes de crear este jugador.
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

        /// <summary>
        /// Cliente -> Servidor. Las cartas nunca se replican a otros clientes.
        /// Solo el dueño del jugador puede invocarlo (InvokePermission.Owner) y además el servidor
        /// comprueba quién envía: nadie puede jugar por el barco de otro.
        /// </summary>
        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Owner)]
        public void SubmitPlaysRpc(PlayDto[] plays, RpcParams rpcParams = default)
        {
            bool isHostPlayer = IsServer && IsOwner; // el host se llama a sí mismo: no hay emisor remoto
            if (!isHostPlayer && rpcParams.Receive.SenderClientId != OwnerClientId) return;

            if (MatchController.Instance != null) MatchController.Instance.ServerReceivePlays(this, plays);
        }

        /// <summary>Servidor -> solo el dueño de este jugador (la mano es privada).</summary>
        public void SendHand(FixedString32Bytes[] cardIds) => ReceiveHandRpc(cardIds);

        [Rpc(SendTo.Owner, InvokePermission = RpcInvokePermission.Server)] // solo el servidor puede repartir manos
        private void ReceiveHandRpc(FixedString32Bytes[] cardIds)
        {
            var ids = new string[cardIds.Length];
            for (int i = 0; i < ids.Length; i++) ids[i] = cardIds[i].ToString();
            MatchEvents.RaiseHandReceived(ids);
        }
    }
}
