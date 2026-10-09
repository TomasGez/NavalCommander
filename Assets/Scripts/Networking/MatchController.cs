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
    /// Objeto de la GameScene (NetworkObject colocado en la escena). Toda la lógica corre SOLO en el servidor
    /// (host o dedicado). Los clientes leen NetworkVariables/NetworkList y reciben eventos por ClientRpc.
    /// La partida arranca cuando todos los clientes terminaron de cargar la escena.
    /// </summary>
    public class MatchController : NetworkBehaviour
    {
        public static MatchController Instance { get; private set; }

        [Header("Reglas de la sesión")]
        [SerializeField] private float selectionSeconds = 30f; // tiempo para elegir
        [SerializeField] private float resolutionSeconds = 3f; // pausa para ver el resultado

        [Header("Modo prueba")]
        [SerializeField] private bool movementOnlyTest = true; // solo cartas de movimiento
        [SerializeField] private int maxPlaysPerTurn = 2;

        // Estado replicado (lo ven todos)
        public NetworkVariable<MatchPhase> Phase = new NetworkVariable<MatchPhase>(MatchPhase.Lobby);
        public NetworkVariable<int> Turn = new NetworkVariable<int>(0);
        public NetworkVariable<double> TurnEndTime = new NetworkVariable<double>(0);
        public NetworkVariable<int> PlayerCount = new NetworkVariable<int>(0);
        public NetworkVariable<int> WinnerSlot = new NetworkVariable<int>(-1);
        public NetworkList<ShipSnapshot> Ships;

        // Estado solo del servidor
        private readonly List<PlayerNet> _players = new List<PlayerNet>();
        private readonly Dictionary<int, PlayerCardDeck> _decks = new Dictionary<int, PlayerCardDeck>();
        private readonly Dictionary<int, List<CardPlayCommand>> _pending = new Dictionary<int, List<CardPlayCommand>>();
        private readonly HashSet<int> _forfeited = new HashSet<int>();
        private CardBoardState _board;
        private CardEffectResolver _resolver;
        private CardDefinition[] _catalog;
        private int[,] _cells;
        private float _nextTurnAt;
        private bool _started;

        private void Awake()
        {
            Instance = this;
            Ships = new NetworkList<ShipSnapshot>(); // estado de los barcos, replicado a todos
        }

        public override void OnDestroy()
        {
            if (Instance == this) Instance = null;
            base.OnDestroy();
        }

        // ---------------- Arranque ----------------

        public override void OnNetworkSpawn()
        {
            if (!IsServer) return;
            // Cuando TODOS los clientes terminaron de cargar esta escena, arranca la partida.
            NetworkManager.SceneManager.OnLoadEventCompleted += OnLoadEventCompleted;
        }

        public override void OnNetworkDespawn()
        {
            if (IsServer && NetworkManager != null && NetworkManager.SceneManager != null)
                NetworkManager.SceneManager.OnLoadEventCompleted -= OnLoadEventCompleted;
        }

        private void OnLoadEventCompleted(string sceneName, LoadSceneMode mode,
            List<ulong> clientsCompleted, List<ulong> clientsTimedOut)
        {
            if (sceneName != gameObject.scene.name || _started) return;
            StartMatch();
        }

        public void OnPlayerLeft(PlayerNet p)
        {
            if (!_started) return;
            if (_players.Remove(p))
            {
                _forfeited.Add(p.Slot.Value); // abandono = derrota
                if (IsSpawned) PlayerCount.Value = _players.Count;
            }
        }

        private void StartMatch()
        {
            _started = true;

            _players.Clear();
            foreach (var p in PlayerNet.All)
                if (p != null && p.Slot.Value >= 0) _players.Add(p);
            _players.Sort((a, b) => a.Slot.Value.CompareTo(b.Slot.Value));
            PlayerCount.Value = _players.Count;

            _catalog = Resources.LoadAll<CardDefinition>("CardDefinitions");
            _resolver = new CardEffectResolver(_catalog);

            _cells = _map01.Cells; // 1 = pared, 2..5 = spawn jugador 1..4
            int h = _cells.GetLength(0), w = _cells.GetLength(1);
            _board = new CardBoardState(w, h);
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                    if (_cells[y, x] == 1) _board.AddObstacle(x, y);

            ulong seed = (ulong)DateTime.UtcNow.Ticks;
            foreach (var p in _players)
            {
                int slot = p.Slot.Value;
                if (!TryFindSpawn(slot, out int sx, out int sy))
                {
                    Debug.LogError($"El mapa no tiene spawn para el slot {slot}");
                    continue;
                }
                _board.AddShip(ShipId(slot), sx, sy, FacingToCenter(sx, sy, w, h));
                _decks[slot] = new PlayerCardDeck(_catalog, seed, (ulong)slot);
            }

            PublishShips();
            NextTurn();
        }

        private bool TryFindSpawn(int slot, out int x, out int y)
        {
            for (y = 0; y < _cells.GetLength(0); y++)
                for (x = 0; x < _cells.GetLength(1); x++)
                    if (_cells[y, x] == 2 + slot) return true;
            x = 0; y = 0; return false;
        }

        // En tu resolver North = +Y y South = -Y.
        private static CardinalDirection FacingToCenter(int x, int y, int w, int h)
        {
            float dx = (w - 1) / 2f - x, dy = (h - 1) / 2f - y;
            if (Mathf.Abs(dx) >= Mathf.Abs(dy)) return dx >= 0 ? CardinalDirection.East : CardinalDirection.West;
            return dy >= 0 ? CardinalDirection.North : CardinalDirection.South;
        }

        // ---------------- Turnos ----------------

        private void NextTurn()
        {
            Turn.Value++;
            _pending.Clear();

            foreach (var p in _players)
            {
                p.Confirmed.Value = false;
                int slot = p.Slot.Value;
                if (!_decks.TryGetValue(slot, out var deck) || !IsAlive(slot)) continue;

                FixedString32Bytes[] ids;
                if (movementOnlyTest)
                {
                    ids = GetMovementCardIds(); // mano fija: todas las cartas de movimiento
                }
                else
                {
                    deck.BeginTurn();
                    ids = new FixedString32Bytes[deck.Hand.Count];
                    for (int i = 0; i < ids.Length; i++) ids[i] = new FixedString32Bytes(deck.Hand[i].CardId);
                }
                p.SendHand(ids);
            }

            TurnEndTime.Value = NetworkManager.ServerTime.Time + selectionSeconds;
            Phase.Value = MatchPhase.Selecting;
        }

        /// <summary>Llamado desde PlayerNet (ServerRpc). El servidor valida TODO.</summary>
        public void ServerReceivePlays(PlayerNet sender, PlayDto[] plays)
        {
            if (Phase.Value != MatchPhase.Selecting || sender.Confirmed.Value) return;
            int slot = sender.Slot.Value;
            if (!_decks.TryGetValue(slot, out var deck) || !IsAlive(slot)) return;

            var commands = new List<CardPlayCommand>();
            var seen = new HashSet<string>();
            foreach (var play in plays ?? Array.Empty<PlayDto>())
            {
                string id = play.CardId.ToString();
                if (!seen.Add(id)) continue;
                if (movementOnlyTest)
                {
                    if (!IsMovementCard(id) || commands.Count >= maxPlaysPerTurn) continue;
                }
                else if (!deck.TrySelectCard(id)) continue; // no está en la mano o excede el máximo por turno

                RelativeDirection? aim = (play.Aim >= 0 && play.Aim <= 3)
                    ? (RelativeDirection)play.Aim
                    : (RelativeDirection?)null;
                commands.Add(new CardPlayCommand(ShipId(slot), id, aim));
            }

            _pending[slot] = commands;
            sender.Confirmed.Value = true;
        }

        private void Update()
        {
            if (!IsServer || !_started) return;

            switch (Phase.Value)
            {
                case MatchPhase.Selecting:
                    if (TryFinish()) return;
                    bool timeUp = NetworkManager.ServerTime.Time >= TurnEndTime.Value;
                    bool allIn = true;
                    foreach (var p in _players)
                        if (IsAlive(p.Slot.Value) && !p.Confirmed.Value) allIn = false;
                    if (timeUp || allIn) ResolveTurn();
                    break;

                case MatchPhase.Resolving:
                    if (Time.time >= _nextTurnAt && !TryFinish()) NextTurn();
                    break;
            }
        }

        private void ResolveTurn()
        {
            Phase.Value = MatchPhase.Resolving;

            var all = new List<CardPlayCommand>();
            foreach (var kv in _pending) all.AddRange(kv.Value);

            var report = new List<ResolutionDto>();
            try
            {
                CardTurnResult result = _resolver.ResolveTurn(_board, all);
                foreach (var r in result.Resolutions)
                {
                    int slot = SlotOf(r.ShipId);
                    if (!movementOnlyTest && _decks.TryGetValue(slot, out var deck)) deck.ResolveSelectedCard(r.CardId, r.Outcome);
                    report.Add(new ResolutionDto
                    {
                        Slot = slot,
                        CardId = new FixedString32Bytes(r.CardId),
                        Phase = (int)r.Phase,
                        Outcome = (int)r.Outcome
                    });
                }
            }
            catch (Exception e)
            {
                Debug.LogError($"Error resolviendo el turno: {e}");
            }

            PublishShips();
            TurnResolvedClientRpc(BuildSnapshot(), report.ToArray());
            _nextTurnAt = Time.time + resolutionSeconds;
        }

        private bool TryFinish()
        {
            if (_board == null) return false;
            var alive = new List<int>();
            foreach (int slot in _decks.Keys)
                if (IsAlive(slot)) alive.Add(slot);

            if (alive.Count > 1) return false;
            WinnerSlot.Value = alive.Count == 1 ? alive[0] : -1;
            Phase.Value = MatchPhase.Finished;
            return true;
        }

        // ---------------- Helpers ----------------

        private bool IsMovementCard(string id)
        {
            foreach (var c in _catalog)
                if (c.CardId == id) return c.Category == CardCategory.Movement;
            return false;
        }

        private FixedString32Bytes[] GetMovementCardIds()
        {
            var names = new List<string>();
            foreach (var c in _catalog)
                if (c.Category == CardCategory.Movement) names.Add(c.CardId);
            names.Sort(StringComparer.Ordinal);

            var ids = new FixedString32Bytes[names.Count];
            for (int i = 0; i < ids.Length; i++) ids[i] = new FixedString32Bytes(names[i]);
            return ids;
        }

        private static string ShipId(int slot) => "P" + slot;
        private static int SlotOf(string shipId) => int.Parse(shipId.Substring(1));
        private bool IsAlive(int slot) => !_forfeited.Contains(slot) && _board.GetShip(ShipId(slot)).Health > 0;

        private ShipSnapshot[] BuildSnapshot()
        {
            var list = new List<ShipSnapshot>();
            foreach (int slot in _decks.Keys)
            {
                var s = _board.GetShip(ShipId(slot));
                list.Add(new ShipSnapshot { Slot = slot, X = s.X, Y = s.Y, Facing = (int)s.Facing, Health = s.Health });
            }
            return list.ToArray();
        }

        private void PublishShips()
        {
            Ships.Clear();
            foreach (var s in BuildSnapshot()) Ships.Add(s);
        }

        // ---------------- Servidor -> todos los clientes ----------------

        [ClientRpc]
        private void TurnResolvedClientRpc(ShipSnapshot[] ships, ResolutionDto[] resolutions)
        {
            MatchEvents.RaiseTurnResolved(ships, resolutions);
        }
    }
}
