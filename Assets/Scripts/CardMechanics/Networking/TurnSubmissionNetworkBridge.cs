using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using NavalCommander.CardMechanics;
using Unity.Netcode;
using UnityEngine;

namespace NavalCommander.CardMechanics.Networking
{
    public sealed class TurnSubmissionNetworkBridge : NetworkBehaviour
    {
        private const int ActivePlayerCount = 2;
        private const int RequiredCardCount = 12;
        private const int MaximumHandSize = 3;

        [SerializeField] private CardDefinition[] _cardCatalog = Array.Empty<CardDefinition>();

        private Dictionary<string, CardDefinition> _cardDefinitionsById;
        private TurnSubmissionCoordinator _serverCoordinator;
        private List<ulong> _activeClientIds = new List<ulong>(ActivePlayerCount);
        private IReadOnlyList<TurnCardSubmission> _pendingServerResolution;
        private bool _serverResolutionActive;
        private IReadOnlyList<CardDefinition> _ownedHand = Array.Empty<CardDefinition>();

        public IReadOnlyList<CardDefinition> OwnHand => _ownedHand;

        public event Action<IReadOnlyList<CardDefinition>> OwnHandChanged;
        public event Action<string, bool> CardSelectionResultReceived;
        public event Action<bool> TurnConfirmationResultReceived;

        public bool TryInitializeServerMatch(ulong matchSeed)
        {
            if (!IsSpawned || !IsServer || NetworkManager == null || _serverCoordinator != null ||
                !TryBuildCardDefinitionIndex(out Dictionary<string, CardDefinition> cardDefinitionsById))
            {
                return false;
            }

            var clientIds = new List<ulong>(NetworkManager.ConnectedClientsIds);
            if (clientIds.Count != ActivePlayerCount)
            {
                return false;
            }

            // The sorted roster defines stable deck-seed slots for this match lifetime.
            clientIds.Sort();
            if (clientIds[0] == clientIds[1])
            {
                return false;
            }

            var playerDecks = new Dictionary<ulong, PlayerCardDeck>(ActivePlayerCount);
            for (int slot = 0; slot < clientIds.Count; slot++)
            {
                var deck = new PlayerCardDeck(_cardCatalog, matchSeed, (ulong)slot);
                deck.BeginTurn();
                playerDecks.Add(clientIds[slot], deck);
            }

            _cardDefinitionsById = cardDefinitionsById;
            _activeClientIds = clientIds;
            _serverCoordinator = new TurnSubmissionCoordinator(playerDecks);

            for (int i = 0; i < _activeClientIds.Count; i++)
            {
                SendPrivateHandToOwner(_activeClientIds[i]);
            }

            return true;
        }

        public void RequestCardSelection(string cardId)
        {
            if (string.IsNullOrEmpty(cardId))
            {
                CardSelectionResultReceived?.Invoke(cardId ?? string.Empty, false);
                return;
            }

            if (!IsSpawned || !IsClient)
            {
                CardSelectionResultReceived?.Invoke(cardId, false);
                return;
            }

            RequestCardSelectionServerRpc(cardId);
        }

        public void RequestTurnConfirmation()
        {
            if (!IsSpawned || !IsClient)
            {
                TurnConfirmationResultReceived?.Invoke(false);
                return;
            }

            RequestTurnConfirmationServerRpc();
        }

        public bool TryConsumeServerResolutionSnapshot(
            out IReadOnlyList<TurnCardSubmission> submissions)
        {
            submissions = null;
            if (!IsSpawned || !IsServer || _pendingServerResolution == null)
            {
                return false;
            }

            submissions = _pendingServerResolution;
            _pendingServerResolution = null;
            return true;
        }

        public bool TryRecordServerActionOutcome(
            ulong playerClientId,
            string cardId,
            CardUseOutcome outcome)
        {
            return IsSpawned && IsServer && _serverResolutionActive &&
                   _pendingServerResolution == null &&
                   _serverCoordinator != null &&
                   _serverCoordinator.TryRecordActionOutcome(playerClientId, cardId, outcome);
        }

        public bool TryBeginServerNextTurn()
        {
            if (!IsSpawned || !IsServer || !_serverResolutionActive ||
                _pendingServerResolution != null || _serverCoordinator == null ||
                !_serverCoordinator.TryBeginNextTurn())
            {
                return false;
            }

            _serverResolutionActive = false;
            for (int i = 0; i < _activeClientIds.Count; i++)
            {
                SendPrivateHandToOwner(_activeClientIds[i]);
            }

            return true;
        }

