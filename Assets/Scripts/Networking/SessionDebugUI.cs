using System;
using System.Collections.Generic;
using NavalCommander.CardMechanics;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace NavalCommander.Networking
{
    /// <summary>
    /// GameScene: dibuja la partida en 2D (cámara ortográfica sobre el plano XY) y da la UI para jugar cartas.
    /// El tablero de la red (celdas de _map01) se estira sobre el sprite "Sea Tiles_0".
    /// La conexión ya NO se hace acá: se hace en la escena Lobby.
    /// </summary>
    public class SessionDebugUI : MonoBehaviour
    {
        [SerializeField] private string lobbySceneName = "Lobby";

        [Header("Visual del barco")]
        [SerializeField] private GameObject shipViewPrefab;      // opcional: tu prefab 2D (cuerpo + triángulo hacia +Y)
        [SerializeField] private float shipSizeInCells = 1f;     // lado del CUERPO en celdas (1 = tamaño real de la lógica)
        [SerializeField] private Vector2 manualOffsetCells = Vector2.zero; // ajuste fino, en celdas
        [SerializeField] private bool showCellMarker = true;     // cuadrado translúcido en la celda lógica (para calibrar)
        [SerializeField] private Color myColor = Color.blue;     // tu barco
        [SerializeField] private Color[] enemyColors =
        {
            new Color(1f, 0.55f, 0f), new Color(0f, 0.6f, 0.1f), new Color(0.6f, 0f, 0.8f), new Color(0.4f, 0.4f, 0.4f)
        };
        // Los SpriteRenderer cuyo nombre contenga alguna de estas palabras NO se pintan (conservan su color).
        [SerializeField] private string[] untintedNameParts = { "triang", "proa", "heading", "flecha", "arrow", "nose" };

        private string[] _hand = Array.Empty<string>();
        private readonly List<string> _selected = new List<string>();
        private readonly Dictionary<int, GameObject> _ships = new Dictionary<int, GameObject>();
        private readonly Dictionary<int, GameObject> _markers = new Dictionary<int, GameObject>();
        private readonly Dictionary<int, List<SpriteRenderer>> _bodies = new Dictionary<int, List<SpriteRenderer>>();
        private readonly Dictionary<int, float> _shipUnit = new Dictionary<int, float>();
        private readonly List<GameObject> _obstacles = new List<GameObject>();
        private int _aim;
        private string _lastLog = "";
        private ShipSnapshot[] _lastShips = Array.Empty<ShipSnapshot>();
        private Sprite _squareSprite;
        private SpriteRenderer _sea;

        private void OnEnable()
        {
            MatchEvents.HandReceived += OnHand;
            MatchEvents.TurnResolved += OnResolved;
        }

        private void OnDisable()
        {
            MatchEvents.HandReceived -= OnHand;
            MatchEvents.TurnResolved -= OnResolved;
        }

        private void OnHand(string[] ids) { _hand = ids; _selected.Clear(); }

        private void OnResolved(ShipSnapshot[] ships, ResolutionDto[] res)
        {
            var sb = new System.Text.StringBuilder();
            foreach (var r in res) sb.AppendLine($"P{r.Slot}: {r.CardId} -> {(CardUseOutcome)r.Outcome}");
            _lastLog = sb.ToString();
        }

        // ---------------- Dibujo 2D ----------------

        private void Update()
        {
            if (Application.isBatchMode) return; // servidor dedicado: no dibuja nada

            var nm = NetworkManager.Singleton;
            if (nm == null || !nm.IsListening) { ClearAll(); return; }

            var mc = MatchController.Instance;
            if (mc == null || !mc.IsSpawned || mc.Ships == null) return;

            int n = mc.Ships.Count;
            if (_lastShips.Length != n) _lastShips = new ShipSnapshot[n];
            for (int i = 0; i < n; i++) _lastShips[i] = mc.Ships[i];
            if (n > 0) ApplySnapshot(_lastShips);
        }

        private void ClearAll()
        {
            foreach (var kv in _ships) if (kv.Value != null) Destroy(kv.Value);
            foreach (var kv in _markers) if (kv.Value != null) Destroy(kv.Value);
            foreach (var o in _obstacles) if (o != null) Destroy(o);
            _ships.Clear();
            _markers.Clear();
            _bodies.Clear();
            _shipUnit.Clear();
            _obstacles.Clear();
            _lastShips = Array.Empty<ShipSnapshot>();
        }

        private Sprite Square()
        {
            if (_squareSprite != null) return _squareSprite;
            var tex = new Texture2D(4, 4);
            var px = new Color[16];
            for (int i = 0; i < px.Length; i++) px[i] = Color.white;
            tex.SetPixels(px);
            tex.Apply();
            tex.filterMode = FilterMode.Point;
            _squareSprite = Sprite.Create(tex, new Rect(0, 0, 4, 4), new Vector2(0.5f, 0.5f), 4f); // mide 1 unidad
            return _squareSprite;
        }

        private GameObject MakeSquare(string name, Transform parent, Color color, int order)
        {
            var go = new GameObject(name);
            if (parent != null) go.transform.SetParent(parent, false);
            var sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = Square();
            sr.color = color;
            sr.sortingOrder = order;
            return go;
        }

        /// <summary>Centro de una celda en coordenadas de mundo, estirando el tablero sobre el sprite del mar.</summary>
        private Vector3 CellToWorld(int x, int y, int w, int h, out float cellW, out float cellH)
        {
            if (_sea == null)
            {
                var seaGo = GameObject.Find("Sea Tiles_0");
                if (seaGo != null) _sea = seaGo.GetComponent<SpriteRenderer>();
            }

            Bounds b = _sea != null ? _sea.bounds : new Bounds(Vector3.zero, new Vector3(w, h, 0f));
            cellW = b.size.x / w;
            cellH = b.size.y / h;
            return new Vector3(b.min.x + (x + 0.5f) * cellW, b.min.y + (y + 0.5f) * cellH, -0.2f);
        }

        private void BuildObstacles(int[,] cells, int w, int h)
        {
            if (_obstacles.Count > 0) return;
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                {
                    if (cells[y, x] != 1) continue;
                    Vector3 p = CellToWorld(x, y, w, h, out float cw, out float ch);
                    var o = MakeSquare("NetObstacle", null, new Color(0.35f, 0.35f, 0.35f, 1f), 5);
                    o.transform.position = new Vector3(p.x, p.y, -0.1f);
                    o.transform.localScale = new Vector3(cw * 0.95f, ch * 0.95f, 1f);
                    _obstacles.Add(o);
                }
        }

        private bool IsUntinted(SpriteRenderer sr)
        {
            string n = sr.gameObject.name.ToLowerInvariant();
            foreach (var part in untintedNameParts)
                if (!string.IsNullOrEmpty(part) && n.Contains(part)) return true;
            return false;
        }

        private static void Grow(ref Bounds b, ref bool has, Bounds add)
        {
            if (!has) { b = add; has = true; } else b.Encapsulate(add);
        }

        private GameObject CreateShip(int slot)
        {
            var holder = new GameObject("NetShip_P" + slot); // nace en el origen, sin rotación ni escala
            var bodies = new List<SpriteRenderer>();
            float unit = 1f;

            if (shipViewPrefab != null)
            {
                var view = Instantiate(shipViewPrefab, holder.transform);
                Bounds bodyB = default, allB = default;
                bool hasBody = false, hasAll = false;

                foreach (var sr in view.GetComponentsInChildren<SpriteRenderer>())
                {
                    bool untinted = IsUntinted(sr);
                    sr.sortingOrder = untinted ? 11 : 10; // el triángulo siempre encima del cuerpo
                    Grow(ref allB, ref hasAll, sr.bounds);
                    if (!untinted) { bodies.Add(sr); Grow(ref bodyB, ref hasBody, sr.bounds); }
                }

                // Se centra y se mide el CUERPO (no el triángulo), así el cuadrado queda justo en el medio de la celda.
                Bounds use = hasBody ? bodyB : allB;
                if (hasBody || hasAll)
                {
                    view.transform.position -= use.center;
                    unit = Mathf.Max(use.size.x, use.size.y);
                }
            }
            else
            {
                var body = MakeSquare("Cuerpo", holder.transform, Color.white, 10);
                body.transform.localScale = new Vector3(0.8f, 0.8f, 1f);
                bodies.Add(body.GetComponent<SpriteRenderer>());
                var nose = MakeSquare("Proa", holder.transform, Color.red, 11);
                nose.transform.localPosition = new Vector3(0f, 0.4f, 0f);
                nose.transform.localScale = new Vector3(0.3f, 0.3f, 1f);
                unit = 0.8f;
            }

            _bodies[slot] = bodies;
            _shipUnit[slot] = Mathf.Max(0.0001f, unit);
            Debug.Log($"[Net] Creado {holder.name}");
            return holder;
        }

        private void ApplySnapshot(ShipSnapshot[] ships)
        {
            int[,] cells = _map01.Cells;
            int h = cells.GetLength(0), w = cells.GetLength(1);
            BuildObstacles(cells, w, h);

            var me = PlayerNet.Local;
            foreach (var s in ships)
            {
                Vector3 cellCenter = CellToWorld(s.X, s.Y, w, h, out float cw, out float ch);
                float cell = Mathf.Min(cw, ch);

                if (!_ships.TryGetValue(s.Slot, out var go) || go == null)
                {
                    go = CreateShip(s.Slot);
                    _ships[s.Slot] = go;
                }

                // Color según quién sos: tu barco siempre igual, los rivales por slot. El triángulo no se toca.
                bool mine = me != null && me.Slot.Value == s.Slot;
                Color color = mine ? myColor : enemyColors[s.Slot % enemyColors.Length];
                foreach (var sr in _bodies[s.Slot]) if (sr != null) sr.color = color;

                // Marcador de la celda lógica (translúcido): sirve para ver si el barco está centrado.
                if (showCellMarker)
                {
                    if (!_markers.TryGetValue(s.Slot, out var mk) || mk == null)
                    {
                        mk = MakeSquare("NetCellMarker_P" + s.Slot, null, Color.white, 6);
                        _markers[s.Slot] = mk;
                    }
                    var c = color; c.a = 0.35f;
                    mk.GetComponent<SpriteRenderer>().color = c;
                    mk.transform.position = new Vector3(cellCenter.x, cellCenter.y, -0.15f);
                    mk.transform.localScale = new Vector3(cw, ch, 1f);
                    mk.SetActive(s.Health > 0);
                }

                Vector3 pos = cellCenter + new Vector3(manualOffsetCells.x * cw, manualOffsetCells.y * ch, 0f);
                go.transform.localScale = Vector3.one * (cell * shipSizeInCells / _shipUnit[s.Slot]);
                go.transform.position = pos;
                // Norte = +Y (arriba). Este = derecha. Gira en el eje Z (vista 2D).
                go.transform.rotation = Quaternion.Euler(0f, 0f, -90f * (s.Facing % 4));
                go.SetActive(s.Health > 0);
            }
        }

        // ---------------- UI ----------------

        private void OnGUI()
        {
            var nm = NetworkManager.Singleton;
            if (nm == null) return;

            GUILayout.BeginArea(new Rect(10, 10, 340, Screen.height - 20));
            if (!nm.IsListening)
            {
                GUILayout.Label("Sin conexión. La conexión se hace en la escena Lobby.");
                if (GUILayout.Button("Ir al Lobby")) SceneManager.LoadScene(lobbySceneName);
            }
            else DrawMatch(nm);
            GUILayout.EndArea();
        }

        private void DrawMatch(NetworkManager nm)
        {
            var mc = MatchController.Instance;
            if (mc == null || !mc.IsSpawned) { GUILayout.Label("Cargando partida..."); return; }

            var me = PlayerNet.Local;
            string role = nm.IsHost ? "Host" : nm.IsServer ? "Servidor" : "Cliente";
            GUILayout.Label($"{role} | tu slot: {(me != null ? me.Slot.Value : -1)}");
            GUILayout.Label($"Fase: {mc.Phase.Value} | Turno: {mc.Turn.Value} | Jugadores: {mc.PlayerCount.Value}");
            GUILayout.Label($"Barcos recibidos: {mc.Ships.Count} | Objetos NetShip: {_ships.Count}");

            if (mc.Phase.Value == MatchPhase.Lobby)
                GUILayout.Label("Preparando la partida...");

            if (mc.Phase.Value == MatchPhase.Selecting)
            {
                double left = Math.Max(0, mc.TurnEndTime.Value - nm.ServerTime.Time);
                GUILayout.Label($"Tiempo: {left:0}s");

                if (me != null && !me.Confirmed.Value)
                {
                    GUILayout.Label("Tu mano (feedback local instantáneo):");
                    foreach (string id in _hand)
                    {
                        bool on = _selected.Contains(id);
                        bool now = GUILayout.Toggle(on, id, "Button");
                        if (now && !on) _selected.Add(id);
                        else if (!now && on) _selected.Remove(id);
                    }
                    GUILayout.Label("Dirección (cartas de ataque):");
                    _aim = GUILayout.Toolbar(_aim, new[] { "Fwd", "Right", "Back", "Left" });

                    if (GUILayout.Button("CONFIRMAR"))
                    {
                        var plays = new PlayDto[_selected.Count];
                        for (int i = 0; i < plays.Length; i++)
                            plays[i] = new PlayDto { CardId = new FixedString32Bytes(_selected[i]), Aim = _aim };
                        me.SubmitPlaysServerRpc(plays);
                    }
                }
                else if (me != null) GUILayout.Label("Confirmado. Esperando a los demás...");
            }

            if (mc.Phase.Value == MatchPhase.Finished)
                GUILayout.Label(mc.WinnerSlot.Value >= 0 ? $"¡Ganó el slot {mc.WinnerSlot.Value}!" : "Empate / sin ganador");

            foreach (var s in _lastShips)
                GUILayout.Label($"P{s.Slot}: celda ({s.X},{s.Y}) mira {(CardinalDirection)s.Facing} vida {s.Health}");

            if (!string.IsNullOrEmpty(_lastLog)) GUILayout.Label("Último turno:\n" + _lastLog);

            if (GUILayout.Button("Desconectar y volver al Lobby"))
            {
                nm.Shutdown();
                SceneManager.LoadScene(lobbySceneName);
            }
        }
    }
}
