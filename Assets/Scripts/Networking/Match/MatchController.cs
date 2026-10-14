using System;
using System.Collections.Generic;
using NavalCommander.CardMechanics;
using NavalCommander.Gameplay;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace NavalCommander.Networking
{
    /// <summary>
    /// Capa de RED de la partida (objeto de la GameScene). Orquesta el flujo de turnos y la comunicación;
    /// las REGLAS del juego están en MatchRules (sin Netcode). Solo el servidor ejecuta lógica: los clientes
    /// leen el estado replicado (NetworkVariable / NetworkList) y reciben eventos por ClientRpc.
    /// </summary>
    public class MatchController : NetworkBehaviour
    {
        public static MatchController Instance { get; private set; }

        [Header("Tiempos")]
        [SerializeField] private float selectionSeconds = 30f; // tiempo para elegir cartas
        [SerializeField] private float resolutionSeconds = 3f; // pausa para ver el resultado

        [Header("Modo prueba")]
        [SerializeField] private bool movementOnlyTest = true; // solo cartas de movimiento
        [SerializeField] private int maxPlaysPerTurn = 2;

        // ---- Estado replicado: lo ven todos los clientes ----
        public NetworkVariable<MatchPhase> Phase = new NetworkVariable<MatchPhase>(MatchPhase.Lobby);
        public NetworkVariable<int> Turn = new NetworkVariable<int>(0);
        public NetworkVariable<double> TurnEndTime = new NetworkVariable<double>(0);
        public NetworkVariable<int> PlayerCount = new NetworkVariable<int>(0);
        public NetworkVariable<int> WinnerSlot = new NetworkVariable<int>(-1);
        public NetworkList<ShipSnapshot> Ships;

        // ---- Estado solo del servidor ----
        private readonly List<PlayerNet> _players = new List<PlayerNet>();
        private readonly Dictionary<int, List<CardPlayCommand>> _pendingPlays = new Dictionary<int, List<CardPlayCommand>>();
        private MatchRules _rules;
        private float _nextTurnAt;
        private bool _started;

        private void Awake()
        {
            Instance = this;
            Ships = new NetworkList<ShipSnapshot>();
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
            NetworkManager.SceneManager.OnLoadEventCompleted += HandleLoadEventCompleted;
        }

        public override void OnNetworkDespawn()
        {
            if (IsServer && NetworkManager != null && NetworkManager.SceneManager != null)
                NetworkManager.SceneManager.OnLoadEventCompleted -= HandleLoadEventCompleted;
        }

        private void HandleLoadEventCompleted(string sceneName, LoadSceneMode mode,
            List<ulong> clientsCompleted, List<ulong> clientsTimedOut)
        {
            if (sceneName != gameObject.scene.name || _started) return;
            StartMatch();
        }

        private void StartMatch()
        {
            _started = true;

            _players.Clear();
            foreach (var player in PlayerNet.All)
                if (player != null && player.Slot.Value >= 0) _players.Add(player);
            _players.Sort((a, b) => a.Slot.Value.CompareTo(b.Slot.Value));
            PlayerCount.Value = _players.Count;

            var slots = new List<int>();
            foreach (var player in _players) slots.Add(player.Slot.Value);

            var catalog = Resources.LoadAll<CardDefinition>("CardDefinitions");
            _rules = new MatchRules(catalog, movementOnlyTest, maxPlaysPerTurn);
            try
            {
                _rules.Start(_map01.Cells, slots, (ulong)DateTime.UtcNow.Ticks);
            }
            catch (Exception e)
            {
                Debug.LogError($"No se pudo iniciar la partida: {e.Message}");
                return;
            }

            PublishShips();
            BeginTurn();
        }

        public void OnPlayerLeft(PlayerNet player)
        {
            if (!_started || _rules == null) return;
            if (_players.Remove(player))
            {
                _rules.Forfeit(player.Slot.Value); // abandonar = perder
                if (IsSpawned) PlayerCount.Value = _players.Count;
            }
        }

        // ---------------- Flujo de turnos (servidor) ----------------

        private void Update()
        {
            if (!IsServer || !_started || _rules == null) return;

            switch (Phase.Value)
            {
                case MatchPhase.Selecting:
                    if (TryFinishMatch()) return;
                    bool timeIsUp = NetworkManager.ServerTime.Time >= TurnEndTime.Value;
                    if (timeIsUp || AllAlivePlayersConfirmed()) ResolveTurn();
                    break;

                case MatchPhase.Resolving:
                    if (Time.time >= _nextTurnAt && !TryFinishMatch()) BeginTurn();
                    break;
            }
        }

        private void BeginTurn()
        {
            Turn.Value++;
            _pendingPlays.Clear();

            foreach (var player in _players)
            {
                player.Confirmed.Value = false;
                if (!_rules.IsAlive(player.Slot.Value)) continue;

                string[] hand = _rules.DealHand(player.Slot.Value);
                var ids = new FixedString32Bytes[hand.Length];
                for (int i = 0; i < ids.Length; i++) ids[i] = new FixedString32Bytes(hand[i]);
                player.SendHand(ids); // mano privada: solo llega a su dueño
            }

            TurnEndTime.Value = NetworkManager.ServerTime.Time + selectionSeconds;
            Phase.Value = MatchPhase.Selecting;
        }

        /// <summary>Llamado desde PlayerNet (ServerRpc). El servidor no confía en el cliente: valida todo en MatchRules.</summary>
        public void ServerReceivePlays(PlayerNet sender, PlayDto[] plays)
        {
            if (Phase.Value != MatchPhase.Selecting || sender.Confirmed.Value) return;

            var inputs = new List<PlayInput>();
            if (plays != null)
                foreach (var play in plays) inputs.Add(new PlayInput(play.CardId.ToString(), play.Aim));

            _pendingPlays[sender.Slot.Value] = _rules.BuildCommands(sender.Slot.Value, inputs);
            sender.Confirmed.Value = true;
        }

        private bool AllAlivePlayersConfirmed()
        {
            foreach (var player in _players)
                if (_rules.IsAlive(player.Slot.Value) && !player.Confirmed.Value) return false;
            return true;
        }

        private void ResolveTurn()
        {
            Phase.Value = MatchPhase.Resolving;

            var allPlays = new List<CardPlayCommand>();
            foreach (var plays in _pendingPlays.Values) allPlays.AddRange(plays);

            var report = new List<ResolutionDto>();
            try
            {
                foreach (var info in _rules.ResolveTurn(allPlays))
                    report.Add(new ResolutionDto
                    {
                        Slot = info.Slot,
                        CardId = new FixedString32Bytes(info.CardId),
                        Phase = info.Phase,
                        Outcome = info.Outcome
                    });
            }
            catch (Exception e)
            {
                Debug.LogError($"Error resolviendo el turno: {e}");
            }

            PublishShips();
            NotifyTurnResolvedClientRpc(report.ToArray());
            _nextTurnAt = Time.time + resolutionSeconds;
        }

        private bool TryFinishMatch()
        {
            if (!_rules.TryGetWinner(out int winner)) return false;
            WinnerSlot.Value = winner;
            Phase.Value = MatchPhase.Finished;
            return true;
        }

        // ---------------- Publicación del estado ----------------

        private void PublishShips()
        {
            Ships.Clear();
            foreach (var ship in _rules.GetShipStatuses())
                Ships.Add(new ShipSnapshot { Slot = ship.Slot, X = ship.X, Y = ship.Y, Facing = ship.Facing, Health = ship.Health });
        }

        [ClientRpc]
        private void NotifyTurnResolvedClientRpc(ResolutionDto[] resolutions)
        {
            MatchEvents.RaiseTurnResolved(resolutions);
        }
    }
}
