using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using UnityEditor;
using NavalCommander.CardMechanics;

namespace NavalCommander.CardMechanics.Tests
{
    public sealed class PlayerCardDeckTests
    {
        private const string DeckTypeName = "NavalCommander.CardMechanics.PlayerCardDeck";
        private const string OutcomeTypeName = "NavalCommander.CardMechanics.CardUseOutcome";

        [Test]
        public void BeginTurn_FillsHandToThreeAndOffersNoDrawOrDiscardCommand()
        {
            var deck = CreateDeck();

            BeginTurn(deck);

            var hand = GetHand(deck);
            Assert.That(hand, Has.Count.EqualTo(3));
            Assert.That(hand.Select(card => card.CardId).Distinct().Count(), Is.EqualTo(3));

            var forbiddenMethods = deck.GetType()
                .GetMethods(BindingFlags.Public | BindingFlags.Instance)
                .Select(method => method.Name)
                .Where(name => name.IndexOf("Draw", StringComparison.OrdinalIgnoreCase) >= 0 ||
                               name.IndexOf("Discard", StringComparison.OrdinalIgnoreCase) >= 0);
            Assert.That(forbiddenMethods, Is.Empty,
                "The player deck must refill automatically, without player draw/discard commands.");
        }

        [Test]
        public void SeededDecks_AreRepeatableForTheSamePlayerAndIndependentAcrossPlayers()
        {
            var first = CreateDeck(matchSeed: 42, stablePlayerId: 17);
            var repeat = CreateDeck(matchSeed: 42, stablePlayerId: 17);
            var otherPlayer = CreateDeck(matchSeed: 42, stablePlayerId: 18);

            BeginTurn(first);
            BeginTurn(repeat);
            BeginTurn(otherPlayer);

            var firstHand = GetHand(first).Select(card => card.CardId).ToArray();
            var repeatHand = GetHand(repeat).Select(card => card.CardId).ToArray();
            var otherHand = GetHand(otherPlayer).Select(card => card.CardId).ToArray();

            Assert.That(repeatHand, Is.EqualTo(firstHand));
            Assert.That(otherHand, Is.Not.EqualTo(firstHand),
                "A shared match seed must still yield a distinct stream for another stable player ID.");
        }

        [Test]
        public void BeginTurn_PreservesUnusedCardsAndFillsOnlyTheMissingSlot()
        {
            var deck = CreateDeck();
            BeginTurn(deck);
            var initialHand = GetHand(deck);
            var preservedIds = initialHand.Take(2).Select(card => card.CardId).ToArray();
            var playedId = initialHand[2].CardId;

            Assert.That(TrySelect(deck, playedId), Is.True);
            Assert.That(Resolve(deck, playedId, "Played"), Is.True);

            BeginTurn(deck);

            var nextHand = GetHand(deck).Select(card => card.CardId).ToArray();
            Assert.That(nextHand, Has.Length.EqualTo(3));
            Assert.That(nextHand, Does.Contain(preservedIds[0]));
            Assert.That(nextHand, Does.Contain(preservedIds[1]));
        }

        [Test]
        public void Selection_CountsOnlyValidCardsAndStopsAfterTwoPerTurn()
        {
            var deck = CreateDeck();
            BeginTurn(deck);
            var cardIds = GetHand(deck).Select(card => card.CardId).ToArray();

            Assert.That(TrySelect(deck, "not-in-this-hand"), Is.False);
            Assert.That(GetSelectedUses(deck), Is.Zero);
            Assert.That(TrySelect(deck, cardIds[0]), Is.True);
            Assert.That(TrySelect(deck, cardIds[0]), Is.False, "A physical card cannot be selected twice in one turn.");
            Assert.That(TrySelect(deck, cardIds[1]), Is.True);
            Assert.That(TrySelect(deck, cardIds[2]), Is.False);
            Assert.That(GetSelectedUses(deck), Is.EqualTo(2));
        }

        [Test]
        public void OnlySelectedCardsCanBeResolved()
        {
            var deck = CreateDeck();
            BeginTurn(deck);
            var hand = GetHand(deck);
            var cardId = hand[0].CardId;

            Assert.That(Resolve(deck, cardId, "Played"), Is.False);
            Assert.That(GetHand(deck).Select(card => card.CardId), Does.Contain(cardId));
            Assert.That(GetSelectedUses(deck), Is.Zero);
        }

        [Test]
        public void PlayedCardReturnsToItsOwnersPoolAndCanBeDealtAgain()
        {
            var deck = CreateDeck(matchSeed: 42, stablePlayerId: 17);
            BeginTurn(deck);
            var returnedCardId = GetHand(deck)[0].CardId;

            Assert.That(TrySelect(deck, returnedCardId), Is.True);
            Assert.That(Resolve(deck, returnedCardId, "Played"), Is.True);
            Assert.That(GetHand(deck).Select(card => card.CardId), Does.Not.Contain(returnedCardId));

            var wasDealtAgain = false;
            for (var turn = 0; turn < 32 && !wasDealtAgain; turn++)
            {
                BeginTurn(deck);
                var hand = GetHand(deck);
                if (hand.Any(card => card.CardId == returnedCardId))
                {
                    wasDealtAgain = true;
                    break;
                }

                foreach (var card in hand.Take(2))
                {
                    Assert.That(TrySelect(deck, card.CardId), Is.True);
                    Assert.That(Resolve(deck, card.CardId, "Played"), Is.True);
                }
            }

            Assert.That(wasDealtAgain, Is.True,
                "A successfully used card must return to the same player's pool for a future automatic refill.");
        }

