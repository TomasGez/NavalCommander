using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using UnityEditor;

namespace NavalCommander.CardMechanics.Tests
{
    public sealed class TurnSubmissionCoordinatorTests
    {
        private const ulong HostClientId = 17;
        private const ulong RemoteClientId = 18;
        private const ulong UnknownClientId = 99;
        private const ulong MatchSeed = 42;

        private PlayerCardDeck _hostDeck;
        private PlayerCardDeck _remoteDeck;
        private TurnSubmissionCoordinator _coordinator;

        [SetUp]
        public void SetUp()
        {
            CardDefinition[] definitions = FindDefinitions();
            _hostDeck = CreateStartedDeck(definitions, HostClientId);
            _remoteDeck = CreateStartedDeck(definitions, RemoteClientId);

            _coordinator = new TurnSubmissionCoordinator(
                new Dictionary<ulong, PlayerCardDeck>
                {
                    { HostClientId, _hostDeck },
                    { RemoteClientId, _remoteDeck }
                });
        }

        [Test]
        public void SubmitCard_UsesTheSenderOwnersHandAndRejectsUnknownClients()
        {
            string hostOnlyCardId = _hostDeck.Hand
                .First(card => !_remoteDeck.Hand.Any(other => other.CardId == card.CardId))
                .CardId;

            Assert.That(_coordinator.TrySubmitCard(RemoteClientId, hostOnlyCardId), Is.False,
                "A client cannot submit a card that belongs to another player's hand.");
            Assert.That(_coordinator.TrySubmitCard(UnknownClientId, hostOnlyCardId), Is.False,
                "A client without an active match slot cannot submit an action.");
            Assert.That(_coordinator.TrySubmitCard(HostClientId, hostOnlyCardId), Is.True);
            Assert.That(_hostDeck.SelectedUsesThisTurn, Is.EqualTo(1));
            Assert.That(_remoteDeck.SelectedUsesThisTurn, Is.Zero);
        }

        [Test]
        public void SubmitCard_CountsOnlyValidOwnCardsAndAllowsAtMostTwoPerTurn()
        {
            string[] cardIds = _hostDeck.Hand.Select(card => card.CardId).ToArray();

            Assert.That(_coordinator.TrySubmitCard(HostClientId, "not-in-this-hand"), Is.False);
            Assert.That(_hostDeck.SelectedUsesThisTurn, Is.Zero,
                "An invalid card ID cannot consume a use.");
            Assert.That(_coordinator.TrySubmitCard(HostClientId, cardIds[0]), Is.True);
            Assert.That(_coordinator.TrySubmitCard(HostClientId, cardIds[0]), Is.False,
                "A duplicate submission cannot spend another use.");
            Assert.That(_coordinator.TrySubmitCard(HostClientId, cardIds[1]), Is.True);
            Assert.That(_coordinator.TrySubmitCard(HostClientId, cardIds[2]), Is.False,
                "A player cannot submit more than two cards in one turn.");
            Assert.That(_hostDeck.SelectedUsesThisTurn, Is.EqualTo(2));
        }

        [Test]
        public void BeginResolution_HidesActionsUntilEveryActivePlayerConfirms()
        {
            string hostCardId = _hostDeck.Hand[0].CardId;
            string remoteCardId = _remoteDeck.Hand[0].CardId;
            IReadOnlyList<TurnCardSubmission> submissions;

            Assert.That(_coordinator.Phase, Is.EqualTo(MatchTurnPhase.CollectingSubmissions));
            Assert.That(_coordinator.TrySubmitCard(HostClientId, hostCardId), Is.True);
            Assert.That(_coordinator.TrySubmitCard(RemoteClientId, remoteCardId), Is.True);

            Assert.That(_coordinator.TryBeginResolution(out submissions), Is.False);
            Assert.That(submissions, Is.Null,
                "Unconfirmed actions must not be exposed to another player.");

            Assert.That(_coordinator.TryConfirmTurn(HostClientId), Is.True);
            Assert.That(_coordinator.TryBeginResolution(out submissions), Is.False,
                "One player's confirmation cannot reveal the other player's unconfirmed choice.");
            Assert.That(submissions, Is.Null);

            Assert.That(_coordinator.TryConfirmTurn(RemoteClientId), Is.True);
            Assert.That(_coordinator.TryBeginResolution(out submissions), Is.True);
            Assert.That(_coordinator.Phase, Is.EqualTo(MatchTurnPhase.Resolving));
            Assert.That(submissions.Select(action => action.PlayerClientId),
                Is.EquivalentTo(new[] { HostClientId, RemoteClientId }));
            Assert.That(submissions.Select(action => action.CardId),
                Is.EquivalentTo(new[] { hostCardId, remoteCardId }));
        }

