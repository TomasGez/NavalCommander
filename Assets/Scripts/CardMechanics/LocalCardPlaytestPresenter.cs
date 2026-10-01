using System;
using System.Collections.Generic;
using UnityEngine;

namespace NavalCommander.CardMechanics.Playtest
{
    public sealed class LocalCardPlaytestPresenter : MonoBehaviour
    {
        private const int BoardSize = 8;
        private const string PlayerId = "player";
        private const string TargetId = "target";
        private const string TargetCardId = "attack_torpedo";
        private const ulong DemoSeed = 20260930UL;

        [Header("Starting health")]
        [SerializeField, Min(1)] private int playerInitialHealth = 100;
        [SerializeField, Min(1)] private int targetInitialHealth = 100;

        [Header("Scene markers")]
        [SerializeField] private SpriteRenderer playerMarker;
        [SerializeField] private SpriteRenderer targetMarker;

        [Header("Card controls")]
        [SerializeField] private UnityEngine.UI.Button[] cardSlotButtons = new UnityEngine.UI.Button[3];
        [SerializeField] private UnityEngine.UI.Text[] cardSlotLabels = new UnityEngine.UI.Text[3];
        [SerializeField] private UnityEngine.UI.Button[] aimButtons = new UnityEngine.UI.Button[4];
        [SerializeField] private UnityEngine.UI.Text[] aimLabels = new UnityEngine.UI.Text[4];
        [SerializeField] private UnityEngine.UI.Toggle targetFireToggle;
        [SerializeField] private UnityEngine.UI.Button resolveTurnButton;
        [SerializeField] private UnityEngine.UI.Button resetMatchButton;

        [Header("Readouts")]
        [SerializeField] private UnityEngine.UI.Text statusLabel;
        [SerializeField] private UnityEngine.UI.Text playerHealthLabel;
        [SerializeField] private UnityEngine.UI.Text targetHealthLabel;

        private readonly List<CardDefinition> _selectedCards = new List<CardDefinition>(2);
        private CardDefinition[] _catalog;
        private PlayerCardDeck _deck;
        private CardEffectResolver _resolver;
        private CardBoardState _board;
        private CardShipState _player;
        private CardShipState _target;
        private Bounds _seaBounds;
        private Camera _camera;
        private float _cameraAspect;
        private int _screenWidth;
        private int _screenHeight;
        private Color _playerAliveColor;
        private Color _targetAliveColor;
        private RelativeDirection _aim = RelativeDirection.Forward;
        private bool _targetWillFire;
        private int _turn;
        private string _status;

        public int HandCount => _deck == null ? 0 : _deck.Hand.Count;

        private void OnValidate()
        {
            playerInitialHealth = Mathf.Max(1, playerInitialHealth);
            targetInitialHealth = Mathf.Max(1, targetInitialHealth);
        }

        private void Awake()
        {
            if (!HasBindings())
            {
                Debug.LogError("Local card UI is not bound. Install it with Tools > Naval Commander > Install Local Card UI.", this);
                enabled = false;
                return;
            }

            _catalog = Resources.LoadAll<CardDefinition>("CardDefinitions");
            if (_catalog.Length != 12)
            {
                Debug.LogError($"Local card playtest requires 12 card definitions in Resources/CardDefinitions; found {_catalog.Length}.", this);
                enabled = false;
                return;
            }

            SpriteRenderer sea = FindSeaRenderer();
            _camera = Camera.main;
            if (sea == null || _camera == null)
            {
                Debug.LogError("Local card playtest requires the GameScene sea renderer and Main Camera.", this);
                enabled = false;
                return;
            }

            _seaBounds = sea.bounds;
            _resolver = new CardEffectResolver(_catalog);
            _playerAliveColor = playerMarker.color;
            _targetAliveColor = targetMarker.color;
            BindControls();
            FitCamera();
            ResetMatch();
        }

        private bool HasBindings()
        {
            if (playerMarker == null || targetMarker == null ||
                targetFireToggle == null || resolveTurnButton == null || resetMatchButton == null ||
                statusLabel == null || playerHealthLabel == null || targetHealthLabel == null ||
                cardSlotButtons == null || cardSlotLabels == null || aimButtons == null || aimLabels == null ||
                cardSlotButtons.Length != 3 || cardSlotLabels.Length != 3 ||
                aimButtons.Length != 4 || aimLabels.Length != 4)
            {
                return false;
            }

            for (int index = 0; index < 3; index++)
            {
                if (cardSlotButtons[index] == null || cardSlotLabels[index] == null)
                {
                    return false;
                }
            }

            for (int index = 0; index < 4; index++)
            {
                if (aimButtons[index] == null || aimLabels[index] == null)
                {
                    return false;
                }
            }

            return true;
        }

