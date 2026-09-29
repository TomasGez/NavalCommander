using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace NavalCommander.CardMechanics
{
    public enum CardUseOutcome
    {
        Played,
        BlockedMovement
    }

    public sealed class PlayerCardDeck
    {
        private const int RequiredCardCount = 12;
        private const int HandSize = 3;
        private const int MaximumSelectionsPerTurn = 2;
        private const ulong RandomIncrement = 0x9E3779B97F4A7C15UL;

        private readonly Queue<CardDefinition> _availablePool;
        private readonly List<CardDefinition> _hand = new List<CardDefinition>(HandSize);
        private readonly ReadOnlyCollection<CardDefinition> _handView;
        private readonly HashSet<string> _selectedCardIds = new HashSet<string>(StringComparer.Ordinal);
        private readonly HashSet<string> _pendingResolutionIds = new HashSet<string>(StringComparer.Ordinal);

        private ulong _randomState;

        public PlayerCardDeck(
            IEnumerable<CardDefinition> definitions,
            ulong matchSeed,
            ulong stablePlayerId)
        {
            if (definitions == null)
            {
                throw new ArgumentNullException(nameof(definitions));
            }

            var catalog = new List<CardDefinition>();
            foreach (CardDefinition definition in definitions)
            {
                if (definition == null)
                {
                    throw new ArgumentException("The card catalog cannot contain null definitions.", nameof(definitions));
                }

                if (string.IsNullOrWhiteSpace(definition.CardId))
                {
                    throw new ArgumentException("Every card definition must have a non-empty card ID.", nameof(definitions));
                }

                catalog.Add(definition);
            }

            if (catalog.Count != RequiredCardCount)
            {
                throw new ArgumentException(
                    $"The card catalog must contain exactly {RequiredCardCount} definitions.",
                    nameof(definitions));
            }

            catalog.Sort((left, right) => StringComparer.Ordinal.Compare(left.CardId, right.CardId));
            for (int i = 1; i < catalog.Count; i++)
            {
                if (StringComparer.Ordinal.Equals(catalog[i - 1].CardId, catalog[i].CardId))
                {
                    throw new ArgumentException("Card IDs in the catalog must be unique.", nameof(definitions));
                }
            }

            _handView = _hand.AsReadOnly();
            _randomState = matchSeed ^ Mix64(stablePlayerId);
            Shuffle(catalog);
            _availablePool = new Queue<CardDefinition>(catalog);
        }

        public IReadOnlyList<CardDefinition> Hand => _handView;

        public int SelectedUsesThisTurn { get; private set; }

        public void BeginTurn()
        {
            SelectedUsesThisTurn = 0;
            _selectedCardIds.Clear();
            _pendingResolutionIds.Clear();

            while (_hand.Count < HandSize && _availablePool.Count > 0)
            {
                _hand.Add(_availablePool.Dequeue());
            }
        }

        public bool TrySelectCard(string cardId)
        {
            if (string.IsNullOrEmpty(cardId) ||
                SelectedUsesThisTurn >= MaximumSelectionsPerTurn ||
                FindCardIndex(cardId) < 0 ||
                !_selectedCardIds.Add(cardId))
            {
                return false;
            }

            _pendingResolutionIds.Add(cardId);
            SelectedUsesThisTurn++;
            return true;
        }

        public bool ResolveSelectedCard(string cardId, CardUseOutcome outcome)
        {
            if (string.IsNullOrEmpty(cardId) ||
                !Enum.IsDefined(typeof(CardUseOutcome), outcome) ||
                !_pendingResolutionIds.Contains(cardId))
            {
                return false;
            }

            int cardIndex = FindCardIndex(cardId);
            if (cardIndex < 0)
            {
                return false;
            }

            CardDefinition card = _hand[cardIndex];
            if (outcome == CardUseOutcome.BlockedMovement && card.Category != CardCategory.Movement)
            {
                return false;
            }

            _pendingResolutionIds.Remove(cardId);
            if (outcome == CardUseOutcome.Played)
            {
                _hand.RemoveAt(cardIndex);
                _availablePool.Enqueue(card);
            }

            return true;
        }

        private int FindCardIndex(string cardId)
        {
            for (int i = 0; i < _hand.Count; i++)
            {
                if (StringComparer.Ordinal.Equals(_hand[i].CardId, cardId))
                {
                    return i;
                }
            }

            return -1;
        }

        private void Shuffle(List<CardDefinition> cards)
        {
            for (int i = cards.Count - 1; i > 0; i--)
            {
                int swapIndex = (int)(NextRandomUInt64() % (ulong)(i + 1));
                CardDefinition current = cards[i];
                cards[i] = cards[swapIndex];
                cards[swapIndex] = current;
            }
        }

        private ulong NextRandomUInt64()
        {
            unchecked
            {
                _randomState += RandomIncrement;
                return Mix64(_randomState);
            }
        }

        private static ulong Mix64(ulong value)
        {
            unchecked
            {
                value = (value ^ (value >> 30)) * 0xBF58476D1CE4E5B9UL;
                value = (value ^ (value >> 27)) * 0x94D049BB133111EBUL;
                return value ^ (value >> 31);
            }
        }
    }
}
