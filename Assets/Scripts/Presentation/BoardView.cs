using System.Collections.Generic;
using NavalCommander.Networking;
using Unity.Netcode;
using UnityEngine;

namespace NavalCommander.Presentation
{
    /// <summary>
    /// Responsabilidad única: dibujar el estado REPLICADO del tablero (marco, obstáculos, barcos).
    /// Solo lee: nunca modifica el estado de la partida. Corre en todos los clientes (y en el host);
    /// en un servidor dedicado (modo batch) no dibuja nada.
    /// Cada frame toma MatchController.Ships (NetworkList replicada) y actualiza los barcos; el mapa es
    /// el mismo _map01 que usa el servidor. Crea TurnEffectsView para las animaciones del turno.
    /// </summary>
    [RequireComponent(typeof(BoardLayout))]
    public class BoardView : MonoBehaviour
    {
        [Header("Barcos")]
        [SerializeField] private GameObject shipViewPrefab;   // opcional: tu prefab 2D (cuerpo + triángulo hacia +Y)
        [SerializeField] private float shipSizeInCells = 1f;  // lado del cuerpo en celdas
        [SerializeField] private Vector2 manualOffsetCells = Vector2.zero;
        [SerializeField] private bool showCellMarker = true;  // cuadrado translúcido en la celda lógica
        [SerializeField] private Color myColor = Color.blue;
        [SerializeField] private Color[] enemyColors =
        {
            new Color(1f, 0.55f, 0f), new Color(0f, 0.6f, 0.1f), new Color(0.6f, 0f, 0.8f), new Color(0.4f, 0.4f, 0.4f)
        };
        // Los dibujos cuyo nombre contenga alguna de estas palabras no se pintan (conservan su color).
        [SerializeField] private string[] untintedNameParts = { "triang", "proa", "heading", "flecha", "arrow", "nose" };

        [Header("Tablero")]
        [SerializeField] private bool showBoardFrame = true;

        private BoardLayout _layout;
        private readonly Dictionary<int, ShipView> _ships = new Dictionary<int, ShipView>();
        private readonly Dictionary<int, GameObject> _markers = new Dictionary<int, GameObject>();
        private readonly List<GameObject> _staticObjects = new List<GameObject>(); // marco y obstáculos

        private TurnEffectsView _effects;

        private void Awake()
        {
            _layout = GetComponent<BoardLayout>();
            // Los efectos del turno se crean solos (la escena no necesita un componente extra).
            _effects = GetComponent<TurnEffectsView>();
            if (_effects == null && !Application.isBatchMode) _effects = gameObject.AddComponent<TurnEffectsView>();
        }

        /// <summary>Color del barco de ese slot (el propio se ve distinto).</summary>
        public Color ColorOf(int slot)
        {
            PlayerNet me = PlayerNet.Local;
            return me != null && me.Slot.Value == slot ? myColor : enemyColors[slot % enemyColors.Length];
        }

        private void Update()
        {
            if (Application.isBatchMode) return; // un servidor dedicado no dibuja

            var manager = NetworkManager.Singleton;
            if (manager == null || !manager.IsListening) { ClearAll(); return; }

            var match = MatchController.Instance;
            if (match == null || !match.IsSpawned || match.Ships == null || match.Ships.Count == 0) return;

            Render(match);
        }

        // ---------------- Dibujo ----------------

        private void Render(MatchController match)
        {
            int[,] cells = _map01.Cells;
            int height = cells.GetLength(0), width = cells.GetLength(1);
            EnsureStaticBoard(cells, width, height);

            for (int i = 0; i < match.Ships.Count; i++)
            {
                ShipSnapshot snapshot = match.Ships[i];
                // Un barco hundido sigue visible hasta que el proyectil lo alcanza; se desliza al moverse y vibra al ser golpeado.
                bool alive = snapshot.Health > 0 || (_effects != null && _effects.KeepsShipVisible(snapshot.Slot));
                Color color = ColorOf(snapshot.Slot);
                Vector3 cellCenter = _layout.CellToWorld(snapshot.X, snapshot.Y, width, height);
                if (_effects != null) cellCenter += _effects.ShipOffset(snapshot.Slot);

                ShipView ship = GetOrCreateShip(snapshot.Slot);
                ship.SetBodyColor(color);
                ship.Place(cellCenter + OffsetInWorld(), _layout.CellSize * shipSizeInCells, snapshot.Facing);
                ship.gameObject.SetActive(alive);

                if (showCellMarker) UpdateMarker(snapshot.Slot, cellCenter, color, alive);
            }
        }