        [Test]
        public void BlockedMovement_RemainsInHandButStillConsumesOneOfTwoUses()
        {
            var deck = CreateDeck(matchSeed: 42, stablePlayerId: 17);
            BeginTurn(deck);
            var hand = GetHand(deck);
            var movementCard = hand.FirstOrDefault(card => card.Category == CardCategory.Movement);
            Assert.That(movementCard, Is.Not.Null, "The deterministic fixture must begin with a movement card.");
            if (movementCard == null)
            {
                return;
            }

            Assert.That(TrySelect(deck, movementCard.CardId), Is.True);
            Assert.That(Resolve(deck, movementCard.CardId, "BlockedMovement"), Is.True);
            Assert.That(GetHand(deck).Select(card => card.CardId), Does.Contain(movementCard.CardId));
            Assert.That(GetSelectedUses(deck), Is.EqualTo(1));

            var otherCard = hand.First(card => card.CardId != movementCard.CardId);
            Assert.That(TrySelect(deck, otherCard.CardId), Is.True);
            Assert.That(GetSelectedUses(deck), Is.EqualTo(2));

            var thirdCard = hand.First(card => card.CardId != movementCard.CardId && card.CardId != otherCard.CardId);
            Assert.That(TrySelect(deck, thirdCard.CardId), Is.False);
        }

        [Test]
        public void Constructor_RequiresExactlyTwelveUniqueDefinitions()
        {
            var definitions = FindDefinitions();
            Assert.That(definitions, Has.Length.EqualTo(12));

            AssertConstructorThrows<ArgumentException>(definitions.Take(11).ToArray());
            var duplicateCatalog = definitions.Take(11).Concat(new[] { definitions[0] }).ToArray();
            AssertConstructorThrows<ArgumentException>(duplicateCatalog);
        }

        private static object CreateDeck(ulong matchSeed = 20260928, ulong stablePlayerId = 17)
        {
            var deckType = GetDeckType();
            var constructor = deckType.GetConstructor(new[]
            {
                typeof(IEnumerable<CardDefinition>),
                typeof(ulong),
                typeof(ulong)
            });
            Assert.That(constructor, Is.Not.Null,
                "PlayerCardDeck must accept the authored catalog, match seed, and stable numeric player ID.");
            return constructor.Invoke(new object[] { FindDefinitions(), matchSeed, stablePlayerId });
        }

        private static void AssertConstructorThrows<TException>(CardDefinition[] definitions)
            where TException : Exception
        {
            var constructor = GetDeckType().GetConstructor(new[]
            {
                typeof(IEnumerable<CardDefinition>),
                typeof(ulong),
                typeof(ulong)
            });
            Assert.That(constructor, Is.Not.Null);
            Assert.That(() => constructor.Invoke(new object[] { definitions, 1UL, 1UL }),
                Throws.TargetInvocationException.With.InnerException.TypeOf<TException>());
        }

        private static Type GetDeckType()
        {
            var type = typeof(CardDefinition).Assembly.GetType(DeckTypeName, throwOnError: false);
            Assert.That(type, Is.Not.Null,
                $"Expected {DeckTypeName} to provide the player's isolated deck/hand rules.");
            return type;
        }

        private static CardDefinition[] FindDefinitions()
        {
            return AssetDatabase.FindAssets("t:CardDefinition", new[] { "Assets" })
                .Select(AssetDatabase.GUIDToAssetPath)
                .Select(AssetDatabase.LoadAssetAtPath<CardDefinition>)
                .Where(definition => definition != null)
                .OrderBy(definition => definition.CardId, StringComparer.Ordinal)
                .ToArray();
        }

        private static void BeginTurn(object deck)
        {
            Invoke(deck, "BeginTurn");
        }

        private static IReadOnlyList<CardDefinition> GetHand(object deck)
        {
            var property = deck.GetType().GetProperty("Hand", BindingFlags.Public | BindingFlags.Instance);
            Assert.That(property, Is.Not.Null, "PlayerCardDeck must expose a read-only hand view to its owner.");
            return (IReadOnlyList<CardDefinition>)property.GetValue(deck);
        }

        private static int GetSelectedUses(object deck)
        {
            var property = deck.GetType().GetProperty("SelectedUsesThisTurn", BindingFlags.Public | BindingFlags.Instance);
            Assert.That(property, Is.Not.Null, "PlayerCardDeck must expose its local per-turn selection count.");
            return (int)property.GetValue(deck);
        }

        private static bool TrySelect(object deck, string cardId)
        {
            return (bool)Invoke(deck, "TrySelectCard", cardId);
        }

        private static bool Resolve(object deck, string cardId, string outcomeName)
        {
            var outcomeType = typeof(CardDefinition).Assembly.GetType(OutcomeTypeName, throwOnError: false);
            Assert.That(outcomeType, Is.Not.Null, $"Expected {OutcomeTypeName} for resolved card outcomes.");
            var outcome = Enum.Parse(outcomeType, outcomeName);
            return (bool)Invoke(deck, "ResolveSelectedCard", cardId, outcome);
        }

        private static object Invoke(object instance, string methodName, params object[] arguments)
        {
            var method = instance.GetType().GetMethod(methodName, BindingFlags.Public | BindingFlags.Instance);
            Assert.That(method, Is.Not.Null, $"PlayerCardDeck must provide {methodName}().");
            return method.Invoke(instance, arguments);
        }
    }
}
