using System;
using System.Collections;
using System.Collections.Generic;
using NavalCommander.CardMechanics;
using NavalCommander.Gameplay;
using NavalCommander.Networking;
using UnityEngine;

namespace NavalCommander.Presentation
{
    /// <summary>
    /// Responsabilidad única: mostrar CÓMO se resolvió el turno (defensas, movimientos y ataques con su celda objetivo,
    /// proyectil, impacto, daño, hundimiento) usando solo formas dibujadas por código. Solo lee lo que el servidor
    /// envió (ResolutionDto + HitDto); nunca decide nada. Se crea sola desde BoardView.
    /// Corre en cada cliente (y en el host); se suscribe a MatchEvents.TurnResolvedDetailed, que dispara
    /// MatchController por RPC, y no hace nada en un servidor dedicado. BoardView le consulta ShipOffset y
    /// KeepsShipVisible para que el barco se deslice, vibre o siga visible hasta que lo alcance el proyectil.
    /// </summary>
    public class TurnEffectsView : MonoBehaviour
    {
        private const float Z = -0.3f;                // delante del tablero y de los barcos
        private const float MovementDelay = 0.15f;    // la defensa se ve primero, luego el movimiento
        private const float AttackStart = 0.7f;       // los ataques empiezan cuando terminó el movimiento
        private const float AttackSpan = 1.8f;        // tiempo máximo para escalonar todos los ataques

        private static readonly Color ShieldColor = new Color(0.3f, 0.9f, 1f);
        private static readonly Color MirrorColor = new Color(0.9f, 0.5f, 1f);
        private static readonly Color HitColor = new Color(1f, 0.25f, 0.2f);
        private static readonly Color FireColor = new Color(1f, 0.6f, 0.1f);
        private static readonly Color WallColor = new Color(0.7f, 0.7f, 0.7f);

        private struct Slide
        {
            public Vector3 Delta;     // desplazamiento visual inicial respecto a la celda final
            public float Start, Duration;
            public bool Bump;         // true = va y vuelve (choque), false = llega a la celda final
        }

        private sealed class FloatingText
        {
            public string Text;
            public Vector3 World;
            public Color Color;
            public float Start, Duration;
            public bool Big;
        }

        private sealed class Frame
        {
            public GameObject Root;
            public SpriteRenderer[] Sides;
            public void SetColor(Color color) { foreach (var side in Sides) side.color = color; }
        }

        private BoardLayout _layout;
        private BoardView _board;
        private Transform _root;
        private GUIStyle _style;
        private readonly Dictionary<int, List<Slide>> _slides = new Dictionary<int, List<Slide>>();
        private readonly Dictionary<int, float> _shakes = new Dictionary<int, float>();
        private readonly Dictionary<int, int> _pendingDeaths = new Dictionary<int, int>();
        private readonly List<FloatingText> _texts = new List<FloatingText>();
        private float _deathGuardUntil;

        private float Cs => _layout.CellSize;

        private void Awake()
        {
            _layout = GetComponent<BoardLayout>();
            _board = GetComponent<BoardView>();
            _root = new GameObject("TurnEffects").transform;
            _root.SetParent(transform, false);
        }

        private void OnEnable() { MatchEvents.TurnResolvedDetailed += HandleTurn; }
        private void OnDisable() { MatchEvents.TurnResolvedDetailed -= HandleTurn; }

        private void Update()
        {
            _texts.RemoveAll(t => Time.time > t.Start + t.Duration);
            if (_pendingDeaths.Count > 0 && Time.time > _deathGuardUntil) _pendingDeaths.Clear(); // red de seguridad
        }

        // ---------------- Lo que consulta BoardView ----------------

        /// <summary>Desplazamiento visual del barco: se desliza al moverse, choca al bloquearse y vibra al recibir un golpe.</summary>
        public Vector3 ShipOffset(int slot)
        {
            Vector3 offset = Vector3.zero;
            if (_slides.TryGetValue(slot, out var slides))
                foreach (var slide in slides)
                {
                    float t = Mathf.Clamp01((Time.time - slide.Start) / slide.Duration);
                    offset += slide.Delta * (slide.Bump ? Mathf.Sin(Mathf.PI * t) : 1f - t * t * (3f - 2f * t));
                }
            if (_shakes.TryGetValue(slot, out float until) && Time.time < until)
                offset += (Vector3)(UnityEngine.Random.insideUnitCircle * (0.06f * Cs));
            return offset;
        }