        [Test]
        public void ConfirmAndSubmit_RejectUnknownDuplicateAndLateRequests()
        {
            string firstCardId = _hostDeck.Hand[0].CardId;
            string secondCardId = _hostDeck.Hand[1].CardId;

            Assert.That(_coordinator.TryConfirmTurn(UnknownClientId), Is.False);
            Assert.That(_coordinator.TrySubmitCard(HostClientId, firstCardId), Is.True);
            Assert.That(_coordinator.TryConfirmTurn(HostClientId), Is.True);
            Assert.That(_coordinator.TryConfirmTurn(HostClientId), Is.False,
                "A player can confirm a turn only once.");
            Assert.That(_coordinator.TrySubmitCard(HostClientId, secondCardId), Is.False,
                "A confirmed player's submission is closed.");
            Assert.That(_coordinator.TryConfirmTurn(RemoteClientId), Is.True);

            IReadOnlyList<TurnCardSubmission> submissions;
            Assert.That(_coordinator.TryBeginResolution(out submissions), Is.True);
            Assert.That(_coordinator.TrySubmitCard(HostClientId, secondCardId), Is.False,
                "No action can be submitted after resolution begins.");
        }

        [Test]
        public void PrivateHand_IsAvailableOnlyToItsOwner()
        {
            IReadOnlyList<CardDefinition> hand;

            Assert.That(_coordinator.TryGetPrivateHand(HostClientId, HostClientId, out hand), Is.True);
            Assert.That(hand.Select(card => card.CardId),
                Is.EqualTo(_hostDeck.Hand.Select(card => card.CardId)));

            Assert.That(_coordinator.TryGetPrivateHand(HostClientId, RemoteClientId, out hand), Is.False,
                "A client cannot query another match slot's private hand.");
            Assert.That(hand, Is.Null);
            Assert.That(_coordinator.TryGetPrivateHand(UnknownClientId, HostClientId, out hand), Is.False);
            Assert.That(hand, Is.Null);

            Type coordinatorType = typeof(TurnSubmissionCoordinator);
            IEnumerable<Type> publicStateTypes = coordinatorType
                .GetProperties(BindingFlags.Public | BindingFlags.Instance)
                .Select(property => property.PropertyType)
                .Concat(coordinatorType
                    .GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
                    .Select(method => method.ReturnType))
                .Concat(coordinatorType
                    .GetFields(BindingFlags.Public | BindingFlags.Instance)
                    .Select(field => field.FieldType));
            Assert.That(publicStateTypes.Any(ExposesPlayerDeck), Is.False,
                "The coordinator must not publish a player's deck or a collection of decks.");
        }

        [Test]
        public void BeginNextTurn_RequiresAnExplicitOutcomeForEverySubmittedAction()
        {
            string firstCardId = _hostDeck.Hand[0].CardId;
            string secondCardId = _hostDeck.Hand[1].CardId;

            Assert.That(_coordinator.TrySubmitCard(HostClientId, firstCardId), Is.True);
            Assert.That(_coordinator.TrySubmitCard(HostClientId, secondCardId), Is.True);
            Assert.That(_coordinator.TryConfirmTurn(HostClientId), Is.True);
            Assert.That(_coordinator.TryConfirmTurn(RemoteClientId), Is.True);

            IReadOnlyList<TurnCardSubmission> submissions;
            Assert.That(_coordinator.TryBeginResolution(out submissions), Is.True);
            Assert.That(submissions, Has.Count.EqualTo(2));
            Assert.That(_coordinator.TryBeginNextTurn(), Is.False,
                "The coordinator cannot call PlayerCardDeck.BeginTurn while selected cards are unresolved.");
            Assert.That(_hostDeck.SelectedUsesThisTurn, Is.EqualTo(2));
            Assert.That(_hostDeck.Hand.Select(card => card.CardId), Does.Contain(firstCardId));
            Assert.That(_hostDeck.Hand.Select(card => card.CardId), Does.Contain(secondCardId));

            Assert.That(_coordinator.TryRecordActionOutcome(
                HostClientId, firstCardId, CardUseOutcome.Played), Is.True);
            Assert.That(_hostDeck.Hand.Select(card => card.CardId), Does.Not.Contain(firstCardId));
            Assert.That(_coordinator.TryBeginNextTurn(), Is.False,
                "One resolved action cannot clear the other selected card's pending outcome.");

            Assert.That(_coordinator.TryRecordActionOutcome(
                HostClientId, secondCardId, CardUseOutcome.Played), Is.True);
            Assert.That(_coordinator.TryRecordActionOutcome(
                HostClientId, secondCardId, CardUseOutcome.Played), Is.False,
                "An action outcome can be recorded only once.");
            Assert.That(_coordinator.TryBeginNextTurn(), Is.True);
            Assert.That(_coordinator.Phase, Is.EqualTo(MatchTurnPhase.CollectingSubmissions));
            Assert.That(_hostDeck.SelectedUsesThisTurn, Is.Zero);
            Assert.That(_hostDeck.Hand, Has.Count.EqualTo(3));
        }

        private static PlayerCardDeck CreateStartedDeck(CardDefinition[] definitions, ulong clientId)
        {
            var deck = new PlayerCardDeck(definitions, MatchSeed, clientId);
            deck.BeginTurn();
            return deck;
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

        private static bool ExposesPlayerDeck(Type type)
        {
            if (type == typeof(PlayerCardDeck))
            {
                return true;
            }

            if (type.IsArray)
            {
                return ExposesPlayerDeck(type.GetElementType());
            }

            return type.IsGenericType && type.GetGenericArguments().Any(ExposesPlayerDeck);
        }
    }
}
