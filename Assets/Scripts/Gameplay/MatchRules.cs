using System;
using System.Collections.Generic;
using NavalCommander.CardMechanics;

namespace NavalCommander.Gameplay
{
    /// <summary>Estado de un barco en un momento dado (tipo del juego, sin Netcode).</summary>
    public readonly struct ShipStatus
    {
        public readonly int Slot, X, Y, Facing, Health;

        public ShipStatus(int slot, int x, int y, int facing, int health)
        {
            Slot = slot; X = x; Y = y; Facing = facing; Health = health;
        }
    }

    /// <summary>Una carta que un jugador quiere jugar (todavía sin validar).</summary>
    public readonly struct PlayInput
    {
        public readonly string CardId;
        public readonly int Aim; // -1 = sin dirección, 0..3 = RelativeDirection

        public PlayInput(string cardId, int aim) { CardId = cardId; Aim = aim; }
    }

    /// <summary>Resultado de una carta ya resuelta.</summary>
    public readonly struct ResolutionInfo
    {
        public readonly int Slot;
        public readonly string CardId;
        public readonly int Phase, Outcome;

        public ResolutionInfo(int slot, string cardId, int phase, int outcome)
        {
            Slot = slot; CardId = cardId; Phase = phase; Outcome = outcome;
        }
    }

    /// <summary>
    /// REGLAS de la partida, sin ninguna dependencia de red: arma el tablero, reparte manos,
    /// valida jugadas, resuelve turnos y decide el ganador. Al no usar Netcode se puede probar solo
    /// y funciona igual en un host o en un servidor dedicado.
    /// </summary>
    public sealed class MatchRules
    {
        private const int WallCell = 1;
        private const int FirstSpawnCell = 2; // 2 = spawn del slot 0, 3 = slot 1, ...

        private readonly CardDefinition[] _catalog;
        private readonly CardEffectResolver _resolver;
        private readonly bool _movementOnly;
        private readonly int _maxPlaysPerTurn;
        private readonly string[] _movementHand;
        private readonly HashSet<string> _movementIds = new HashSet<string>();
        private readonly Dictionary<int, PlayerCardDeck> _decks = new Dictionary<int, PlayerCardDeck>();
        private readonly HashSet<int> _forfeited = new HashSet<int>();
        private CardBoardState _board;

        public MatchRules(CardDefinition[] catalog, bool movementOnly, int maxPlaysPerTurn)
        {
            _catalog = catalog;
            _movementOnly = movementOnly;
            _maxPlaysPerTurn = maxPlaysPerTurn;
            _resolver = new CardEffectResolver(catalog);

            var names = new List<string>();
            foreach (var card in catalog)
                if (card.Category == CardCategory.Movement) { names.Add(card.CardId); _movementIds.Add(card.CardId); }
            names.Sort(StringComparer.Ordinal);
            _movementHand = names.ToArray();
        }

        // ---------------- Preparación ----------------

        /// <summary>Arma el tablero con obstáculos y un barco por slot, mirando al centro.</summary>
        public void Start(int[,] cells, IReadOnlyList<int> slots, ulong seed)
        {
            int height = cells.GetLength(0), width = cells.GetLength(1);
            _board = new CardBoardState(width, height);

            for (int y = 0; y < height; y++)
                for (int x = 0; x < width; x++)
                    if (cells[y, x] == WallCell) _board.AddObstacle(x, y);

            foreach (int slot in slots)
            {
                if (!TryFindSpawn(cells, slot, out int sx, out int sy))
                    throw new InvalidOperationException($"El mapa no tiene spawn para el slot {slot}");

                _board.AddShip(ShipId(slot), sx, sy, FacingToCenter(sx, sy, width, height));
                _decks[slot] = new PlayerCardDeck(_catalog, seed, (ulong)slot);
            }
        }

        private static bool TryFindSpawn(int[,] cells, int slot, out int x, out int y)
        {
            for (y = 0; y < cells.GetLength(0); y++)
                for (x = 0; x < cells.GetLength(1); x++)
                    if (cells[y, x] == FirstSpawnCell + slot) return true;
            x = 0; y = 0; return false;
        }