        /// <summary>true mientras un barco hundido todavía no recibió el golpe que lo hunde en pantalla.</summary>
        public bool KeepsShipVisible(int slot) => _pendingDeaths.TryGetValue(slot, out int count) && count > 0;

        // ---------------- Entrada del informe del turno ----------------

        private void HandleTurn(ResolutionDto[] resolutions, HitDto[] hits)
        {
            if (Application.isBatchMode || _layout == null) return;

            foreach (var list in _slides.Values) list.RemoveAll(s => Time.time > s.Start + s.Duration);

            int attacks = 0;
            foreach (var r in resolutions) if (r.Phase == (int)CardEffectPhase.Attack) attacks++;
            float gap = Mathf.Min(0.5f, AttackSpan / Mathf.Max(1, attacks));

            var movementClock = new Dictionary<int, float>(); // dos movimientos del mismo barco se muestran uno tras otro
            int attackIndex = 0;
            for (int i = 0; i < resolutions.Length; i++)
            {
                ResolutionDto r = resolutions[i];
                switch ((CardEffectPhase)r.Phase)
                {
                    case CardEffectPhase.Defense:
                        StartCoroutine(PlayDefense(r));
                        break;
                    case CardEffectPhase.Movement:
                        if (!movementClock.TryGetValue(r.Slot, out float start)) start = Time.time + MovementDelay;
                        movementClock[r.Slot] = start + 0.45f;
                        StartMovement(r, start);
                        break;
                    case CardEffectPhase.Attack:
                        StartCoroutine(PlayAttack(r, HitsOf(hits, i), AttackStart + gap * attackIndex++));
                        break;
                }
            }
        }

        private static List<HitDto> HitsOf(HitDto[] hits, int index)
        {
            var list = new List<HitDto>();
            foreach (var hit in hits) if (hit.Index == index) list.Add(hit);
            return list;
        }

        // ---------------- Defensa ----------------

        private IEnumerator PlayDefense(ResolutionDto r)
        {
            bool mirror = r.CardId.ToString() == "defense_mirror";
            Color color = mirror ? MirrorColor : ShieldColor;
            Vector3 fallback = Cell(r.ToX, r.ToY);
            Frame frame = MakeFrame(color, 24);
            AddText(mirror ? "Espejo" : "Escudo", fallback + Vector3.up * (0.6f * Cs), color);

            // Escudo = marco que se expande; Espejo = el mismo marco girado 45° (rombo).
            yield return Animate(1.1f, t =>
            {
                frame.Root.transform.position = ShipPos(r.Slot, fallback);
                frame.Root.transform.localScale = Vector3.one * (Cs * Mathf.Lerp(1.0f, 1.3f, t));
                frame.Root.transform.rotation = Quaternion.Euler(0f, 0f, mirror ? 45f : 0f);
                frame.SetColor(Alpha(color, 1f - t));
            }, frame.Root);
        }

        // ---------------- Movimiento ----------------

        private void StartMovement(ResolutionDto r, float start)
        {
            Vector3 from = Cell(r.FromX, r.FromY), to = Cell(r.ToX, r.ToY);
            bool blocked = r.Outcome == (int)CardUseOutcome.BlockedMovement;
            bool moved = r.FromX != r.ToX || r.FromY != r.ToY;

            if (blocked && r.TargetCount > 0)
                AddSlide(r.Slot, new Slide { Delta = (Cell(r.TargetX, r.TargetY) - to) * 0.3f, Start = start, Duration = 0.3f, Bump = true });
            else if (moved)
                AddSlide(r.Slot, new Slide { Delta = from - to, Start = start, Duration = 0.35f });

            StartCoroutine(PlayMovement(r, start));
        }

        private void AddSlide(int slot, Slide slide)
        {
            if (!_slides.TryGetValue(slot, out var list)) _slides[slot] = list = new List<Slide>();
            list.Add(slide);
        }

