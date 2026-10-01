using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;

namespace NavalCommander.CardMechanics
{
    public enum CardinalDirection
    {
        North,
        East,
        South,
        West
    }

    public enum RelativeDirection
    {
        Forward,
        Right,
        Backward,
        Left
    }

    public enum CardEffectPhase
    {
        Defense,
        Movement,
        Attack
    }

    public sealed class CardShipState
    {
        internal CardShipState(string shipId, int x, int y, CardinalDirection facing, int initialHealth)
        {
            ShipId = shipId;
            X = x;
            Y = y;
            Facing = facing;
            Health = initialHealth;
        }

        public string ShipId { get; }
        public int X { get; internal set; }
        public int Y { get; internal set; }
        public CardinalDirection Facing { get; internal set; }
        public int Health { get; internal set; }
    }

    public sealed class CardBoardState
    {
        private readonly List<CardShipState> _ships = new List<CardShipState>();
        private readonly List<GridPoint> _obstacles = new List<GridPoint>();

        public CardBoardState(int width, int height)
        {
            if (width <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(width));
            }

            if (height <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(height));
            }

            Width = width;
            Height = height;
        }

        public int Width { get; }
        public int Height { get; }

        public CardShipState AddShip(string shipId, int x, int y, CardinalDirection facing, int initialHealth = 100)
        {
            if (string.IsNullOrWhiteSpace(shipId))
            {
                throw new ArgumentException("A ship ID is required.", nameof(shipId));
            }

            if (!Enum.IsDefined(typeof(CardinalDirection), facing))
            {
                throw new ArgumentOutOfRangeException(nameof(facing));
            }

            if (initialHealth <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(initialHealth));
            }

            if (!IsInBounds(x, y) || IsObstacle(x, y) || FindShipAt(x, y) != null)
            {
                throw new InvalidOperationException("A ship must start in an unoccupied board cell.");
            }

            if (_ships.Any(ship => string.Equals(ship.ShipId, shipId, StringComparison.Ordinal)))
            {
                throw new ArgumentException("Ship IDs must be unique.", nameof(shipId));
            }

            var ship = new CardShipState(shipId, x, y, facing, initialHealth);
            _ships.Add(ship);
            return ship;
        }

        public void AddObstacle(int x, int y)
        {
            if (!IsInBounds(x, y) || FindShipAt(x, y) != null)
            {
                throw new InvalidOperationException("An obstacle must occupy an unoccupied board cell.");
            }

            if (!IsObstacle(x, y))
            {
                _obstacles.Add(new GridPoint(x, y));
            }
        }

        public CardShipState GetShip(string shipId)
        {
            CardShipState ship = _ships.FirstOrDefault(candidate =>
                string.Equals(candidate.ShipId, shipId, StringComparison.Ordinal));
            if (ship == null)
            {
                throw new KeyNotFoundException($"No ship with ID '{shipId}' exists on this board.");
            }

            return ship;
        }

        internal IReadOnlyList<CardShipState> Ships => _ships.AsReadOnly();

        internal bool IsInBounds(int x, int y)
        {
            return x >= 0 && x < Width && y >= 0 && y < Height;
        }

        internal bool IsObstacle(int x, int y)
        {
            return _obstacles.Any(point => point.X == x && point.Y == y);
        }

        internal CardShipState FindShipAt(int x, int y)
        {
            return _ships.FirstOrDefault(ship => ship.X == x && ship.Y == y);
        }

        private readonly struct GridPoint
        {
            public GridPoint(int x, int y)
            {
                X = x;
                Y = y;
            }

            public int X { get; }
            public int Y { get; }
        }
    }

    public sealed class CardPlayCommand
    {
        public CardPlayCommand(string shipId, string cardId, RelativeDirection? aimDirection = null)
        {
            if (string.IsNullOrWhiteSpace(shipId))
            {
                throw new ArgumentException("A ship ID is required.", nameof(shipId));
            }

            if (string.IsNullOrWhiteSpace(cardId))
            {
                throw new ArgumentException("A card ID is required.", nameof(cardId));
            }

            if (aimDirection.HasValue &&
                !Enum.IsDefined(typeof(RelativeDirection), aimDirection.Value))
            {
                throw new ArgumentOutOfRangeException(nameof(aimDirection));
            }

            ShipId = shipId;
            CardId = cardId;
            AimDirection = aimDirection;
        }

        public string ShipId { get; }
        public string CardId { get; }
        public RelativeDirection? AimDirection { get; }
    }

    public sealed class CardEffectResolution
    {
        internal CardEffectResolution(
            string shipId,
            string cardId,
            CardEffectPhase phase,
            CardUseOutcome outcome)
        {
            ShipId = shipId;
            CardId = cardId;
            Phase = phase;
            Outcome = outcome;
        }

        public string ShipId { get; }
        public string CardId { get; }
        public CardEffectPhase Phase { get; }
        public CardUseOutcome Outcome { get; }
    }

    public sealed class CardTurnResult
    {
        internal CardTurnResult(IEnumerable<CardEffectResolution> resolutions)
        {
            Resolutions = new ReadOnlyCollection<CardEffectResolution>(resolutions.ToList());
        }

        public IReadOnlyList<CardEffectResolution> Resolutions { get; }
    }

    public sealed class CardEffectResolver
    {
        private readonly Dictionary<string, CardDefinition> _catalog;

        public CardEffectResolver(IEnumerable<CardDefinition> catalog)
        {
            if (catalog == null)
            {
                throw new ArgumentNullException(nameof(catalog));
            }

            _catalog = catalog.ToDictionary(card => card.CardId, StringComparer.Ordinal);
        }

        public CardTurnResult ResolveTurn(CardBoardState board, IEnumerable<CardPlayCommand> commands)
        {
            if (board == null)
            {
                throw new ArgumentNullException(nameof(board));
            }

            if (commands == null)
            {
                throw new ArgumentNullException(nameof(commands));
            }

            var plannedCards = new List<PlannedCard>();
            foreach (CardPlayCommand command in commands)
            {
                if (command == null)
                {
                    throw new ArgumentException("Every command must reference a known card.", nameof(commands));
                }

                if (!_catalog.TryGetValue(command.CardId, out CardDefinition definition))
                {
                    throw new ArgumentException("Every command must reference a known card.", nameof(commands));
                }

                CardEffectPhase phase = GetPhase(definition);
                board.GetShip(command.ShipId);
                if (phase == CardEffectPhase.Attack &&
                    definition.CardId != "attack_three_shot" &&
                    !command.AimDirection.HasValue)
                {
                    throw new ArgumentException(
                        "Missile and Torpedo commands require a relative aim direction.",
                        nameof(commands));
                }

                plannedCards.Add(new PlannedCard(command, definition, phase));
            }

            var defensesByShip = new Dictionary<string, Queue<DefenseEffect>>(StringComparer.Ordinal);
            var resolutions = new List<CardEffectResolution>(plannedCards.Count);

            for (int phaseValue = (int)CardEffectPhase.Defense;
                 phaseValue <= (int)CardEffectPhase.Attack;
                 phaseValue++)
            {
                CardEffectPhase phase = (CardEffectPhase)phaseValue;
                foreach (PlannedCard plannedCard in plannedCards)
                {
                    if (plannedCard.Phase != phase)
                    {
                        continue;
                    }

                    CardUseOutcome outcome = ResolveCard(board, plannedCard, defensesByShip);
                    resolutions.Add(new CardEffectResolution(
                        plannedCard.Command.ShipId,
                        plannedCard.Definition.CardId,
                        phase,
                        outcome));
                }
            }

            return new CardTurnResult(resolutions);
        }

        private static CardEffectPhase GetPhase(CardDefinition definition)
        {
            switch (definition.Category)
            {
                case CardCategory.Defense:
                    if (definition.CardId == "defense_shield" || definition.CardId == "defense_mirror")
                    {
                        return CardEffectPhase.Defense;
                    }

                    break;
                case CardCategory.Movement:
                    if (definition.CardId == "movement_up" ||
                        definition.CardId == "movement_right" ||
                        definition.CardId == "movement_down" ||
                        definition.CardId == "movement_left" ||
                        definition.CardId == "movement_rotate_right_90" ||
                        definition.CardId == "movement_rotate_left_90" ||
                        definition.CardId == "movement_rotate_180")
                    {
                        return CardEffectPhase.Movement;
                    }

                    break;
                case CardCategory.Attack:
                    if (definition.CardId == "attack_missile" ||
                        definition.CardId == "attack_torpedo" ||
                        definition.CardId == "attack_three_shot")
                    {
                        return CardEffectPhase.Attack;
                    }

                    break;
            }

            throw new ArgumentException(
                $"Card '{definition.CardId}' does not have a supported effect for category '{definition.Category}'.",
                nameof(definition));
        }

        private static CardUseOutcome ResolveCard(
            CardBoardState board,
            PlannedCard plannedCard,
            Dictionary<string, Queue<DefenseEffect>> defensesByShip)
        {
            CardPlayCommand command = plannedCard.Command;
            CardDefinition definition = plannedCard.Definition;
            CardShipState ship = board.GetShip(command.ShipId);

            switch (plannedCard.Phase)
            {
                case CardEffectPhase.Defense:
                    AddDefense(ship, definition.CardId, defensesByShip);
                    return CardUseOutcome.Played;
                case CardEffectPhase.Movement:
                    return ResolveMovement(board, ship, definition.CardId);
                case CardEffectPhase.Attack:
                    ResolveAttack(board, ship, definition, command, defensesByShip);
                    return CardUseOutcome.Played;
                default:
                    throw new ArgumentOutOfRangeException(nameof(plannedCard));
            }
        }

        private static void AddDefense(
            CardShipState ship,
            string cardId,
            Dictionary<string, Queue<DefenseEffect>> defensesByShip)
        {
            if (!defensesByShip.TryGetValue(ship.ShipId, out Queue<DefenseEffect> defenses))
            {
                defenses = new Queue<DefenseEffect>();
                defensesByShip.Add(ship.ShipId, defenses);
            }

            DefenseEffect effect;
            switch (cardId)
            {
                case "defense_shield":
                    effect = DefenseEffect.Shield;
                    break;
                case "defense_mirror":
                    effect = DefenseEffect.Mirror;
                    break;
                default:
                    throw new ArgumentException("The card is not a supported defense effect.", nameof(cardId));
            }

            defenses.Enqueue(effect);
        }

        private static CardUseOutcome ResolveMovement(
            CardBoardState board,
            CardShipState ship,
            string cardId)
        {
            if (cardId == "movement_rotate_right_90")
            {
                ship.Facing = RotateRight(ship.Facing);
                return CardUseOutcome.Played;
            }

            if (cardId == "movement_rotate_left_90")
            {
                ship.Facing = RotateLeft(ship.Facing);
                return CardUseOutcome.Played;
            }

            if (cardId == "movement_rotate_180")
            {
                ship.Facing = RotateRight(RotateRight(ship.Facing));
                return CardUseOutcome.Played;
            }

            RelativeDirection relativeDirection;
            switch (cardId)
            {
                case "movement_up":
                    relativeDirection = RelativeDirection.Forward;
                    break;
                case "movement_right":
                    relativeDirection = RelativeDirection.Right;
                    break;
                case "movement_down":
                    relativeDirection = RelativeDirection.Backward;
                    break;
                case "movement_left":
                    relativeDirection = RelativeDirection.Left;
                    break;
                default:
                    throw new ArgumentException("The card is not a supported movement effect.", nameof(cardId));
            }

            CardinalDirection direction = ResolveDirection(ship.Facing, relativeDirection);
            GetOffset(direction, out int offsetX, out int offsetY);
            int destinationX = ship.X + offsetX;
            int destinationY = ship.Y + offsetY;
            if (!board.IsInBounds(destinationX, destinationY) || board.IsObstacle(destinationX, destinationY))
            {
                return CardUseOutcome.BlockedMovement;
            }

            CardShipState destinationShip = board.FindShipAt(destinationX, destinationY);
            if (destinationShip != null && !ReferenceEquals(destinationShip, ship))
            {
                return CardUseOutcome.BlockedMovement;
            }

            ship.X = destinationX;
            ship.Y = destinationY;
            return CardUseOutcome.Played;
        }

        private static void ResolveAttack(
            CardBoardState board,
            CardShipState attacker,
            CardDefinition definition,
            CardPlayCommand command,
            Dictionary<string, Queue<DefenseEffect>> defensesByShip)
        {
            if (definition.CardId == "attack_three_shot")
            {
                ResolveThreeShot(board, attacker, definition.DamagePerHitCell, defensesByShip);
                return;
            }

            CardinalDirection aimDirection = ResolveDirection(attacker.Facing, command.AimDirection.Value);
            GetOffset(attacker.Facing, out int forwardX, out int forwardY);
            GetOffset(aimDirection, out int aimX, out int aimY);
            int x = attacker.X + forwardX;
            int y = attacker.Y + forwardY;

            while (board.IsInBounds(x, y))
            {
                if (board.IsObstacle(x, y))
                {
                    return;
                }

                CardShipState target = board.FindShipAt(x, y);
                if (target != null)
                {
                    ApplyIncomingAttack(target, attacker, definition.DamagePerHitCell, defensesByShip);
                    return;
                }

                x += aimX;
                y += aimY;
            }
        }

        private static void ResolveThreeShot(
            CardBoardState board,
            CardShipState attacker,
            int damage,
            Dictionary<string, Queue<DefenseEffect>> defensesByShip)
        {
            GetOffset(attacker.Facing, out int forwardX, out int forwardY);
            GetOffset(RotateRight(attacker.Facing), out int rightX, out int rightY);
            int centerX = attacker.X + forwardX;
            int centerY = attacker.Y + forwardY;

            for (int lateralOffset = -1; lateralOffset <= 1; lateralOffset++)
            {
                int x = centerX + rightX * lateralOffset;
                int y = centerY + rightY * lateralOffset;
                if (!board.IsInBounds(x, y))
                {
                    continue;
                }

                CardShipState target = board.FindShipAt(x, y);
                if (target != null)
                {
                    ApplyIncomingAttack(target, attacker, damage, defensesByShip);
                }
            }
        }

        private static void ApplyIncomingAttack(
            CardShipState target,
            CardShipState attacker,
            int damage,
            Dictionary<string, Queue<DefenseEffect>> defensesByShip)
        {
            if (defensesByShip.TryGetValue(target.ShipId, out Queue<DefenseEffect> defenses) &&
                defenses.Count > 0)
            {
                DefenseEffect defense = defenses.Dequeue();
                if (defense == DefenseEffect.Mirror)
                {
                    ApplyDamage(attacker, damage);
                }

                return;
            }

            ApplyDamage(target, damage);
        }

        private static void ApplyDamage(CardShipState ship, int damage)
        {
            ship.Health = Math.Max(0, ship.Health - damage);
        }

        private static CardinalDirection ResolveDirection(
            CardinalDirection facing,
            RelativeDirection relativeDirection)
        {
            switch (relativeDirection)
            {
                case RelativeDirection.Forward:
                    return facing;
                case RelativeDirection.Right:
                    return RotateRight(facing);
                case RelativeDirection.Backward:
                    return RotateRight(RotateRight(facing));
                case RelativeDirection.Left:
                    return RotateLeft(facing);
                default:
                    throw new ArgumentOutOfRangeException(nameof(relativeDirection));
            }
        }

        private static CardinalDirection RotateRight(CardinalDirection direction)
        {
            return (CardinalDirection)(((int)direction + 1) % 4);
        }

        private static CardinalDirection RotateLeft(CardinalDirection direction)
        {
            return (CardinalDirection)(((int)direction + 3) % 4);
        }

        private static void GetOffset(CardinalDirection direction, out int offsetX, out int offsetY)
        {
            offsetX = 0;
            offsetY = 0;
            switch (direction)
            {
                case CardinalDirection.North:
                    offsetY = 1;
                    break;
                case CardinalDirection.East:
                    offsetX = 1;
                    break;
                case CardinalDirection.South:
                    offsetY = -1;
                    break;
                case CardinalDirection.West:
                    offsetX = -1;
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(direction));
            }
        }

        private enum DefenseEffect
        {
            Shield,
            Mirror
        }

        private readonly struct PlannedCard
        {
            public PlannedCard(CardPlayCommand command, CardDefinition definition, CardEffectPhase phase)
            {
                Command = command;
                Definition = definition;
                Phase = phase;
            }

            public CardPlayCommand Command { get; }
            public CardDefinition Definition { get; }
            public CardEffectPhase Phase { get; }
        }
    }
}