        private Vector3 OffsetInWorld() =>
            new Vector3(manualOffsetCells.x * _layout.CellSize, manualOffsetCells.y * _layout.CellSize, 0f);

        private ShipView GetOrCreateShip(int slot)
        {
            if (_ships.TryGetValue(slot, out var existing) && existing != null) return existing;

            var go = new GameObject("NetShip_P" + slot); // nace en el origen, sin rotación ni escala
            var view = go.AddComponent<ShipView>();
            view.Build(shipViewPrefab, untintedNameParts);
            _ships[slot] = view;
            Debug.Log($"[Board] Creado {go.name}");
            return view;
        }

        private void UpdateMarker(int slot, Vector3 cellCenter, Color shipColor, bool alive)
        {
            if (!_markers.TryGetValue(slot, out var marker) || marker == null)
            {
                marker = Primitives2D.CreateSquare("NetCellMarker_P" + slot, null, Color.white, 6);
                _markers[slot] = marker;
            }

            shipColor.a = 0.35f;
            marker.GetComponent<SpriteRenderer>().color = shipColor;
            marker.transform.position = new Vector3(cellCenter.x, cellCenter.y, -0.15f);
            marker.transform.localScale = new Vector3(_layout.CellSize, _layout.CellSize, 1f);
            marker.SetActive(alive);
        }

        // ---------------- Marco y obstáculos (se arman una sola vez) ----------------

        private void EnsureStaticBoard(int[,] cells, int width, int height)
        {
            if (_staticObjects.Count > 0) return;

            if (showBoardFrame) BuildFrame(width, height);

            for (int y = 0; y < height; y++)
                for (int x = 0; x < width; x++)
                {
                    if (cells[y, x] != 1) continue; // 1 = pared

                    Vector3 center = _layout.CellToWorld(x, y, width, height);
                    var wall = Primitives2D.CreateSquare("NetObstacle", null, new Color(0.35f, 0.35f, 0.35f, 1f), 5);
                    wall.transform.position = new Vector3(center.x, center.y, -0.1f);
                    wall.transform.localScale = new Vector3(_layout.CellSize * 0.95f, _layout.CellSize * 0.95f, 1f);
                    _staticObjects.Add(wall);
                }
        }

        private void BuildFrame(int width, int height)
        {
            Rect board = _layout.GetBoardRect(width, height);
            float thickness = _layout.CellSize * 0.06f;

            AddFrameLine(board.center.x, board.yMin, board.width + thickness, thickness); // abajo
            AddFrameLine(board.center.x, board.yMax, board.width + thickness, thickness); // arriba
            AddFrameLine(board.xMin, board.center.y, thickness, board.height + thickness); // izquierda
            AddFrameLine(board.xMax, board.center.y, thickness, board.height + thickness); // derecha
        }

        private void AddFrameLine(float centerX, float centerY, float sizeX, float sizeY)
        {
            var line = Primitives2D.CreateSquare("NetBoardFrame", null, new Color(0f, 0f, 0f, 0.7f), 4);
            line.transform.position = new Vector3(centerX, centerY, -0.05f);
            line.transform.localScale = new Vector3(sizeX, sizeY, 1f);
            _staticObjects.Add(line);
        }

        private void ClearAll()
        {
            if (_ships.Count == 0 && _markers.Count == 0 && _staticObjects.Count == 0) return;

            foreach (var ship in _ships.Values) if (ship != null) Destroy(ship.gameObject);
            foreach (var marker in _markers.Values) if (marker != null) Destroy(marker);
            foreach (var item in _staticObjects) if (item != null) Destroy(item);
            _ships.Clear();
            _markers.Clear();
            _staticObjects.Clear();
        }
    }
}