        private IEnumerator PlayMovement(ResolutionDto r, float start)
        {
            yield return Wait(start - Time.time);

            Color color = _board.ColorOf(r.Slot);
            Vector3 from = Cell(r.FromX, r.FromY), to = Cell(r.ToX, r.ToY);

            if (r.Outcome == (int)CardUseOutcome.BlockedMovement)
            {
                // Choca contra el borde, una pared u otro barco: una X roja en la celda a la que no pudo ir.
                Vector3 blockedCell = r.TargetCount > 0 ? Cell(r.TargetX, r.TargetY) : to;
                AddText("Bloqueado", to + Vector3.up * (0.6f * Cs), HitColor);
                var armA = Quad(HitColor, 26, blockedCell, new Vector2(0.9f, 0.12f) * Cs, 45f);
                var armB = Quad(HitColor, 26, blockedCell, new Vector2(0.9f, 0.12f) * Cs, -45f);
                yield return Animate(1f, t => { armA.color = armB.color = Alpha(HitColor, 1f - t); }, armA.gameObject, armB.gameObject);
            }
            else if (r.FromX != r.ToX || r.FromY != r.ToY)
            {
                // Estela: barco fantasma en la celda de origen y una flecha hasta la de destino.
                var ghost = Quad(Alpha(color, 0.4f), 20, from, Vector2.one * (0.8f * Cs));
                var trail = Quad(Alpha(color, 0.6f), 20, from, Vector2.one);
                var head = Quad(Alpha(color, 0.8f), 21, to, Vector2.one * (0.25f * Cs), 45f);
                PlaceLine(trail.transform, from, to, 0.1f * Cs);
                yield return Animate(0.9f, t =>
                {
                    ghost.color = Alpha(color, 0.4f * (1f - t));
                    trail.color = Alpha(color, 0.6f * (1f - t));
                    head.color = Alpha(color, 0.8f * (1f - t));
                }, ghost.gameObject, trail.gameObject, head.gameObject);
            }
            else if (r.FromFacing != r.ToFacing)
            {
                int turn = (r.ToFacing - r.FromFacing + 4) % 4; // 1 = derecha (horario), 3 = izquierda, 2 = media vuelta
                float angle = turn == 1 ? -90f : turn == 3 ? 90f : 180f;
                AddText(turn == 1 ? "Giro der. 90°" : turn == 3 ? "Giro izq. 90°" : "Giro 180°", to + Vector3.up * (0.6f * Cs), color);

                Frame frame = MakeFrame(color, 24);
                yield return Animate(0.6f, t =>
                {
                    frame.Root.transform.position = ShipPos(r.Slot, to);
                    frame.Root.transform.localScale = Vector3.one * (1.1f * Cs);
                    frame.Root.transform.rotation = Quaternion.Euler(0f, 0f, angle * t);
                    frame.SetColor(Alpha(color, 1f - t * t));
                }, frame.Root);
            }
        }

        // ---------------- Ataque ----------------

        private IEnumerator PlayAttack(ResolutionDto r, List<HitDto> hits, float delay)
        {
            yield return Wait(delay);

            Color color = _board.ColorOf(r.Slot);
            Vector3 origin = Cell(r.FromX, r.FromY);
            if (r.TargetCount <= 0)
            {
                AddText("Sin blanco", origin + Vector3.up * (0.6f * Cs), WallColor); // apunta fuera del tablero
                yield break;
            }

            var cells = new Vector3[r.TargetCount];
            for (int i = 0; i < cells.Length; i++) cells[i] = Cell(r.TargetX + r.StepX * i, r.TargetY + r.StepY * i);

            // El barco hundido sigue en pantalla hasta que el proyectil llegue.
            foreach (var hit in hits)
                if (hit.Destroyed != 0)
                {
                    _pendingDeaths[hit.Slot] = (_pendingDeaths.TryGetValue(hit.Slot, out int n) ? n : 0) + 1;
                    _deathGuardUntil = Time.time + 8f;
                }

            MarkCells(r, cells, hits, color);
            Burst(origin, Color.white, 0.6f, 4, 0.3f); // fogonazo
            yield return Wait(Launch(r.CardId.ToString(), origin, cells));

            yield return ShowImpacts(r, cells, hits, origin);
        }

