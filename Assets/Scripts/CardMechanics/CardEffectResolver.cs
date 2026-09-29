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
        internal CardShipState(string shipId, int x, int y, CardinalDirection facing)
        {
            ShipId = shipId;
            X = x;
            Y = y;
            Facing = facing;
            Health = 100;
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

        public CardShipState AddShip(string shipId, int x, int y, CardinalDirection facing)
        {
            if (string.IsNullOrWhiteSpace(shipId))
            {
                throw new ArgumentException("A ship ID is required.", nameof(shipId));
            }

            if (!Enum.IsDefined(typeof(CardinalDirection), facing))
            {
                throw new ArgumentOutOfRangeException(nameof(facing));
            }

            if (!IsInBounds(x, y) || IsObstacle(x, y) || FindShipAt(x, y) != null)
            {
                throw new InvalidOperationException("A ship must start in an unoccupied board cell.");
            }

            if (_ships.Any(ship => string.Equals(ship.ShipId, shipId, StringComparison.Ordinal)))
            {
                throw new ArgumentException("Ship IDs must be unique.", nameof(shipId));
            }

            var ship = new CardShipState(shipId, x, y, facing);
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

            foreach (CardPlayCommand command in commands)
            {
                if (command == null || !_catalog.ContainsKey(command.CardId))
                {
                    throw new ArgumentException("Every command must reference a known card.", nameof(commands));
                }
            }

            throw new NotImplementedException(
                "Card effects are intentionally unimplemented until the focused EditMode RED is observed.");
        }
    }
}
