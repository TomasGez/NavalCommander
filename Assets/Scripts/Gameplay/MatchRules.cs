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
        public readonly ResolutionDetail Detail; // celdas, impactos y posiciones para mostrar la carta (puede ser null)

        public ResolutionInfo(int slot, string cardId, int phase, int outcome, ResolutionDetail detail = null)
        {
            Slot = slot; CardId = cardId; Phase = phase; Outcome = outcome; Detail = detail;
        }
    }

    /// <summary>
    /// REGLAS de la partida, sin ninguna dependencia de red: arma el tablero, reparte manos,
    /// valida jugadas, resuelve turnos y decide el ganador. Al no usar Netcode se puede probar solo
    /// y funciona igual en un host o en un servidor dedicado.
    /// Solo la instancia del servidor la crea y la usa: MatchController la llama en cada paso del turno
    /// y traduce sus resultados (ShipStatus, ResolutionInfo) a los DTO de red. Delega el cálculo de
    /// efectos en CardEffectResolver / PlayerCardDeck (CardMechanics) y el detalle visual en TurnTracer.
    /// </summary>
    public sealed class MatchRules
    {
        private const int WallCell = 1;
        private const int FirstSpawnCell = 2; // 2 = spawn del slot 0, 3 = slot 1, ...

        private readonly CardDefinition[] _catalog;
        private readonly CardEffectResolver _resolver;
        private readonly Dictionary<string, CardDefinition> _byId = new Dictionary<string, CardDefinition>();
        private readonly bool _movementOnly;
        private readonly int _maxPlaysPerTurn;
        private readonly string[] _movementHand;
        private readonly HashSet<string> _movementIds = new HashSet<string>();
        private readonly Dictionary<int, PlayerCardDeck> _decks = new Dictionary<int, PlayerCardDeck>();
        private readonly HashSet<int> _forfeited = new HashSet<int>();
        private CardBoardState _board;
        private TurnTracer _tracer;

        public MatchRules(CardDefinition[] catalog, bool movementOnly, int maxPlaysPerTurn)
        {
            _catalog = catalog;
            _movementOnly = movementOnly;
            _maxPlaysPerTurn = maxPlaysPerTurn;
            _resolver = new CardEffectResolver(catalog);

            var names = new List<string>();
            foreach (var card in catalog) _byId[card.CardId] = card;
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
            _tracer = new TurnTracer(_catalog, _resolver, cells);

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
                if (NeedsAim(play.CardId) && (play.Aim < 0 || play.Aim > 3)) continue; // sin dirección el resolver rechazaría todo el turno

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

        private bool NeedsAim(string cardId) =>
            _byId.TryGetValue(cardId, out var card) && card.Category == CardCategory.Attack && cardId != "attack_three_shot";

        /// <summary>Resuelve todas las jugadas del turno (Defensa -> Movimiento -> Ataque) y devuelve el informe.</summary>
        public List<ResolutionInfo> ResolveTurn(List<CardPlayCommand> commands)
        {
            var report = new List<ResolutionInfo>();
            List<ResolutionDetail> details = TryTrace(commands); // antes de resolver: necesita el tablero previo
            CardTurnResult result = _resolver.ResolveTurn(_board, commands);

            for (int i = 0; i < result.Resolutions.Count; i++)
            {
                var resolution = result.Resolutions[i];
                ResolutionDetail detail = details != null && i < details.Count && details[i].CardId == resolution.CardId ? details[i] : null;
                int slot = SlotOf(resolution.ShipId);
                if (!_movementOnly && _decks.TryGetValue(slot, out var deck))
                    deck.ResolveSelectedCard(resolution.CardId, resolution.Outcome);

                report.Add(new ResolutionInfo(slot, resolution.CardId, (int)resolution.Phase, (int)resolution.Outcome, detail));
            }
            return report;
        }

        // El detalle es solo visual: si falla, el turno se resuelve igual y solo se pierde la animación.
        private List<ResolutionDetail> TryTrace(List<CardPlayCommand> commands)
        {
            try
            {
                var shipIds = new List<string>();
                foreach (int slot in _decks.Keys) shipIds.Add(ShipId(slot));
                return _tracer.Trace(_board, shipIds, commands);
            }
            catch (Exception e)
            {
                UnityEngine.Debug.LogWarning($"No se pudo detallar el turno para la animación: {e.Message}");
                return null;
            }
        }

        private static string ShipId(int slot) => "P" + slot;
        private static int SlotOf(string shipId) => int.Parse(shipId.Substring(1));
    }
}