        // En el resolver North = +Y y South = -Y.
        private static CardinalDirection FacingToCenter(int x, int y, int width, int height)
        {
            float dx = (width - 1) / 2f - x, dy = (height - 1) / 2f - y;
            if (Math.Abs(dx) >= Math.Abs(dy)) return dx >= 0 ? CardinalDirection.East : CardinalDirection.West;
            return dy >= 0 ? CardinalDirection.North : CardinalDirection.South;
        }

        // ---------------- Consultas ----------------

        public bool IsAlive(int slot) =>
            _decks.ContainsKey(slot) && !_forfeited.Contains(slot) && _board.GetShip(ShipId(slot)).Health > 0;

        public void Forfeit(int slot) => _forfeited.Add(slot);

        public List<ShipStatus> GetShipStatuses()
        {
            var list = new List<ShipStatus>();
            foreach (int slot in _decks.Keys)
            {
                var ship = _board.GetShip(ShipId(slot));
                list.Add(new ShipStatus(slot, ship.X, ship.Y, (int)ship.Facing, ship.Health));
            }
            return list;
        }

        /// <summary>true cuando queda a lo sumo un barco vivo. winnerSlot = -1 si no quedó ninguno.</summary>
        public bool TryGetWinner(out int winnerSlot)
        {
            var alive = new List<int>();
            foreach (int slot in _decks.Keys)
                if (IsAlive(slot)) alive.Add(slot);

            winnerSlot = alive.Count == 1 ? alive[0] : -1;
            return alive.Count <= 1;
        }

        // ---------------- Turno ----------------

        /// <summary>Ids de las cartas que recibe el jugador este turno.</summary>
        public string[] DealHand(int slot)
        {
            if (_movementOnly) return _movementHand;

            var deck = _decks[slot];
            deck.BeginTurn();
            var ids = new string[deck.Hand.Count];
            for (int i = 0; i < ids.Length; i++) ids[i] = deck.Hand[i].CardId;
            return ids;
        }

        /// <summary>Convierte lo que pidió el jugador en comandos válidos; descarta lo ilegal.</summary>
        public List<CardPlayCommand> BuildCommands(int slot, IReadOnlyList<PlayInput> plays)
        {
            var commands = new List<CardPlayCommand>();
            if (!IsAlive(slot) || plays == null) return commands;

            var deck = _decks[slot];
            var seen = new HashSet<string>();
            foreach (var play in plays)
            {
                if (!seen.Add(play.CardId)) continue; // no repetir la misma carta

                if (_movementOnly)
                {
                    if (!_movementIds.Contains(play.CardId) || commands.Count >= _maxPlaysPerTurn) continue;
                }
                else if (!deck.TrySelectCard(play.CardId)) continue; // no está en la mano o excede el máximo

                RelativeDirection? aim = (play.Aim >= 0 && play.Aim <= 3)
                    ? (RelativeDirection)play.Aim
                    : (RelativeDirection?)null;
                commands.Add(new CardPlayCommand(ShipId(slot), play.CardId, aim));
            }
            return commands;
        }

        /// <summary>Resuelve todas las jugadas del turno (Defensa -> Movimiento -> Ataque) y devuelve el informe.</summary>
        public List<ResolutionInfo> ResolveTurn(List<CardPlayCommand> commands)
        {
            var report = new List<ResolutionInfo>();
            CardTurnResult result = _resolver.ResolveTurn(_board, commands);

            foreach (var resolution in result.Resolutions)
            {
                int slot = SlotOf(resolution.ShipId);
                if (!_movementOnly && _decks.TryGetValue(slot, out var deck))
                    deck.ResolveSelectedCard(resolution.CardId, resolution.Outcome);

                report.Add(new ResolutionInfo(slot, resolution.CardId, (int)resolution.Phase, (int)resolution.Outcome));
            }
            return report;
        }

        private static string ShipId(int slot) => "P" + slot;
        private static int SlotOf(string shipId) => int.Parse(shipId.Substring(1));
    }
}