        private void BindControls()
        {
            for (int index = 0; index < cardSlotButtons.Length; index++)
            {
                int slot = index;
                cardSlotButtons[index].onClick.AddListener(() => SelectCardAt(slot));
            }

            for (int index = 0; index < aimButtons.Length; index++)
            {
                RelativeDirection direction = (RelativeDirection)index;
                aimButtons[index].onClick.AddListener(() => SetAim(direction));
            }

            targetFireToggle.onValueChanged.AddListener(value => _targetWillFire = value);
            resolveTurnButton.onClick.AddListener(ResolveTurn);
            resetMatchButton.onClick.AddListener(ResetMatch);
        }

        private SpriteRenderer FindSeaRenderer()
        {
            foreach (GameObject root in gameObject.scene.GetRootGameObjects())
            {
                if (root.name == "Sea Tiles_0")
                {
                    return root.GetComponent<SpriteRenderer>();
                }
            }

            return null;
        }

        private void ResetMatch()
        {
            _board = new CardBoardState(BoardSize, BoardSize);
            _player = _board.AddShip(PlayerId, 3, 2, CardinalDirection.North, Mathf.Max(1, playerInitialHealth));
            _target = _board.AddShip(TargetId, 3, 5, CardinalDirection.South, Mathf.Max(1, targetInitialHealth));
            _deck = new PlayerCardDeck(_catalog, DemoSeed, 1UL);
            _deck.BeginTurn();
            _selectedCards.Clear();
            _aim = RelativeDirection.Forward;
            _targetWillFire = false;
            targetFireToggle.SetIsOnWithoutNotify(false);
            _turn = 1;
            _status = "Select up to two cards, then resolve the turn. Movement is ship-relative.";
            UpdateMarkers();
            RefreshUi();
        }

        private void FitCamera()
        {
            _cameraAspect = Mathf.Max(0.5f, _camera.aspect);
            _screenWidth = Screen.width;
            _screenHeight = Screen.height;
            Canvas.ForceUpdateCanvases();

            RectTransform panel = cardSlotButtons[0].transform.parent as RectTransform;
            var panelCorners = new Vector3[4];
            panel.GetWorldCorners(panelCorners);
            float reservedLeft = Mathf.Clamp(
                (panelCorners[3].x + 20f) / Mathf.Max(1, _screenWidth), 0.15f, 0.65f);
            const float rightMargin = 0.02f;
            float availableWidth = 1f - reservedLeft - rightMargin;
            float size = Mathf.Max(
                _seaBounds.extents.y / 0.94f,
                _seaBounds.extents.x / (_cameraAspect * availableWidth)) * 1.03f;
            float seaViewportCenter = (reservedLeft + 1f - rightMargin) * 0.5f;
            Vector3 position = _camera.transform.position;
            _camera.transform.position = new Vector3(
                _seaBounds.center.x - (seaViewportCenter - 0.5f) * 2f * size * _cameraAspect,
                _seaBounds.center.y,
                position.z);
            _camera.orthographic = true;
            _camera.orthographicSize = size;
        }

        private void LateUpdate()
        {
            if (_camera != null && (_screenWidth != Screen.width || _screenHeight != Screen.height ||
                !Mathf.Approximately(_cameraAspect, Mathf.Max(0.5f, _camera.aspect))))
            {
                FitCamera();
            }
        }

        private void UpdateMarkers()
        {
            PlaceMarker(playerMarker, _player, _playerAliveColor);
            PlaceMarker(targetMarker, _target, _targetAliveColor);
        }

        private void PlaceMarker(SpriteRenderer marker, CardShipState ship, Color color)
        {
            marker.transform.position = new Vector3(
                _seaBounds.min.x + (ship.X + 0.5f) * _seaBounds.size.x / BoardSize,
                _seaBounds.min.y + (ship.Y + 0.5f) * _seaBounds.size.y / BoardSize,
                -0.2f);
            marker.transform.rotation = Quaternion.Euler(0f, 0f, -90f * (int)ship.Facing);
            marker.color = ship.Health > 0 ? color : Color.gray;
        }

