using System;
using System.Collections.Generic;
using System.Linq;
using NavalCommander.CardMechanics;

namespace NavalCommander.Gameplay
{
    /// <summary>Qué le pasó a un barco al recibir (o rebotar) un ataque.</summary>
    public enum HitKind
    {
        Hit,       // daño recibido
        Shielded,  // el escudo absorbió el ataque
        Mirrored,  // el espejo devolvió el ataque (en el barco que se defiende)
        Reflected  // daño que vuelve al atacante por el espejo
    }

    /// <summary>Un impacto concreto de una carta de ataque.</summary>
    public struct HitInfo
    {
        public int Slot, X, Y, Kind, Damage;
        public bool Destroyed; // este impacto dejó al barco sin vida
    }

    /// <summary>Detalle de una carta resuelta, solo para mostrarla (el estado real lo calcula el resolver).</summary>
    public sealed class ResolutionDetail
    {
        public string ShipId, CardId;
        public int FromX, FromY, FromFacing; // barco justo antes de la carta
        public int ToX, ToY, ToFacing;       // barco justo después
        // Celdas objetivo en línea: (TargetX, TargetY) + paso * i. En un movimiento bloqueado es la celda a la que no pudo ir.
        public int TargetX, TargetY, StepX, StepY, TargetCount;
        public readonly List<HitInfo> Hits = new List<HitInfo>();
    }

    /// <summary>
    /// Reconstruye, carta por carta, lo que hace el CardEffectResolver (que solo informa el resultado final):
    /// repite el turno sobre copias del tablero con las primeras k cartas y compara. Así se saben las celdas
    /// alcanzadas, los impactos y los efectos de Escudo/Espejo sin tocar el resolver. Es solo informativo.
    /// Solo corre en el servidor, dentro de MatchRules.ResolveTurn y antes de resolver (necesita el tablero
    /// previo). MatchController convierte el resultado en ResolutionDto/HitDto para que TurnEffectsView lo anime.
    /// </summary>
    internal sealed class TurnTracer
    {
        private const int WallCell = 1;
        private const int HealthPadding = 1000; // vida extra en las copias para no perder el daño recibido al llegar a 0
        private const string ThreeShotId = "attack_three_shot";
        private const string MirrorId = "defense_mirror";

        private struct Pose
        {
            public int X, Y, Facing, Health;
        }

        private readonly Dictionary<string, CardDefinition> _catalog;
        private readonly CardEffectResolver _resolver;
        private readonly int[,] _cells;
        private readonly int _width, _height;

        public TurnTracer(CardDefinition[] catalog, CardEffectResolver resolver, int[,] cells)
        {
            _catalog = catalog.ToDictionary(card => card.CardId, StringComparer.Ordinal);
            _resolver = resolver;
            _cells = cells;
            _height = cells.GetLength(0);
            _width = cells.GetLength(1);
        }

        /// <summary>Un detalle por comando, en el mismo orden en que el resolver los procesa (Defensa, Movimiento, Ataque).</summary>
        public List<ResolutionDetail> Trace(CardBoardState board, IEnumerable<string> shipIds, IEnumerable<CardPlayCommand> commands)
        {
            List<CardPlayCommand> ordered = commands.OrderBy(c => (int)_catalog[c.CardId].Category).ToList(); // estable

            var states = new List<Dictionary<string, Pose>> { Capture(board, shipIds) };
            for (int k = 1; k <= ordered.Count; k++)
                states.Add(Replay(states[0], ordered.GetRange(0, k)));

            var defenses = new Dictionary<string, Queue<bool>>(); // true = espejo
            var details = new List<ResolutionDetail>(ordered.Count);
            for (int k = 0; k < ordered.Count; k++)
            {
                CardPlayCommand command = ordered[k];
                CardDefinition definition = _catalog[command.CardId];
                Dictionary<string, Pose> before = states[k], after = states[k + 1];
                Pose from = before[command.ShipId], to = after[command.ShipId];

                var detail = new ResolutionDetail
                {
                    ShipId = command.ShipId, CardId = command.CardId,
                    FromX = from.X, FromY = from.Y, FromFacing = from.Facing,
                    ToX = to.X, ToY = to.Y, ToFacing = to.Facing
                };

                switch (definition.Category)
                {
                    case CardCategory.Defense:
                        if (!defenses.TryGetValue(command.ShipId, out var queue)) defenses[command.ShipId] = queue = new Queue<bool>();
                        queue.Enqueue(command.CardId == MirrorId);
                        break;
                    case CardCategory.Movement:
                        TraceBlockedCell(detail, command.CardId, from);
                        break;
                    case CardCategory.Attack:
                        TraceAttack(detail, command, definition, before, after, defenses);
                        break;
                }
                details.Add(detail);
            }
            return details;
        }

        // ---------------- Estados ----------------

        private static Dictionary<string, Pose> Capture(CardBoardState board, IEnumerable<string> shipIds)
        {
            var poses = new Dictionary<string, Pose>();
            foreach (string id in shipIds)
            {
                CardShipState ship = board.GetShip(id);
                poses[id] = new Pose { X = ship.X, Y = ship.Y, Facing = (int)ship.Facing, Health = ship.Health };
            }
            return poses;
        }

