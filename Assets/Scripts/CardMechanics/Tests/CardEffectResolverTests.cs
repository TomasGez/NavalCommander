using System;
using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace NavalCommander.CardMechanics.Tests
{
    public sealed class CardEffectResolverTests
    {
        private const string AttackerId = "attacker";
        private const string TargetId = "target";

        [TestCase("movement_up", 0, 1)]
        [TestCase("movement_right", 1, 0)]
        [TestCase("movement_down", 0, -1)]
        [TestCase("movement_left", -1, 0)]
        public void Movement_TranslatesOneCellRelativeToNorthFacingShip(
            string cardId,
            int expectedOffsetX,
            int expectedOffsetY)
        {
            var board = new CardBoardState(9, 9);
            CardShipState ship = board.AddShip(AttackerId, 4, 4, CardinalDirection.North);

            CardTurnResult turn = Resolve(board, new CardPlayCommand(AttackerId, cardId));

            Assert.That(ship.X, Is.EqualTo(4 + expectedOffsetX));
            Assert.That(ship.Y, Is.EqualTo(4 + expectedOffsetY));
            Assert.That(turn.Resolutions.Single().Outcome, Is.EqualTo(CardUseOutcome.Played));
        }

        [Test]
        public void MoveUp_UsesTheShipsForwardDirection()
        {
            var board = new CardBoardState(9, 9);
            CardShipState ship = board.AddShip(AttackerId, 4, 4, CardinalDirection.East);

            Resolve(board, new CardPlayCommand(AttackerId, "movement_up"));

            Assert.That(ship.X, Is.EqualTo(5));
            Assert.That(ship.Y, Is.EqualTo(4));
        }

        [TestCase("movement_rotate_right_90", CardinalDirection.East)]
        [TestCase("movement_rotate_left_90", CardinalDirection.West)]
        [TestCase("movement_rotate_180", CardinalDirection.South)]
        public void RotationCards_ChangeFacingWithoutMovingOneCellShip(
            string cardId,
            CardinalDirection expectedFacing)
        {
            var board = new CardBoardState(9, 9);
            CardShipState ship = board.AddShip(AttackerId, 4, 4, CardinalDirection.North);

            CardTurnResult turn = Resolve(board, new CardPlayCommand(AttackerId, cardId));

            Assert.That(ship.Facing, Is.EqualTo(expectedFacing));
            Assert.That(ship.X, Is.EqualTo(4));
            Assert.That(ship.Y, Is.EqualTo(4));
            Assert.That(turn.Resolutions.Single().Outcome, Is.EqualTo(CardUseOutcome.Played));
        }

        [Test]
        public void BlockedMovement_ReportsRetentionAndConsumesSelectedUse()
        {
            CardDefinition[] catalog = LoadCatalog();
            MovementDeckFixture fixture = CreateDeckWithDirectionalMovement(catalog);
            Assert.That(fixture, Is.Not.Null);
            Assert.That(fixture.Deck.TrySelectCard(fixture.Card.CardId), Is.True);

            var board = new CardBoardState(9, 9);
            CardShipState ship = board.AddShip(AttackerId, 4, 4, CardinalDirection.North);
            GetMovementOffset(fixture.Card.CardId, out int offsetX, out int offsetY);
            board.AddObstacle(ship.X + offsetX, ship.Y + offsetY);

            CardTurnResult turn = CreateResolver(catalog).ResolveTurn(
                board,
                new[] { new CardPlayCommand(AttackerId, fixture.Card.CardId) });

            CardEffectResolution movement = turn.Resolutions.Single();
            Assert.That(movement.Outcome, Is.EqualTo(CardUseOutcome.BlockedMovement));
            Assert.That(fixture.Deck.ResolveSelectedCard(movement.CardId, movement.Outcome), Is.True);
            Assert.That(fixture.Deck.Hand.Select(card => card.CardId), Does.Contain(fixture.Card.CardId));
            Assert.That(fixture.Deck.SelectedUsesThisTurn, Is.EqualTo(1));
            Assert.That(ship.X, Is.EqualTo(4));
            Assert.That(ship.Y, Is.EqualTo(4));
        }

        [TestCase("attack_missile", 30)]
        [TestCase("attack_torpedo", 50)]
        public void DirectionalAttacks_DamageOnlyTheFirstShipOnTheirRay(string cardId, int expectedDamage)
        {
            var board = new CardBoardState(9, 7);
            board.AddShip(AttackerId, 1, 3, CardinalDirection.East);
            CardShipState firstShip = board.AddShip("first", 3, 3, CardinalDirection.North);
            CardShipState secondShip = board.AddShip("second", 5, 3, CardinalDirection.North);

            Resolve(board, new CardPlayCommand(AttackerId, cardId, RelativeDirection.Forward));

            Assert.That(firstShip.Health, Is.EqualTo(100 - expectedDamage));
            Assert.That(secondShip.Health, Is.EqualTo(100));
        }

        [TestCase(CardinalDirection.North)]
        [TestCase(CardinalDirection.East)]
        public void ThreeShot_DamagesTheCenteredThreeCellRowImmediatelyAhead(
            CardinalDirection facing)
        {
            var board = new CardBoardState(9, 9);
            board.AddShip(AttackerId, 3, 3, facing);

            int[,] targetCells = facing == CardinalDirection.North
                ? new[,] { { 2, 4 }, { 3, 4 }, { 4, 4 } }
                : new[,] { { 4, 2 }, { 4, 3 }, { 4, 4 } };
            var targets = new CardShipState[3];
            for (int i = 0; i < targets.Length; i++)
            {
                targets[i] = board.AddShip(
                    $"row-target-{i}",
                    targetCells[i, 0],
                    targetCells[i, 1],
                    CardinalDirection.North);
            }

            CardShipState outsideFootprint = board.AddShip(
                "outside-footprint",
                facing == CardinalDirection.North ? 3 : 5,
                facing == CardinalDirection.North ? 5 : 3,
                CardinalDirection.North);

            Resolve(board, new CardPlayCommand(AttackerId, "attack_three_shot"));

            Assert.That(targets.Select(target => target.Health), Is.All.EqualTo(60));
            Assert.That(outsideFootprint.Health, Is.EqualTo(100));
        }

        [Test]
        public void Shield_AbsorbsOnlyTheFirstIncomingAttackThisTurn()
        {
            var board = new CardBoardState(7, 7);
            board.AddShip("missile-ship", 1, 3, CardinalDirection.East);
            board.AddShip("torpedo-ship", 3, 1, CardinalDirection.North);
            CardShipState target = board.AddShip(TargetId, 3, 3, CardinalDirection.West);

            Resolve(
                board,
                new CardPlayCommand(TargetId, "defense_shield"),
                new CardPlayCommand("missile-ship", "attack_missile", RelativeDirection.Forward),
                new CardPlayCommand("torpedo-ship", "attack_torpedo", RelativeDirection.Forward));

            Assert.That(target.Health, Is.EqualTo(50));
        }

        [Test]
        public void Mirror_ReflectsAndConsumesOnlyTheFirstIncomingAttackThisTurn()
        {
            var board = new CardBoardState(7, 7);
            CardShipState missileShip = board.AddShip("missile-ship", 1, 3, CardinalDirection.East);
            board.AddShip("torpedo-ship", 3, 1, CardinalDirection.North);
            CardShipState target = board.AddShip(TargetId, 3, 3, CardinalDirection.West);

            Resolve(
                board,
                new CardPlayCommand(TargetId, "defense_mirror"),
                new CardPlayCommand("missile-ship", "attack_missile", RelativeDirection.Forward),
                new CardPlayCommand("torpedo-ship", "attack_torpedo", RelativeDirection.Forward));

            Assert.That(missileShip.Health, Is.EqualTo(70));
            Assert.That(target.Health, Is.EqualTo(50));
        }

        [Test]
        public void TurnResolution_OrdersDefenseBeforeMovementBeforeAttack()
        {
            var board = new CardBoardState(7, 7);
            CardShipState attacker = board.AddShip(AttackerId, 1, 3, CardinalDirection.North);
            CardShipState target = board.AddShip(TargetId, 3, 3, CardinalDirection.West);

            CardTurnResult turn = Resolve(
                board,
                new CardPlayCommand(AttackerId, "attack_missile", RelativeDirection.Forward),
                new CardPlayCommand(AttackerId, "movement_rotate_right_90"),
                new CardPlayCommand(TargetId, "defense_shield"));

            Assert.That(
                turn.Resolutions.Select(resolution => resolution.Phase),
                Is.EqualTo(new[]
                {
                    CardEffectPhase.Defense,
                    CardEffectPhase.Movement,
                    CardEffectPhase.Attack
                }));
            Assert.That(attacker.Facing, Is.EqualTo(CardinalDirection.East));
            Assert.That(target.Health, Is.EqualTo(100));
        }

        private static CardTurnResult Resolve(CardBoardState board, params CardPlayCommand[] commands)
        {
            return CreateResolver(LoadCatalog()).ResolveTurn(board, commands);
        }

        private static CardEffectResolver CreateResolver(CardDefinition[] catalog)
        {
            return new CardEffectResolver(catalog);
        }

        private static CardDefinition[] LoadCatalog()
        {
            CardDefinition[] definitions = AssetDatabase.FindAssets("t:CardDefinition", new[] { "Assets" })
                .Select(AssetDatabase.GUIDToAssetPath)
                .Select(AssetDatabase.LoadAssetAtPath<CardDefinition>)
                .Where(definition => definition != null)
                .OrderBy(definition => definition.CardId, StringComparer.Ordinal)
                .ToArray();

            Assert.That(definitions, Has.Length.EqualTo(12), "The effect fixture requires all authored card definitions.");
            return definitions;
        }

        private static MovementDeckFixture CreateDeckWithDirectionalMovement(CardDefinition[] catalog)
        {
            for (ulong seed = 0; seed < 256; seed++)
            {
                var deck = new PlayerCardDeck(catalog, seed, 17);
                deck.BeginTurn();
                CardDefinition movementCard = deck.Hand.FirstOrDefault(card => IsDirectionalMovement(card.CardId));
                if (movementCard != null)
                {
                    return new MovementDeckFixture(deck, movementCard);
                }
            }

            Assert.Fail("The deterministic deck fixture did not find a directional movement card in 256 seeds.");
            return null;
        }

        private static bool IsDirectionalMovement(string cardId)
        {
            return cardId == "movement_up" ||
                   cardId == "movement_right" ||
                   cardId == "movement_down" ||
                   cardId == "movement_left";
        }

        private static void GetMovementOffset(string cardId, out int offsetX, out int offsetY)
        {
            offsetX = 0;
            offsetY = 0;
            switch (cardId)
            {
                case "movement_up":
                    offsetY = 1;
                    break;
                case "movement_right":
                    offsetX = 1;
                    break;
                case "movement_down":
                    offsetY = -1;
                    break;
                case "movement_left":
                    offsetX = -1;
                    break;
                default:
                    Assert.Fail($"'{cardId}' is not a directional movement card.");
                    break;
            }
        }

        private sealed class MovementDeckFixture
        {
            public MovementDeckFixture(PlayerCardDeck deck, CardDefinition card)
            {
                Deck = deck;
                Card = card;
            }

            public PlayerCardDeck Deck { get; }
            public CardDefinition Card { get; }
        }
    }
}