        // Cuadros translúcidos sobre cada celda alcanzada (rojos las que impactan).
        private void MarkCells(ResolutionDto r, Vector3[] cells, List<HitDto> hits, Color color)
        {
            for (int i = 0; i < cells.Length; i++)
            {
                int cx = r.TargetX + r.StepX * i, cy = r.TargetY + r.StepY * i;
                bool isHit = hits.Exists(h => h.Kind != (int)HitKind.Reflected && h.X == cx && h.Y == cy);
                Color tint = isHit ? HitColor : color;
                var mark = Quad(Alpha(tint, 0.4f), 19, cells[i], Vector2.one * (0.9f * Cs));
                StartCoroutine(Animate(1.4f, t => mark.color = Alpha(tint, 0.45f * (1f - t) * (0.7f + 0.3f * Mathf.Sin(t * 25f))), mark.gameObject));
            }
        }

        // Dispara el proyectil de cada tipo de carta y devuelve lo que tarda en llegar.
        private float Launch(string cardId, Vector3 origin, Vector3[] cells)
        {
            if (cardId == "attack_three_shot") // tres balas rápidas, una por celda
            {
                float longest = 0f;
                foreach (var cell in cells)
                    longest = Mathf.Max(longest, Fly(new[] { origin, cell }, new Color(1f, 0.95f, 0.4f), Vector2.one * 0.16f, 16f, false));
                return longest;
            }

            var path = new Vector3[cells.Length + 1]; // el misil/torpedo recorre las celdas una tras otra
            path[0] = origin;
            Array.Copy(cells, 0, path, 1, cells.Length);
            return cardId == "attack_torpedo"
                ? Fly(path, new Color(0.4f, 0.8f, 1f), new Vector2(0.2f, 0.5f), 11f, true)   // torpedo: lento y con estela
                : Fly(path, new Color(1f, 0.7f, 0.2f), new Vector2(0.24f, 0.24f), 18f, false); // misil
        }

        private float Fly(Vector3[] path, Color color, Vector2 size, float cellsPerSecond, bool wake)
        {
            float total = 0f;
            for (int i = 1; i < path.Length; i++) total += Vector3.Distance(path[i - 1], path[i]);
            float duration = Mathf.Max(0.12f, total / (cellsPerSecond * Cs));

            var shot = Quad(color, 25, path[0], size * Cs);
            var trail = wake ? Quad(Alpha(color, 0.35f), 24, path[0], Vector2.one) : null;
            GameObject[] cleanup = wake ? new[] { shot.gameObject, trail.gameObject } : new[] { shot.gameObject };

            StartCoroutine(Animate(duration, t =>
            {
                float remaining = t * total;
                Vector3 position = path[path.Length - 1], direction = Vector3.up;
                for (int i = 1; i < path.Length; i++)
                {
                    float length = Vector3.Distance(path[i - 1], path[i]);
                    direction = (path[i] - path[i - 1]).normalized;
                    if (remaining <= length || i == path.Length - 1)
                    {
                        position = Vector3.Lerp(path[i - 1], path[i], length > 0f ? Mathf.Clamp01(remaining / length) : 1f);
                        break;
                    }
                    remaining -= length;
                }
                shot.transform.position = position;
                shot.transform.rotation = Quaternion.Euler(0f, 0f, Mathf.Atan2(direction.y, direction.x) * Mathf.Rad2Deg - 90f);
                if (trail != null) PlaceLine(trail.transform, path[0], position, 0.12f * Cs);
            }, cleanup));
            return duration;
        }

