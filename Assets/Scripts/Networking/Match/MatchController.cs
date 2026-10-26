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
    /// Capa de RED de la partida (NetworkObject colocado en la GameScene). Orquesta el flujo de turnos y la
    /// comunicación; las REGLAS del juego están en MatchRules (sin Netcode).
    ///
    /// Topología: servidor autoritativo. Existe en todas las instancias, pero solo el servidor ejecuta lógica:
    /// el host de una sesión Relay (ruta principal, ver SessionService) o un servidor dedicado (ruta de
    /// desarrollo, ver NetworkLauncher). Los clientes leen el estado replicado (NetworkVariable / NetworkList)
    /// y reciben el resultado de cada turno por RPC, que se reenvía a la UI mediante MatchEvents.
    ///
    /// Máquina de fases (Phase, replicada):
    ///   Lobby -> Selecting -> Resolving -> Selecting (turno siguiente) | Finished
    ///  - Selecting: se reparte la mano privada (PlayerNet.SendHand) y se esperan las jugadas
    ///    (PlayerNet.SubmitPlaysRpc -> ServerReceivePlays). Termina cuando todos confirman o vence TurnEndTime.
    ///  - Resolving: MatchRules resuelve el turno, se publica Ships y se avisa con NotifyTurnResolvedRpc;
    ///    pasados resolutionSeconds empieza el turno siguiente.
    ///  - Finished: queda a lo sumo un barco vivo; se publica WinnerSlot (-1 si no hay ganador). También es el
    ///    estado final si el servidor falla al iniciar o resolver (FailureMessage explica el motivo a los clientes).
    /// </summary>
    public class MatchController : NetworkBehaviour
    {
        public static MatchController Instance { get; private set; }

        [Header("Tiempos")]
        [SerializeField] private float selectionSeconds = 30f; // tiempo para elegir cartas
        [SerializeField] private float resolutionSeconds = 3f; // pausa para ver el resultado

        [Header("Modo prueba")]
        [SerializeField] private bool movementOnly = false; // true = todos reciben solo cartas de movimiento (sin mazo)
        [SerializeField] private int maxPlaysPerTurn = 2;

        // ---- Estado replicado: lo ven todos los clientes ----
        public NetworkVariable<MatchPhase> Phase = new NetworkVariable<MatchPhase>(MatchPhase.Lobby);
        public NetworkVariable<int> Turn = new NetworkVariable<int>(0);
        public NetworkVariable<double> TurnEndTime = new NetworkVariable<double>(0);
        public NetworkVariable<int> PlayerCount = new NetworkVariable<int>(0);
        public NetworkVariable<int> WinnerSlot = new NetworkVariable<int>(-1);
        public NetworkVariable<FixedString128Bytes> FailureMessage = new NetworkVariable<FixedString128Bytes>(); // vacío = sin error
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
            try
            {
                _rules = new MatchRules(catalog, movementOnly, maxPlaysPerTurn);
                _rules.Start(_map01.Cells, slots, (ulong)DateTime.UtcNow.Ticks);
            }
            catch (Exception e)
            {
                _rules = null;
                FailMatch($"No se pudo iniciar la partida: {e}", "No se pudo iniciar la partida (error del servidor).");
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
                _pendingPlays.Remove(player.Slot.Value); // sus jugadas sin resolver ya no cuentan
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

        /// <summary>Llamado desde PlayerNet (Rpc al servidor). El servidor no confía en el cliente: valida todo en MatchRules.</summary>
        public void ServerReceivePlays(PlayerNet sender, PlayDto[] plays)
        {
            if (sender == null || _rules == null || Phase.Value != MatchPhase.Selecting) return;

            int slot = sender.Slot.Value;
            if (slot < 0 || sender.Confirmed.Value) return;

            if (plays == null || plays.Length > maxPlaysPerTurn)
            {
                Debug.LogWarning($"[Servidor] Jugadas inválidas del slot {slot}: se ignoran.");
                return;
            }

            var inputs = new List<PlayInput>();
            foreach (var play in plays) inputs.Add(new PlayInput(play.CardId.ToString(), play.Aim));

            _pendingPlays[slot] = _rules.BuildCommands(slot, inputs);
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
            var hits = new List<HitDto>();
            try
            {
                foreach (var info in _rules.ResolveTurn(allPlays))
                    report.Add(ToDto(info, report.Count, hits));
            }
            catch (Exception e)
            {
                FailMatch($"Error resolviendo el turno: {e}", "Error del servidor al resolver el turno.");
                return;
            }

            PublishShips();
            NotifyTurnResolvedRpc(report.ToArray(), hits.ToArray());
            _nextTurnAt = Time.time + resolutionSeconds;
        }

        // Pasa una carta resuelta al formato de red; sus impactos se agregan a "hits" apuntando a su posición en el informe.
        private static ResolutionDto ToDto(ResolutionInfo info, int index, List<HitDto> hits)
        {
            var dto = new ResolutionDto
            {
                Slot = info.Slot,
                CardId = new FixedString32Bytes(info.CardId),
                Phase = info.Phase,
                Outcome = info.Outcome
            };

            ResolutionDetail d = info.Detail;
            if (d == null) return dto;

            dto.FromX = d.FromX; dto.FromY = d.FromY; dto.FromFacing = d.FromFacing;
            dto.ToX = d.ToX; dto.ToY = d.ToY; dto.ToFacing = d.ToFacing;
            dto.TargetX = d.TargetX; dto.TargetY = d.TargetY;
            dto.StepX = d.StepX; dto.StepY = d.StepY; dto.TargetCount = d.TargetCount;

            foreach (var hit in d.Hits)
                hits.Add(new HitDto
                {
                    Index = index, Slot = hit.Slot, X = hit.X, Y = hit.Y,
                    Kind = hit.Kind, Damage = hit.Damage, Destroyed = hit.Destroyed ? 1 : 0
                });
            return dto;
        }

        // Termina la partida sin ganador y avisa a los clientes (FailureMessage se replica junto con Phase).
        private void FailMatch(string logMessage, string playerMessage)
        {
            Debug.LogError(logMessage);
            FailureMessage.Value = new FixedString128Bytes(playerMessage);
            WinnerSlot.Value = -1;
            Phase.Value = MatchPhase.Finished;
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

        /// <summary>Servidor -> todos los jugadores (en un servidor dedicado no se ejecuta en el servidor).</summary>
        [Rpc(SendTo.ClientsAndHost, InvokePermission = RpcInvokePermission.Server)] // solo el servidor puede avisar
        private void NotifyTurnResolvedRpc(ResolutionDto[] resolutions, HitDto[] hits)
        {
            MatchEvents.RaiseTurnResolved(resolutions, hits);
        }
    }
}
