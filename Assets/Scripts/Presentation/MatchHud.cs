using System;
using System.Collections.Generic;
using NavalCommander.CardMechanics;
using NavalCommander.Networking;
using NavalCommander.Presentation.Cards;
using NavalCommander.Progression;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace NavalCommander.Presentation
{
    /// <summary>
    /// Responsabilidad única: la interfaz del jugador durante la partida (OnGUI provisional):
    /// fase, temporizador, mano de cartas, confirmar y resultado. Lee el estado replicado de MatchController
    /// y envía las jugadas a través de PlayerNet.SubmitPlaysRpc; no contiene reglas ni dibuja el tablero
    /// (eso es BoardView). Corre en los clientes y en el host; recibe la mano y el informe del turno
    /// mediante MatchEvents. Al salir cierra la sesión (SessionService) y vuelve a la LobbyScene.
    /// </summary>
    public class MatchHud : MonoBehaviour
    {
        private static readonly string[] AimLabels = { "Fwd", "Right", "Back", "Left" };

        [SerializeField] private string lobbySceneName = "LobbyScene";

        private string[] _hand = Array.Empty<string>();
        private readonly List<string> _selectedCards = new List<string>();
        private int _aim;
        private string _lastTurnLog = "";
        private CardArtCatalog _cardArt;
        private readonly MatchResultMemo _result = new MatchResultMemo();
        private MatchHudStyles _styles;

        private void Awake()
        {
            _cardArt = Resources.Load<CardArtCatalog>(CardArtCatalog.ResourcePath);
        }

        private void OnEnable()
        {
            MatchEvents.HandReceived += HandleHandReceived;
            MatchEvents.TurnResolved += HandleTurnResolved;
        }

        private void OnDisable()
        {
            MatchEvents.HandReceived -= HandleHandReceived;
            MatchEvents.TurnResolved -= HandleTurnResolved;
        }

        // Se anota en cada cuadro: cuando se cierra la conexión, el jugador local ya no está disponible.
        private void Update()
        {
            PlayerNet me = PlayerNet.Local;
            if (me != null) _result.RememberLocalSlot(me.Slot.Value);

            MatchController match = MatchController.Instance;
            if (match != null && match.Phase.Value == MatchPhase.Finished)
                _result.RememberFinish(match.FailureMessage.Value.ToString(), match.WinnerSlot.Value);
        }

        private void HandleHandReceived(string[] cardIds)
        {
            _hand = cardIds;
            _selectedCards.Clear();
        }

        private void HandleTurnResolved(ResolutionDto[] resolutions)
        {
            var sb = new System.Text.StringBuilder();
            foreach (var r in resolutions) sb.AppendLine($"P{r.Slot}: {r.CardId} -> {(CardUseOutcome)r.Outcome}");
            _lastTurnLog = sb.ToString();
        }

        // ---------------- UI ----------------

        private void OnGUI()
        {
            var manager = NetworkManager.Singleton;
            if (manager == null) return;
            if (_styles == null) _styles = new MatchHudStyles();

            // En ventanas chicas se dibuja en un espacio de 1280x720 y GUI.matrix lo reduce. IMGUI convierte
            // también la posición del mouse, así que botones y cartas reciben los clics en ese mismo espacio.
            float scale = HudScale.For(Screen.width, Screen.height);
            float width = Screen.width / scale;
            float height = Screen.height / scale;
            Matrix4x4 previousMatrix = GUI.matrix;
            GUI.matrix = Matrix4x4.Scale(new Vector3(scale, scale, 1f));

            // El grupo vertical dibuja el panel y toma la altura de su contenido.
            GUILayout.BeginArea(HudPanelLayout.Area(height));
            GUILayout.BeginVertical(_styles.Panel, GUILayout.ExpandHeight(false));
            if (!manager.IsListening) DrawDisconnected();
            else DrawMatch(manager);
            GUILayout.EndVertical();
            GUILayout.EndArea();

            // Fuera del área izquierda: la mano va abajo a lo ancho y el área está recortada a su columna.
            if (manager.IsListening) DrawHand(width, height);

            GUI.matrix = previousMatrix;
        }

        private void DrawDisconnected()
        {
            // Si la partida ya había terminado, el resultado sigue valiendo aunque el host se haya ido.
            if (_result.HasResult) DrawResult();
            else GUILayout.Label("Sin conexión. La conexión se hace en el menú inicial.", _styles.Label);

            if (GUILayout.Button("Ir al menú", _styles.Button)) GoToLobby();
        }

        private void DrawResult()
        {
            GUILayout.Label(_result.ResultText, _styles.Label);

            string xpLine = _result.XpLine(PlayerProgression.LastXpGain, PlayerProgression.Level, PlayerProgression.Xp, PlayerProgression.XpForNextLevel);
            if (xpLine != null) GUILayout.Label(xpLine, _styles.Label);
        }

        private void DrawMatch(NetworkManager manager)
        {
            var match = MatchController.Instance;
            if (match == null || !match.IsSpawned) { GUILayout.Label("Cargando partida...", _styles.Label); return; }

            PlayerNet me = PlayerNet.Local;
            string role = manager.IsHost ? "Host" : manager.IsServer ? "Servidor" : "Cliente";
            GUILayout.Label($"{role} | tu slot: {(me != null ? me.Slot.Value : -1)}", _styles.Label);
            GUILayout.Label($"Fase: {match.Phase.Value} | Turno: {match.Turn.Value} | Jugadores: {match.PlayerCount.Value}", _styles.Label);

            if (match.Phase.Value == MatchPhase.Lobby) GUILayout.Label("Preparando la partida...", _styles.Label);
            if (match.Phase.Value == MatchPhase.Selecting) DrawSelection(manager, match, me);
            if (match.Phase.Value == MatchPhase.Finished && _result.HasResult) DrawResult();

            for (int i = 0; i < match.Ships.Count; i++)
            {
                ShipSnapshot s = match.Ships[i];
                GUILayout.Label($"P{s.Slot}: celda ({s.X},{s.Y}) mira {(CardinalDirection)s.Facing} vida {s.Health}", _styles.Label);
            }

            if (!string.IsNullOrEmpty(_lastTurnLog)) GUILayout.Label("Último turno:\n" + _lastTurnLog, _styles.Label);

            if (GUILayout.Button("Salir y volver al menú", _styles.Button)) LeaveToLobby(manager);
        }

        // Sin conexión (por ejemplo, el host cerró la partida): se cierra la sesión que pudo quedar colgada.
        private async void GoToLobby()
        {
            var sessions = SessionService.ExistingInstance;
            if (sessions != null && sessions.Current != null) await sessions.LeaveAsync();
            SceneManager.LoadScene(lobbySceneName);
        }

        private async void LeaveToLobby(NetworkManager manager)
        {
            var sessions = SessionService.ExistingInstance;
            if (sessions != null && sessions.Current != null) await sessions.LeaveAsync(); // cierra también Relay y Lobby
            else manager.Shutdown();
            SceneManager.LoadScene(lobbySceneName);
        }

        private void DrawSelection(NetworkManager manager, MatchController match, PlayerNet me)
        {
            double secondsLeft = Math.Max(0, match.TurnEndTime.Value - manager.ServerTime.Time);
            GUILayout.Label($"Tiempo: {secondsLeft:0}s", _styles.Timer);

            if (me == null) return; // servidor dedicado: no juega
            if (me.Confirmed.Value) { GUILayout.Label("Confirmado. Esperando a los demás...", _styles.Label); return; }

            GUILayout.Label("Tu mano (la selección se marca al instante, el servidor la valida):", _styles.Label);

            GUILayout.Label("Dirección (cartas de ataque):", _styles.Label);
            _aim = GUILayout.Toolbar(_aim, AimLabels, _styles.Aim);

            if (GUILayout.Button("CONFIRMAR", _styles.Confirm)) SubmitSelection(me);
        }

        // Misma condición que la mano antes: solo en Selecting, sin confirmar y con jugador local.
        private void DrawHand(float width, float height)
        {
            MatchController match = MatchController.Instance;
            PlayerNet me = PlayerNet.Local;
            if (match == null || !match.IsSpawned || me == null) return;
            if (match.Phase.Value != MatchPhase.Selecting || me.Confirmed.Value) return;

            // El espacio del HUD mide al menos 1280x720, así que la mano siempre entra a la derecha de la columna.
            float areaHeight = Mathf.Min(height * 0.28f, 260f);
            const float left = 360f;
            var area = new Rect(left, height - areaHeight - 10f, width - left - 10f, areaHeight);

            string clicked = CardHandGui.Draw(area, _hand, _selectedCards, _cardArt);
            if (clicked == null) return;
            if (_selectedCards.Contains(clicked)) _selectedCards.Remove(clicked);
            else _selectedCards.Add(clicked);
        }

        private void SubmitSelection(PlayerNet me)
        {
            var plays = new PlayDto[_selectedCards.Count];
            for (int i = 0; i < plays.Length; i++)
                plays[i] = new PlayDto { CardId = new FixedString32Bytes(_selectedCards[i]), Aim = _aim };
            me.SubmitPlaysRpc(plays);
        }
    }
}
