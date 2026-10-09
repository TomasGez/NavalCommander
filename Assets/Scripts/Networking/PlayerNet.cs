using System.Collections;
using System.Collections.Generic;
using Unity.Collections;
using Unity.Netcode;

namespace NavalCommander.Networking
{
    /// <summary>
    /// Prefab de jugador (uno por cliente). Se crea al conectarse (en el Lobby) y sobrevive al cambio de escena.
    /// Solo transporta identidad (slot) y "confirmé mis jugadas". Las cartas elegidas viajan por RPC solo al
    /// servidor, así los demás nunca las reciben antes de la resolución.
    /// </summary>
    public class PlayerNet : NetworkBehaviour
    {
        public const int MaxPlayers = 4;

        /// <summary>Todos los jugadores conectados (existe en servidor y en clientes).</summary>
        public static readonly List<PlayerNet> All = new List<PlayerNet>();
        public static PlayerNet Local { get; private set; }

        public NetworkVariable<int> Slot = new NetworkVariable<int>(-1);
        public NetworkVariable<bool> Confirmed = new NetworkVariable<bool>(false);

        public override void OnNetworkSpawn()
        {
            All.Add(this);
            if (IsOwner) Local = this;
            if (IsServer) StartCoroutine(AssignSlot());
        }

        public override void OnNetworkDespawn()
        {
            All.Remove(this);
            if (Local == this) Local = null;
            if (IsServer && MatchController.Instance != null)
                MatchController.Instance.OnPlayerLeft(this);
        }

        // Se espera un frame: durante el spawn las NetworkVariable todavía no están listas para escribirse.
        private IEnumerator AssignSlot()
        {
            yield return null;
            if (this == null || !IsSpawned) yield break;

            // Partida ya empezada (existe el MatchController de la GameScene) o sala llena: se rechaza.
            bool matchStarted = MatchController.Instance != null;
            bool full = All.Count > LobbyController.RequiredPlayers || All.Count > MaxPlayers;
            if (matchStarted || full)
            {
                NetworkManager.DisconnectClient(OwnerClientId,
                    matchStarted ? "La partida ya empezó" : "La sala está llena");
                yield break;
            }

            Slot.Value = FirstFreeSlot();
        }

        private int FirstFreeSlot()
        {
            for (int s = 0; s < MaxPlayers; s++)
            {
                bool used = false;
                foreach (var p in All)
                    if (p != null && p != this && p.Slot.Value == s) used = true;
                if (!used) return s;
            }
            return 0;
        }

        // Cliente -> Servidor (solo el dueño puede llamarlo)
        [ServerRpc]
        public void SubmitPlaysServerRpc(PlayDto[] plays)
        {
            if (MatchController.Instance != null)
                MatchController.Instance.ServerReceivePlays(this, plays);
        }

        // Servidor -> solo al dueño de este jugador (la mano es privada)
        public void SendHand(FixedString32Bytes[] ids)
        {
            var target = new ClientRpcParams
            {
                Send = new ClientRpcSendParams { TargetClientIds = new[] { OwnerClientId } }
            };
            ReceiveHandClientRpc(ids, target);
        }

        [ClientRpc]
        private void ReceiveHandClientRpc(FixedString32Bytes[] ids, ClientRpcParams rpcParams = default)
        {
            var list = new string[ids.Length];
            for (int i = 0; i < ids.Length; i++) list[i] = ids[i].ToString();
            MatchEvents.RaiseHand(list);
        }
    }
}