        public override void OnNetworkDespawn()
        {
            _serverCoordinator = null;
            _activeClientIds.Clear();
            _pendingServerResolution = null;
            _serverResolutionActive = false;
            _ownedHand = Array.Empty<CardDefinition>();
            OwnHandChanged?.Invoke(_ownedHand);
            base.OnNetworkDespawn();
        }

        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
        private void RequestCardSelectionServerRpc(string cardId, RpcParams rpcParams = default)
        {
            if (!IsServer)
            {
                return;
            }

            // NGO supplies the authenticated sender; the request payload contains no owner ID.
            ulong senderClientId = rpcParams.Receive.SenderClientId;
            bool accepted = _serverCoordinator != null &&
                            _serverCoordinator.TrySubmitCard(senderClientId, cardId);
            SendCardSelectionResultClientRpc(
                cardId ?? string.Empty,
                accepted,
                NetworkManager.RpcTarget.Single(senderClientId, RpcTargetUse.Temp));
        }

        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
        private void RequestTurnConfirmationServerRpc(RpcParams rpcParams = default)
        {
            if (!IsServer)
            {
                return;
            }

            ulong senderClientId = rpcParams.Receive.SenderClientId;
            bool accepted = _serverCoordinator != null &&
                            _serverCoordinator.TryConfirmTurn(senderClientId);
            if (accepted && _serverCoordinator.TryBeginResolution(
                    out IReadOnlyList<TurnCardSubmission> submissions))
            {
                // Keep hidden choices in server-local memory for the future effect engine.
                _pendingServerResolution = submissions;
                _serverResolutionActive = true;
            }

            TurnConfirmationResultClientRpc(
                accepted,
                NetworkManager.RpcTarget.Single(senderClientId, RpcTargetUse.Temp));
        }

        // Private hand contents are sent only to the explicitly selected owner.
        [Rpc(SendTo.SpecifiedInParams, InvokePermission = RpcInvokePermission.Server)]
        private void ReceivePrivateHandClientRpc(string[] cardIds, RpcParams rpcParams = default)
        {
            if (!IsClient || cardIds == null || cardIds.Length > MaximumHandSize ||
                !TryEnsureCardDefinitionIndex())
            {
                return;
            }

            var hand = new List<CardDefinition>(cardIds.Length);
            for (int i = 0; i < cardIds.Length; i++)
            {
                if (string.IsNullOrWhiteSpace(cardIds[i]) ||
                    !_cardDefinitionsById.TryGetValue(cardIds[i], out CardDefinition definition))
                {
                    return;
                }

                hand.Add(definition);
            }

            _ownedHand = new ReadOnlyCollection<CardDefinition>(hand);
            OwnHandChanged?.Invoke(_ownedHand);
        }

        [Rpc(SendTo.SpecifiedInParams, InvokePermission = RpcInvokePermission.Server)]
        private void SendCardSelectionResultClientRpc(
            string cardId,
            bool accepted,
            RpcParams rpcParams = default)
        {
            if (IsClient)
            {
                CardSelectionResultReceived?.Invoke(cardId, accepted);
            }
        }

        [Rpc(SendTo.SpecifiedInParams, InvokePermission = RpcInvokePermission.Server)]
        private void TurnConfirmationResultClientRpc(bool accepted, RpcParams rpcParams = default)
        {
            if (IsClient)
            {
                TurnConfirmationResultReceived?.Invoke(accepted);
            }
        }

        private void SendPrivateHandToOwner(ulong ownerClientId)
        {
            if (_serverCoordinator == null ||
                !_serverCoordinator.TryGetPrivateHand(ownerClientId, ownerClientId, out IReadOnlyList<CardDefinition> hand))
            {
                return;
            }

            var cardIds = new string[hand.Count];
            for (int i = 0; i < hand.Count; i++)
            {
                cardIds[i] = hand[i].CardId;
            }

            ReceivePrivateHandClientRpc(
                cardIds,
                NetworkManager.RpcTarget.Single(ownerClientId, RpcTargetUse.Temp));
        }

        private bool TryEnsureCardDefinitionIndex()
        {
            if (_cardDefinitionsById != null)
            {
                return true;
            }

            return TryBuildCardDefinitionIndex(out _cardDefinitionsById);
        }

        private bool TryBuildCardDefinitionIndex(out Dictionary<string, CardDefinition> definitionsById)
        {
            definitionsById = null;
            if (_cardCatalog == null || _cardCatalog.Length != RequiredCardCount)
            {
                return false;
            }

            var candidate = new Dictionary<string, CardDefinition>(RequiredCardCount, StringComparer.Ordinal);
            for (int i = 0; i < _cardCatalog.Length; i++)
            {
                CardDefinition definition = _cardCatalog[i];
                if (definition == null || string.IsNullOrWhiteSpace(definition.CardId) ||
                    candidate.ContainsKey(definition.CardId))
                {
                    return false;
                }

                candidate.Add(definition.CardId, definition);
            }

            definitionsById = candidate;
            return true;
        }
    }
}