        private IEnumerator ShowImpacts(ResolutionDto r, Vector3[] cells, List<HitDto> hits, Vector3 origin)
        {
            // Fallos: lo que no impactó en nada.
            bool anyDirectHit = hits.Exists(h => h.Kind != (int)HitKind.Reflected);
            bool threeShot = r.CardId.ToString() == "attack_three_shot";
            for (int i = threeShot ? 0 : cells.Length - 1; i < cells.Length; i++)
            {
                int cx = r.TargetX + r.StepX * i, cy = r.TargetY + r.StepY * i;
                if (hits.Exists(h => h.Kind != (int)HitKind.Reflected && h.X == cx && h.Y == cy)) continue;

                bool wall = _map01.Cells[cy, cx] == 1;
                Burst(cells[i], wall ? WallColor : Color.white, wall ? 0.9f : 0.6f, wall ? 6 : 3, 0.45f);
                if (!anyDirectHit && i == cells.Length - 1) AddText(wall ? "Choca" : "Fallo", cells[i] + Vector3.up * (0.5f * Cs), WallColor);
            }

            // Impactos en el orden en que los aplicó el servidor (un espejo va seguido del daño devuelto).
            foreach (var hit in hits)
            {
                Vector3 position = Cell(hit.X, hit.Y);
                switch ((HitKind)hit.Kind)
                {
                    case HitKind.Hit:
                    case HitKind.Reflected:
                        ShowDamage(hit, position);
                        break;
                    case HitKind.Shielded:
                        Burst(position, ShieldColor, 1.3f, 8, 0.6f);
                        AddText("Escudo", position + Vector3.up * (0.5f * Cs), ShieldColor);
                        break;
                    case HitKind.Mirrored:
                        Burst(position, MirrorColor, 1.3f, 8, 0.6f);
                        AddText("¡Reflejado!", position + Vector3.up * (0.5f * Cs), MirrorColor);
                        yield return Wait(Fly(new[] { position, origin }, MirrorColor, Vector2.one * 0.22f, 18f, false));
                        break;
                }
            }
        }

        private void ShowDamage(HitDto hit, Vector3 position)
        {
            Burst(position, HitColor, 1.2f, 8, 0.5f);
            _shakes[hit.Slot] = Time.time + 0.4f;
            StartCoroutine(Flash(position));
            AddText("-" + hit.Damage, position + Vector3.up * (0.3f * Cs), HitColor, true);

            if (hit.Destroyed == 0) return;
            Burst(position, FireColor, 2.4f, 12, 0.9f);
            Burst(position, Color.yellow, 1.3f, 6, 0.6f);
            AddText("¡Hundido!", position + Vector3.up * (0.8f * Cs), FireColor, true);
            if (_pendingDeaths.TryGetValue(hit.Slot, out int n)) _pendingDeaths[hit.Slot] = Mathf.Max(0, n - 1);
        }

        // ---------------- Piezas de dibujo ----------------

        // Destello blanco sobre el barco golpeado.
        private IEnumerator Flash(Vector3 position)
        {
            var flash = Quad(Alpha(Color.white, 0.9f), 27, position, Vector2.one * (0.95f * Cs));
            yield return Animate(0.4f, t => flash.color = Alpha(Color.white, 0.9f * (1f - t)), flash.gameObject);
        }

        // Anillo que se expande y chispas que salen disparadas; todo se desvanece.
        private void Burst(Vector3 position, Color color, float size, int sparks, float duration)
        {
            var ring = Quad(Alpha(color, 0.6f), 26, position, Vector2.one * (0.2f * Cs));
            var parts = new SpriteRenderer[sparks];
            var directions = new Vector3[sparks];
            var cleanup = new GameObject[sparks + 1];
            cleanup[0] = ring.gameObject;
            for (int i = 0; i < sparks; i++)
            {
                float angle = i * 360f / sparks + 20f;
                directions[i] = new Vector3(Mathf.Cos(angle * Mathf.Deg2Rad), Mathf.Sin(angle * Mathf.Deg2Rad), 0f);
                parts[i] = Quad(color, 26, position, Vector2.one * (0.12f * Cs), angle);
                cleanup[i + 1] = parts[i].gameObject;
            }

            StartCoroutine(Animate(duration, t =>
            {
                float side = Mathf.Lerp(0.2f, size, t) * Cs;
                ring.transform.localScale = new Vector3(side, side, 1f);
                ring.color = Alpha(color, 0.6f * (1f - t));
                for (int i = 0; i < parts.Length; i++)
                {
                    parts[i].transform.position = position + directions[i] * (Cs * size * 0.6f * t);
                    parts[i].color = Alpha(color, 1f - t);
                }
            }, cleanup));
        }

