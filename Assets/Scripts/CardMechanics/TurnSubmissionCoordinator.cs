using System;
using System.Collections.Generic;

namespace NavalCommander.CardMechanics
{
    public enum MatchTurnPhase
    {
        CollectingSubmissions,
        Resolving
    }

    public struct TurnCardSubmission
    {
        public ulong PlayerClientId { get; private set; }
        public string CardId { get; private set; }

        public TurnCardSubmission(ulong playerClientId, string cardId)
        {
            PlayerClientId = playerClientId;
            CardId = cardId;
        }
    }

    // Pure server-side coordinator. A future network bridge must pass the authenticated
    // transport sender ID, never a client-provided owner ID.
    public sealed class TurnSubmissionCoordinator
    {
        private sealed class PlayerTurnState
        {
            public PlayerTurnState(PlayerCardDeck deck)
            {
                Deck = deck;
            }

            public PlayerCardDeck Deck { get; }
            public List<TurnCardSubmission> Submissions { get; } = new List<TurnCardSubmission>(2);
            public HashSet<string> ResolvedCardIds { get; } = new HashSet<string>(StringComparer.Ordinal);
            public bool HasConfirmed { get; set; }
        }

        private readonly Dictionary<ulong, PlayerTurnState> _players =
            new Dictionary<ulong, PlayerTurnState>();
        private readonly List<ulong> _orderedClientIds = new List<ulong>();
        private MatchTurnPhase _phase = MatchTurnPhase.CollectingSubmissions;

        public TurnSubmissionCoordinator(IDictionary<ulong, PlayerCardDeck> playerDecks)
        {
            if (playerDecks == null)
            {
                throw new ArgumentNullException(nameof(playerDecks));
            }

            if (playerDecks.Count == 0)
            {
                throw new ArgumentException("A match requires at least one active player.", nameof(playerDecks));
            }

            var assignedDecks = new HashSet<PlayerCardDeck>();
            foreach (KeyValuePair<ulong, PlayerCardDeck> entry in playerDecks)
            {
                if (entry.Value == null)
                {
                    throw new ArgumentException("Every active player must have a deck.", nameof(playerDecks));
                }

                if (!assignedDecks.Add(entry.Value))
                {
                    throw new ArgumentException("Every active player must have an independent deck.", nameof(playerDecks));
                }

                _players.Add(entry.Key, new PlayerTurnState(entry.Value));
                _orderedClientIds.Add(entry.Key);
            }

            _orderedClientIds.Sort();
        }

        public MatchTurnPhase Phase => _phase;

        public bool TrySubmitCard(ulong senderClientId, string cardId)
        {
            if (_phase != MatchTurnPhase.CollectingSubmissions ||
                !_players.TryGetValue(senderClientId, out PlayerTurnState player) ||
                player.HasConfirmed ||
                !player.Deck.TrySelectCard(cardId))
            {
                return false;
            }

            player.Submissions.Add(new TurnCardSubmission(senderClientId, cardId));
            return true;
        }

        public bool TryConfirmTurn(ulong senderClientId)
        {
            if (_phase != MatchTurnPhase.CollectingSubmissions ||
                !_players.TryGetValue(senderClientId, out PlayerTurnState player) ||
                player.HasConfirmed)
            {
                return false;
            }

            player.HasConfirmed = true;
            return true;
        }

        public bool TryBeginResolution(out IReadOnlyList<TurnCardSubmission> submissions)
        {
            submissions = null;
            if (_phase != MatchTurnPhase.CollectingSubmissions)
            {
                return false;
            }

            for (int i = 0; i < _orderedClientIds.Count; i++)
            {
                if (!_players[_orderedClientIds[i]].HasConfirmed)
                {
                    return false;
                }
            }

            var snapshot = new List<TurnCardSubmission>();
            for (int i = 0; i < _orderedClientIds.Count; i++)
            {
                PlayerTurnState player = _players[_orderedClientIds[i]];
                snapshot.AddRange(player.Submissions);
            }

            _phase = MatchTurnPhase.Resolving;
            submissions = snapshot.AsReadOnly();
            return true;
        }

        public bool TryRecordActionOutcome(
            ulong playerClientId,
            string cardId,
            CardUseOutcome outcome)
        {
            if (_phase != MatchTurnPhase.Resolving ||
                string.IsNullOrEmpty(cardId) ||
                !_players.TryGetValue(playerClientId, out PlayerTurnState player) ||
                player.ResolvedCardIds.Contains(cardId))
            {
                return false;
            }

            bool wasSubmitted = false;
            for (int i = 0; i < player.Submissions.Count; i++)
            {
                if (StringComparer.Ordinal.Equals(player.Submissions[i].CardId, cardId))
                {
                    wasSubmitted = true;
                    break;
                }
            }

            if (!wasSubmitted || !player.Deck.ResolveSelectedCard(cardId, outcome))
            {
                return false;
            }

            player.ResolvedCardIds.Add(cardId);
            return true;
        }

        public bool TryBeginNextTurn()
        {
            if (_phase != MatchTurnPhase.Resolving)
            {
                return false;
            }

            for (int i = 0; i < _orderedClientIds.Count; i++)
            {
                PlayerTurnState player = _players[_orderedClientIds[i]];
                if (player.ResolvedCardIds.Count != player.Submissions.Count)
                {
                    return false;
                }
            }

            for (int i = 0; i < _orderedClientIds.Count; i++)
            {
                PlayerTurnState player = _players[_orderedClientIds[i]];
                player.Deck.BeginTurn();
                player.Submissions.Clear();
                player.ResolvedCardIds.Clear();
                player.HasConfirmed = false;
            }

            _phase = MatchTurnPhase.CollectingSubmissions;
            return true;
        }

        public bool TryGetPrivateHand(
            ulong requesterClientId,
            ulong ownerClientId,
            out IReadOnlyList<CardDefinition> hand)
        {
            hand = null;
            if (requesterClientId != ownerClientId ||
                !_players.TryGetValue(ownerClientId, out PlayerTurnState player))
            {
                return false;
            }

            hand = new List<CardDefinition>(player.Deck.Hand).AsReadOnly();
            return true;
        }
    }
}
