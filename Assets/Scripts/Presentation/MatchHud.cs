using System;
using System.Collections.Generic;
using NavalCommander.CardMechanics;
using NavalCommander.Networking;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace NavalCommander.Presentation
{
    /// <summary>
    /// Responsabilidad única: la interfaz del jugador durante la partida (OnGUI provisional):
    /// fase, temporizador, mano de cartas, confirmar y resultado. Lee el estado replicado y envía
    /// las jugadas a través de PlayerNet; no contiene reglas ni dibuja el tablero.
    /// </summary>
    public class MatchHud : MonoBehaviour
    {
        [SerializeField] private string lobbySceneName = "Lobby";

        private string[] _hand = Array.Empty<string>();
        private readonly List<string> _selectedCards = new List<string>();
        private int _aim;
        private string _lastTurnLog = "";

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

            GUILayout.BeginArea(new Rect(10, 10, 340, Screen.height - 20));
            if (!manager.IsListening)
            {
                GUILayout.Label("Sin conexión. La conexión se hace en la escena Lobby.");
                if (GUILayout.Button("Ir al Lobby")) SceneManager.LoadScene(lobbySceneName);
            }
            else DrawMatch(manager);
            GUILayout.EndArea();
        }

        private void DrawMatch(NetworkManager manager)
        {
            var match = MatchController.Instance;
            if (match == null || !match.IsSpawned) { GUILayout.Label("Cargando partida..."); return; }

            PlayerNet me = PlayerNet.Local;
            string role = manager.IsHost ? "Host" : manager.IsServer ? "Servidor" : "Cliente";
            GUILayout.Label($"{role} | tu slot: {(me != null ? me.Slot.Value : -1)}");
            GUILayout.Label($"Fase: {match.Phase.Value} | Turno: {match.Turn.Value} | Jugadores: {match.PlayerCount.Value}");

            if (match.Phase.Value == MatchPhase.Lobby) GUILayout.Label("Preparando la partida...");
            if (match.Phase.Value == MatchPhase.Selecting) DrawSelection(manager, match, me);
            if (match.Phase.Value == MatchPhase.Finished)
                GUILayout.Label(match.WinnerSlot.Value >= 0 ? $"¡Ganó el slot {match.WinnerSlot.Value}!" : "Empate / sin ganador");

            for (int i = 0; i < match.Ships.Count; i++)
            {
                ShipSnapshot s = match.Ships[i];
                GUILayout.Label($"P{s.Slot}: celda ({s.X},{s.Y}) mira {(CardinalDirection)s.Facing} vida {s.Health}");
            }

            if (!string.IsNullOrEmpty(_lastTurnLog)) GUILayout.Label("Último turno:\n" + _lastTurnLog);

            if (GUILayout.Button("Desconectar y volver al Lobby"))
            {
                manager.Shutdown();
                SceneManager.LoadScene(lobbySceneName);
            }
        }

        private void DrawSelection(NetworkManager manager, MatchController match, PlayerNet me)
        {
            double secondsLeft = Math.Max(0, match.TurnEndTime.Value - manager.ServerTime.Time);
            GUILayout.Label($"Tiempo: {secondsLeft:0}s");

            if (me == null) return; // servidor dedicado: no juega
            if (me.Confirmed.Value) { GUILayout.Label("Confirmado. Esperando a los demás..."); return; }

            GUILayout.Label("Tu mano (la selección se marca al instante, el servidor la valida):");
            foreach (string cardId in _hand)
            {
                bool wasSelected = _selectedCards.Contains(cardId);
                bool isSelected = GUILayout.Toggle(wasSelected, cardId, "Button");
                if (isSelected && !wasSelected) _selectedCards.Add(cardId);
                else if (!isSelected && wasSelected) _selectedCards.Remove(cardId);
            }

            GUILayout.Label("Dirección (cartas de ataque):");
            _aim = GUILayout.Toolbar(_aim, new[] { "Fwd", "Right", "Back", "Left" });

            if (GUILayout.Button("CONFIRMAR")) SubmitSelection(me);
        }

        private void SubmitSelection(PlayerNet me)
        {
            var plays = new PlayDto[_selectedCards.Count];
            for (int i = 0; i < plays.Length; i++)
                plays[i] = new PlayDto { CardId = new FixedString32Bytes(_selectedCards[i]), Aim = _aim };
            me.SubmitPlaysServerRpc(plays);
        }
    }
}