        // Tablero nuevo con el estado inicial, resuelve solo las primeras cartas y lee cómo quedó.
        private Dictionary<string, Pose> Replay(Dictionary<string, Pose> start, List<CardPlayCommand> prefix)
        {
            var copy = new CardBoardState(_width, _height);
            for (int y = 0; y < _height; y++)
                for (int x = 0; x < _width; x++)
                    if (_cells[y, x] == WallCell) copy.AddObstacle(x, y);
            foreach (var pair in start)
                copy.AddShip(pair.Key, pair.Value.X, pair.Value.Y, (CardinalDirection)pair.Value.Facing, pair.Value.Health + HealthPadding);

            _resolver.ResolveTurn(copy, prefix);

            var result = Capture(copy, start.Keys);
            foreach (string id in start.Keys.ToList())
            {
                Pose pose = result[id];
                pose.Health = Math.Max(0, pose.Health - HealthPadding);
                result[id] = pose;
            }
            return result;
        }

        // ---------------- Movimiento ----------------

        // Para los pasos (arriba/derecha/abajo/izquierda) guarda la celda de destino: si estaba bloqueado, es donde chocó.
        private static void TraceBlockedCell(ResolutionDetail detail, string cardId, Pose from)
        {
            int relative;
            switch (cardId)
            {
                case "movement_up": relative = (int)RelativeDirection.Forward; break;
                case "movement_right": relative = (int)RelativeDirection.Right; break;
                case "movement_down": relative = (int)RelativeDirection.Backward; break;
                case "movement_left": relative = (int)RelativeDirection.Left; break;
                default: return; // los giros no tienen destino
            }

            Offset((from.Facing + relative) % 4, out int dx, out int dy);
            detail.TargetX = from.X + dx; detail.TargetY = from.Y + dy;
            detail.StepX = dx; detail.StepY = dy;
            detail.TargetCount = 1;
        }

        // ---------------- Ataques ----------------

        private void TraceAttack(ResolutionDetail detail, CardPlayCommand command, CardDefinition definition,
            Dictionary<string, Pose> before, Dictionary<string, Pose> after, Dictionary<string, Queue<bool>> defenses)
        {
            Pose attacker = before[command.ShipId];
            Offset(attacker.Facing, out int forwardX, out int forwardY);
            int x = attacker.X + forwardX, y = attacker.Y + forwardY;
            var line = new List<int[]>();
            int stepX, stepY;

            if (command.CardId == ThreeShotId)
            {
                Offset((attacker.Facing + 1) % 4, out stepX, out stepY); // hacia la derecha del barco
                for (int lateral = -1; lateral <= 1; lateral++)
                    if (InBounds(x + stepX * lateral, y + stepY * lateral)) line.Add(new[] { x + stepX * lateral, y + stepY * lateral });
            }
            else
            {
                Offset((attacker.Facing + (int)command.AimDirection.Value) % 4, out stepX, out stepY);
                while (InBounds(x, y))
                {
                    line.Add(new[] { x, y });
                    if (_cells[y, x] == WallCell || ShipAt(before, x, y) != null) break; // el proyectil se detiene
                    x += stepX; y += stepY;
                }
            }

            if (line.Count == 0) return;
            detail.TargetX = line[0][0]; detail.TargetY = line[0][1];
            detail.StepX = stepX; detail.StepY = stepY;
            detail.TargetCount = line.Count;

            // Impactos: el resolver los aplica en este mismo orden (Escudo/Espejo se gastan uno por ataque).
            foreach (int[] cell in line)
            {
                string targetId = ShipAt(before, cell[0], cell[1]);
                if (targetId == null) continue;

                int damage = definition.DamagePerHitCell;
                if (defenses.TryGetValue(targetId, out var queue) && queue.Count > 0)
                {
                    bool mirror = queue.Dequeue();
                    AddHit(detail, targetId, cell[0], cell[1], mirror ? HitKind.Mirrored : HitKind.Shielded, 0);
                    if (mirror) AddHit(detail, command.ShipId, attacker.X, attacker.Y, HitKind.Reflected, damage);
                }
                else AddHit(detail, targetId, cell[0], cell[1], HitKind.Hit, damage);
            }

            MarkDestroyed(detail, before, after);
        }

        private static void AddHit(ResolutionDetail detail, string shipId, int x, int y, HitKind kind, int damage)
        {
            detail.Hits.Add(new HitInfo { Slot = SlotOf(shipId), X = x, Y = y, Kind = (int)kind, Damage = damage });
        }

        // El último impacto con daño sobre un barco que pasó de vivo a muerto es el que lo hunde.
        private static void MarkDestroyed(ResolutionDetail detail, Dictionary<string, Pose> before, Dictionary<string, Pose> after)
        {
            foreach (var pair in before)
            {
                if (pair.Value.Health <= 0 || after[pair.Key].Health > 0) continue;

                int slot = SlotOf(pair.Key);
                for (int i = detail.Hits.Count - 1; i >= 0; i--)
                {
                    HitInfo hit = detail.Hits[i];
                    if (hit.Slot != slot || hit.Damage <= 0) continue;
                    hit.Destroyed = true;
                    detail.Hits[i] = hit;
                    break;
                }
            }
        }

        // ---------------- Geometría (igual que el resolver: Norte = +Y) ----------------

        private bool InBounds(int x, int y) => x >= 0 && x < _width && y >= 0 && y < _height;

        private static string ShipAt(Dictionary<string, Pose> poses, int x, int y)
        {
            foreach (var pair in poses)
                if (pair.Value.X == x && pair.Value.Y == y) return pair.Key;
            return null;
        }

        private static void Offset(int direction, out int dx, out int dy)
        {
            dx = 0; dy = 0;
            switch ((CardinalDirection)direction)
            {
                case CardinalDirection.North: dy = 1; break;
                case CardinalDirection.East: dx = 1; break;
                case CardinalDirection.South: dy = -1; break;
                case CardinalDirection.West: dx = -1; break;
            }
        }

        private static int SlotOf(string shipId) => int.Parse(shipId.Substring(1));
    }
}