        private void SelectCardAt(int index)
        {
            if (_deck == null || index < 0 || index >= _deck.Hand.Count || _player.Health <= 0)
            {
                return;
            }

            CardDefinition card = _deck.Hand[index];
            if (_deck.TrySelectCard(card.CardId))
            {
                _selectedCards.Add(card);
                _status = $"Selected {card.name}. Resolve the turn when ready.";
                RefreshUi();
            }
        }

        private void SetAim(RelativeDirection direction)
        {
            _aim = direction;
            RefreshUi();
        }

        private void ResolveTurn()
        {
            if (_deck == null || _player.Health <= 0)
            {
                return;
            }

            var commands = new List<CardPlayCommand>(_selectedCards.Count + 1);
            foreach (CardDefinition card in _selectedCards)
            {
                RelativeDirection? direction = card.Category == CardCategory.Attack &&
                    card.CardId != "attack_three_shot" ? _aim : (RelativeDirection?)null;
                commands.Add(new CardPlayCommand(PlayerId, card.CardId, direction));
            }

            bool targetFired = _targetWillFire && _target.Health > 0;
            if (targetFired)
            {
                commands.Add(new CardPlayCommand(TargetId, TargetCardId, RelativeDirection.Forward));
            }

            try
            {
                CardTurnResult turn = _resolver.ResolveTurn(_board, commands);
                var report = new List<string>();
                foreach (CardEffectResolution resolution in turn.Resolutions)
                {
                    if (resolution.ShipId != PlayerId)
                    {
                        continue;
                    }

                    if (!_deck.ResolveSelectedCard(resolution.CardId, resolution.Outcome))
                    {
                        throw new InvalidOperationException($"Selected card '{resolution.CardId}' could not be settled.");
                    }
                    string outcome = resolution.Outcome == CardUseOutcome.BlockedMovement ? "blocked" : "played";
                    report.Add($"{FindCardName(resolution.CardId)}: {outcome}");
                }

                if (targetFired)
                {
                    report.Add("Target fired a torpedo");
                }

                if (report.Count == 0)
                {
                    report.Add("No cards played");
                }

                _deck.BeginTurn();
                _selectedCards.Clear();
                _targetWillFire = false;
                targetFireToggle.SetIsOnWithoutNotify(false);
                _turn++;
                UpdateMarkers();
                report.Add($"Health - player: {_player.Health}, target: {_target.Health}");
                _status = string.Join("\n", report);
            }
            catch (Exception exception)
            {
                Debug.LogException(exception, this);
                _status = "Turn resolution failed. See the Console or reset the match.";
            }

            RefreshUi();
        }

        private string FindCardName(string cardId)
        {
            foreach (CardDefinition card in _catalog)
            {
                if (card.CardId == cardId)
                {
                    return card.name;
                }
            }

            return cardId;
        }

        private void RefreshUi()
        {
            for (int index = 0; index < cardSlotButtons.Length; index++)
            {
                CardDefinition card = index < _deck.Hand.Count ? _deck.Hand[index] : null;
                bool selected = card != null && _selectedCards.Contains(card);
                string label = card == null ? "Empty slot" : card.name +
                    (card.DamagePerHitCell > 0 ? $" ({card.DamagePerHitCell} damage)" : "");
                cardSlotLabels[index].text = selected ? $"Selected: {label}" : $"Select: {label}";
                cardSlotButtons[index].interactable = card != null && !selected &&
                    _selectedCards.Count < 2 && _player.Health > 0;
            }

            for (int index = 0; index < aimLabels.Length; index++)
            {
                RelativeDirection direction = (RelativeDirection)index;
                aimLabels[index].text = _aim == direction ? $"> {direction}" : direction.ToString();
            }

            resolveTurnButton.interactable = _player.Health > 0;
            targetFireToggle.interactable = _target.Health > 0 && _player.Health > 0;
            playerHealthLabel.text = $"Turn {_turn} | Player {_player.Health}/{Mathf.Max(1, playerInitialHealth)}\n" +
                $"Cell ({_player.X}, {_player.Y}) | Facing {_player.Facing}";
            targetHealthLabel.text = $"Target {_target.Health}/{Mathf.Max(1, targetInitialHealth)} | Hand {HandCount}/3\n" +
                $"Selected {_selectedCards.Count}/2";
            statusLabel.text = _status;
        }
    }
}