        private Frame MakeFrame(Color color, int order)
        {
            const float thickness = 0.07f;
            var root = new GameObject("FxFrame");
            root.transform.SetParent(_root, false);

            // Cuatro lados de un cuadrado de lado 1 (el tamaño real lo da la escala del Root).
            Vector3[] positions = { new Vector3(0f, 0.5f), new Vector3(0f, -0.5f), new Vector3(-0.5f, 0f), new Vector3(0.5f, 0f) };
            Vector3[] sizes = { new Vector3(1f + thickness, thickness, 1f), new Vector3(1f + thickness, thickness, 1f),
                                new Vector3(thickness, 1f + thickness, 1f), new Vector3(thickness, 1f + thickness, 1f) };
            var frame = new Frame { Root = root, Sides = new SpriteRenderer[4] };
            for (int i = 0; i < 4; i++)
            {
                var side = Primitives2D.CreateSquare("Side", root.transform, color, order).GetComponent<SpriteRenderer>();
                side.transform.localPosition = positions[i];
                side.transform.localScale = sizes[i];
                frame.Sides[i] = side;
            }
            return frame;
        }

        private SpriteRenderer Quad(Color color, int order, Vector3 position, Vector2 size, float angle = 0f)
        {
            var renderer = Primitives2D.CreateSquare("Fx", _root, color, order).GetComponent<SpriteRenderer>();
            renderer.transform.position = position;
            renderer.transform.rotation = Quaternion.Euler(0f, 0f, angle);
            renderer.transform.localScale = new Vector3(size.x, size.y, 1f);
            return renderer;
        }

        private static void PlaceLine(Transform line, Vector3 a, Vector3 b, float thickness)
        {
            Vector3 d = b - a;
            line.position = (a + b) * 0.5f;
            line.rotation = Quaternion.Euler(0f, 0f, Mathf.Atan2(d.y, d.x) * Mathf.Rad2Deg);
            line.localScale = new Vector3(d.magnitude, thickness, 1f);
        }

        private static IEnumerator Animate(float duration, Action<float> step, params GameObject[] destroyAtEnd)
        {
            float start = Time.time;
            float t = 0f;
            while (t < 1f)
            {
                t = Mathf.Clamp01((Time.time - start) / duration);
                step(t);
                if (t < 1f) yield return null;
            }
            foreach (var go in destroyAtEnd) if (go != null) Destroy(go);
        }

        private static WaitForSeconds Wait(float seconds) => new WaitForSeconds(Mathf.Max(0f, seconds));

        private static Color Alpha(Color color, float alpha) { color.a = alpha; return color; }

        private Vector3 Cell(int x, int y)
        {
            int[,] cells = _map01.Cells;
            Vector3 center = _layout.CellToWorld(x, y, cells.GetLength(1), cells.GetLength(0));
            center.z = Z;
            return center;
        }

        // Posición actual del barco en pantalla (sigue su deslizamiento); si no se encuentra, la dada.
        private Vector3 ShipPos(int slot, Vector3 fallback)
        {
            var match = MatchController.Instance;
            if (match == null || match.Ships == null) return fallback;
            for (int i = 0; i < match.Ships.Count; i++)
                if (match.Ships[i].Slot == slot) return Cell(match.Ships[i].X, match.Ships[i].Y) + ShipOffset(slot);
            return fallback;
        }

        // ---------------- Textos flotantes (OnGUI) ----------------

        private void AddText(string text, Vector3 world, Color color, bool big = false)
        {
            _texts.Add(new FloatingText { Text = text, World = world, Color = color, Start = Time.time, Duration = big ? 1.6f : 1.2f, Big = big });
        }

        private void OnGUI()
        {
            if (Event.current.type != EventType.Repaint || _texts.Count == 0) return;
            Camera cam = Camera.main;
            if (cam == null) return;
            if (_style == null) _style = new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleCenter, fontStyle = FontStyle.Bold };

            foreach (var text in _texts)
            {
                float age = (Time.time - text.Start) / text.Duration;
                if (age < 0f || age > 1f) continue;

                Vector3 screen = cam.WorldToScreenPoint(text.World + Vector3.up * (0.6f * Cs * age));
                if (screen.z < 0f) continue;

                _style.fontSize = text.Big ? 26 : 18;
                var rect = new Rect(screen.x - 100f, Screen.height - screen.y - 15f, 200f, 30f);
                float alpha = 1f - age * age;
                _style.normal.textColor = new Color(0f, 0f, 0f, alpha);
                GUI.Label(new Rect(rect.x + 1.5f, rect.y + 1.5f, rect.width, rect.height), text.Text, _style); // sombra
                _style.normal.textColor = Alpha(text.Color, alpha);
                GUI.Label(rect, text.Text, _style);
            }
        }
    }
}
