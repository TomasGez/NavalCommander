#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using UnityEngine.InputSystem.UI;

namespace NavalCommander.CardMechanics.Playtest
{
    [InitializeOnLoad]
    internal static class LocalCardUiInstaller
    {
        private const string ScenePath = "Assets/Scenes/GameScene.unity";
        private const string RootName = "Local Card Playtest";
        private const string PanelPath = "Canvas/Card Panel";
        private const string LayoutUndoName = "Upgrade Local Card UI Layout";
        private static readonly Color PanelColor = new Color(0.07f, 0.11f, 0.18f, 0.92f);
        private static readonly Color ButtonColor = new Color(0.18f, 0.29f, 0.43f);
        private static readonly Color TextColor = new Color(0.96f, 0.97f, 0.99f);
        private static readonly Color HeadingColor = new Color(0.52f, 0.84f, 0.94f);
        private static readonly string[] PanelChildNames =
        {
            "Title", "Player Health", "Target Health", "Cards Heading",
            "Card Slot 1", "Card Slot 2", "Card Slot 3", "Aim Heading",
            "Aim Controls", "Target Fire", "Turn Controls", "Status"
        };
        private static readonly float[] PanelChildHeights =
        {
            30f, 36f, 36f, 24f, 48f, 48f, 48f, 24f, 40f, 32f, 44f, 76f
        };

        static LocalCardUiInstaller()
        {
            EditorApplication.delayCall += UpgradeLoadedSceneAfterReload;
        }

        [MenuItem("Tools/Naval Commander/Install Local Card UI")]
        private static void Install()
        {
            Scene scene = SceneManager.GetActiveScene();
            if (EditorApplication.isPlayingOrWillChangePlaymode || !scene.IsValid() ||
                !scene.isLoaded || scene.path != ScenePath)
            {
                EditorUtility.DisplayDialog("Local Card UI", "Open GameScene in Edit Mode before installing the UI.", "OK");
                return;
            }

            if (scene.isDirty)
            {
                EditorUtility.DisplayDialog("Local Card UI", "Save or discard current GameScene edits before installing the UI.", "OK");
                return;
            }

            foreach (GameObject candidate in scene.GetRootGameObjects())
            {
                if (candidate.name == RootName)
                {
                    if (!UpgradeExistingLayout(scene, candidate, false))
                    {
                        EditorUtility.DisplayDialog("Local Card UI", "The editable UI already exists. No layout or Inspector values were changed.", "OK");
                    }
                    Selection.activeGameObject = candidate;
                    return;
                }
            }

            SpriteRenderer sea = FindSeaRenderer(scene);
            Sprite markerSprite = AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/UISprite.psd");
            if (markerSprite == null)
            {
                foreach (Object asset in AssetDatabase.LoadAllAssetsAtPath("Assets/Textures/SeaTilesTexture.png"))
                {
                    if (asset is Sprite sprite)
                    {
                        markerSprite = sprite;
                        break;
                    }
                }
            }

            Font font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf") ??
                Resources.GetBuiltinResource<Font>("Arial.ttf");
            if (sea == null || markerSprite == null || font == null)
            {
                EditorUtility.DisplayDialog("Local Card UI", "GameScene needs its sea renderer, an importable sprite, and a built-in font.", "OK");
                return;
            }

            EventSystem eventSystem = FindEventSystem(scene);
            if (eventSystem != null && eventSystem.GetComponent<InputSystemUIInputModule>() == null)
            {
                EditorUtility.DisplayDialog("Local Card UI", "The existing EventSystem needs an InputSystemUIInputModule. It was not modified automatically.", "OK");
                return;
            }

            int undoGroup = Undo.GetCurrentGroup();
            Undo.SetCurrentGroupName("Install Local Card UI");
            try
            {
                GameObject root = new GameObject(RootName);
                Undo.RegisterCreatedObjectUndo(root, "Install Local Card UI");
                SpriteRenderer playerMarker = CreateMarker(root.transform, "Player Ship", sea.bounds,
                    markerSprite, new Color(1f, 0.82f, 0.20f));
                SpriteRenderer targetMarker = CreateMarker(root.transform, "Target Ship", sea.bounds,
                    markerSprite, new Color(0.95f, 0.27f, 0.25f));

                GameObject canvasObject = new GameObject("Canvas", typeof(RectTransform),
                    typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
                canvasObject.transform.SetParent(root.transform, false);
                Canvas canvas = canvasObject.GetComponent<Canvas>();
                canvas.renderMode = RenderMode.ScreenSpaceOverlay;
                CanvasScaler scaler = canvasObject.GetComponent<CanvasScaler>();
                scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
                scaler.referenceResolution = new Vector2(1280f, 720f);
                scaler.matchWidthOrHeight = 0.5f;

                GameObject panel = CreateRect("Card Panel", canvasObject.transform);
                RectTransform panelRect = panel.GetComponent<RectTransform>();
                SetPanelRect(panelRect);
                panel.AddComponent<Image>().color = PanelColor;
                VerticalLayoutGroup layout = panel.AddComponent<VerticalLayoutGroup>();
                layout.padding = new RectOffset(12, 12, 12, 12);
                layout.spacing = 6f;
                layout.childAlignment = TextAnchor.UpperCenter;
                layout.childControlWidth = true;
                layout.childControlHeight = true;
                layout.childForceExpandWidth = true;
                layout.childForceExpandHeight = false;

                Text title = CreateText("Title", panel.transform, font, "Local Card Playtest", 22, 30f);
                title.fontStyle = FontStyle.Bold;
                Text playerHealth = CreateText("Player Health", panel.transform, font, "Player health / position", 15, 36f);
                Text targetHealth = CreateText("Target Health", panel.transform, font, "Target health / hand", 15, 36f);
                Text cardsHeading = CreateText("Cards Heading", panel.transform, font, "Hand - select up to two", 16, 24f);
                cardsHeading.color = HeadingColor;
                cardsHeading.fontStyle = FontStyle.Bold;

                var cardButtons = new Button[3];
                var cardLabels = new Text[3];
                for (int index = 0; index < 3; index++)
                {
                    cardButtons[index] = CreateButton($"Card Slot {index + 1}", panel.transform,
                        font, "Empty slot", 48f, out cardLabels[index]);
                }

                Text aimHeading = CreateText("Aim Heading", panel.transform, font, "Missile / Torpedo aim", 16, 24f);
                aimHeading.color = HeadingColor;
                aimHeading.fontStyle = FontStyle.Bold;
                GameObject aimRow = CreateRow("Aim Controls", panel.transform, 40f);
                var aimButtons = new Button[4];
                var aimLabels = new Text[4];
                string[] aimNames = { "Forward", "Right", "Backward", "Left" };
                for (int index = 0; index < aimButtons.Length; index++)
                {
                    aimButtons[index] = CreateButton($"Aim {aimNames[index]}", aimRow.transform,
                        font, aimNames[index], 40f, out aimLabels[index]);
                    aimLabels[index].fontSize = 14;
                    aimLabels[index].horizontalOverflow = HorizontalWrapMode.Overflow;
                }

                Toggle targetFire = CreateToggle("Target Fire", panel.transform, font,
                    "Target fires a torpedo this turn", 32f);
                GameObject actionRow = CreateRow("Turn Controls", panel.transform, 44f);
                Button resolveButton = CreateButton("Resolve Turn", actionRow.transform,
                    font, "Resolve turn", 44f, out _);
                Button resetButton = CreateButton("Reset Match", actionRow.transform,
                    font, "Reset match", 44f, out _);
                Text status = CreateText("Status", panel.transform, font,
                    "Select cards, then resolve the turn.", 15, 76f);
                status.verticalOverflow = VerticalWrapMode.Truncate;

                if (eventSystem == null)
                {
                    GameObject input = new GameObject("EventSystem", typeof(EventSystem),
                        typeof(InputSystemUIInputModule));
                    Undo.RegisterCreatedObjectUndo(input, "Install Local Card UI");
                }

                LocalCardPlaytestPresenter presenter = root.AddComponent<LocalCardPlaytestPresenter>();
                SerializedObject serialized = new SerializedObject(presenter);
                serialized.FindProperty("playerMarker").objectReferenceValue = playerMarker;
                serialized.FindProperty("targetMarker").objectReferenceValue = targetMarker;
                SetArray(serialized, "cardSlotButtons", cardButtons);
                SetArray(serialized, "cardSlotLabels", cardLabels);
                SetArray(serialized, "aimButtons", aimButtons);
                SetArray(serialized, "aimLabels", aimLabels);
                serialized.FindProperty("targetFireToggle").objectReferenceValue = targetFire;
                serialized.FindProperty("resolveTurnButton").objectReferenceValue = resolveButton;
                serialized.FindProperty("resetMatchButton").objectReferenceValue = resetButton;
                serialized.FindProperty("statusLabel").objectReferenceValue = status;
                serialized.FindProperty("playerHealthLabel").objectReferenceValue = playerHealth;
                serialized.FindProperty("targetHealthLabel").objectReferenceValue = targetHealth;
                serialized.ApplyModifiedProperties();

                EditorSceneManager.MarkSceneDirty(scene);
                if (!EditorSceneManager.SaveScene(scene))
                {
                    throw new System.InvalidOperationException("GameScene could not be saved.");
                }

                Undo.CollapseUndoOperations(undoGroup);
                Selection.activeGameObject = root;
                Debug.Log("Installed the editable local card UI in GameScene. Starting health is on Local Card Playtest.", root);
            }
            catch (System.Exception exception)
            {
                Undo.RevertAllDownToGroup(undoGroup);
                Debug.LogException(exception);
                EditorUtility.DisplayDialog("Local Card UI", "Installation failed. The scene changes were reverted; see Console for details.", "OK");
            }
        }

        private static void UpgradeLoadedSceneAfterReload()
        {
            Scene scene = SceneManager.GetActiveScene();
            if (EditorApplication.isPlayingOrWillChangePlaymode || !scene.IsValid() ||
                !scene.isLoaded || scene.path != ScenePath || scene.isDirty)
            {
                return;
            }

            foreach (GameObject root in scene.GetRootGameObjects())
            {
                if (root.name == RootName)
                {
                    UpgradeExistingLayout(scene, root, true);
                    return;
                }
            }
        }

        private static bool UpgradeExistingLayout(Scene scene, GameObject root, bool automatic)
        {
            RectTransform panel = root.transform.Find(PanelPath) as RectTransform;
            VerticalLayoutGroup layout = panel == null ? null : panel.GetComponent<VerticalLayoutGroup>();
            if (root.GetComponent<LocalCardPlaytestPresenter>() == null ||
                !IsLegacyLayout(panel, layout))
            {
                return false;
            }

            var elements = new LayoutElement[PanelChildNames.Length];
            for (int index = 0; index < elements.Length; index++)
            {
                Transform child = panel.Find(PanelChildNames[index]);
                if (child == null || child.parent != panel ||
                    (elements[index] = child.GetComponent<LayoutElement>()) == null)
                {
                    Debug.LogWarning($"Local card UI layout was not upgraded: {PanelChildNames[index]} is missing or modified.", root);
                    return false;
                }
            }

            Text title = panel.Find("Title").GetComponent<Text>();
            Text cardsHeading = panel.Find("Cards Heading").GetComponent<Text>();
            Text aimHeading = panel.Find("Aim Heading").GetComponent<Text>();
            Text playerHealth = panel.Find("Player Health").GetComponent<Text>();
            Text targetHealth = panel.Find("Target Health").GetComponent<Text>();
            Text status = panel.Find("Status").GetComponent<Text>();
            var aimLabels = new Text[4];
            string[] directions = { "Forward", "Right", "Backward", "Left" };
            for (int index = 0; index < aimLabels.Length; index++)
            {
                Transform label = panel.Find($"Aim Controls/Aim {directions[index]}/Label");
                aimLabels[index] = label == null ? null : label.GetComponent<Text>();
            }

            if (title == null || cardsHeading == null || aimHeading == null ||
                playerHealth == null || targetHealth == null || status == null ||
                System.Array.Exists(aimLabels, label => label == null))
            {
                Debug.LogWarning("Local card UI layout was not upgraded: expected text controls are missing.", root);
                return false;
            }

            int undoGroup = Undo.GetCurrentGroup();
            Undo.SetCurrentGroupName(LayoutUndoName);
            try
            {
                Undo.RecordObject(panel, LayoutUndoName);
                SetPanelRect(panel);
                Undo.RecordObject(layout, LayoutUndoName);
                layout.childControlHeight = true;
                for (int index = 0; index < elements.Length; index++)
                {
                    Undo.RecordObject(elements[index], LayoutUndoName);
                    elements[index].minHeight = PanelChildHeights[index];
                    elements[index].preferredHeight = PanelChildHeights[index];
                }

                Undo.RecordObject(title, LayoutUndoName);
                title.fontStyle = FontStyle.Bold;
                Undo.RecordObject(cardsHeading, LayoutUndoName);
                cardsHeading.fontSize = 16;
                cardsHeading.fontStyle = FontStyle.Bold;
                cardsHeading.color = HeadingColor;
                Undo.RecordObject(aimHeading, LayoutUndoName);
                aimHeading.fontSize = 16;
                aimHeading.fontStyle = FontStyle.Bold;
                aimHeading.color = HeadingColor;
                Undo.RecordObject(playerHealth, LayoutUndoName);
                playerHealth.fontSize = 15;
                Undo.RecordObject(targetHealth, LayoutUndoName);
                targetHealth.fontSize = 15;
                Undo.RecordObject(status, LayoutUndoName);
                status.fontSize = 15;
                status.verticalOverflow = VerticalWrapMode.Truncate;
                foreach (Text label in aimLabels)
                {
                    Undo.RecordObject(label, LayoutUndoName);
                    label.fontSize = 14;
                    label.horizontalOverflow = HorizontalWrapMode.Overflow;
                }

                EditorSceneManager.MarkSceneDirty(scene);
                if (!EditorSceneManager.SaveScene(scene))
                {
                    throw new System.InvalidOperationException("GameScene could not be saved after the UI layout upgrade.");
                }

                Undo.CollapseUndoOperations(undoGroup);
                Debug.Log("Upgraded the existing local card UI layout without replacing its hierarchy or Inspector bindings.", root);
                if (!automatic)
                {
                    EditorUtility.DisplayDialog("Local Card UI", "The existing layout was upgraded and saved. Inspector values and bindings were preserved.", "OK");
                }
                return true;
            }
            catch (System.Exception exception)
            {
                Undo.RevertAllDownToGroup(undoGroup);
                Debug.LogException(exception);
                return false;
            }
        }

        private static bool IsLegacyLayout(RectTransform panel, VerticalLayoutGroup layout)
        {
            return panel != null && layout != null && !layout.childControlHeight &&
                Mathf.Approximately(panel.sizeDelta.x, 370f) &&
                Mathf.Approximately(panel.sizeDelta.y, 670f) &&
                panel.anchorMin == new Vector2(0f, 1f) &&
                panel.anchorMax == new Vector2(0f, 1f);
        }

        private static void SetPanelRect(RectTransform panel)
        {
            panel.anchorMin = new Vector2(0f, 0f);
            panel.anchorMax = new Vector2(0f, 1f);
            panel.pivot = new Vector2(0f, 0.5f);
            panel.anchoredPosition = new Vector2(12f, 0f);
            panel.sizeDelta = new Vector2(340f, -24f);
        }

        private static SpriteRenderer FindSeaRenderer(Scene scene)
        {
            foreach (GameObject root in scene.GetRootGameObjects())
            {
                if (root.name == "Sea Tiles_0")
                {
                    return root.GetComponent<SpriteRenderer>();
                }
            }

            return null;
        }

        private static EventSystem FindEventSystem(Scene scene)
        {
            foreach (GameObject root in scene.GetRootGameObjects())
            {
                EventSystem system = root.GetComponentInChildren<EventSystem>(true);
                if (system != null)
                {
                    return system;
                }
            }

            return null;
        }

        private static SpriteRenderer CreateMarker(Transform parent, string name, Bounds sea,
            Sprite sprite, Color color)
        {
            GameObject markerObject = new GameObject(name, typeof(SpriteRenderer));
            markerObject.transform.SetParent(parent, false);
            markerObject.transform.localScale = new Vector3(
                sea.size.x / 8f * 0.62f / Mathf.Max(sprite.bounds.size.x, 0.001f),
                sea.size.y / 8f * 0.62f / Mathf.Max(sprite.bounds.size.y, 0.001f),
                1f);
            SpriteRenderer marker = markerObject.GetComponent<SpriteRenderer>();
            marker.sprite = sprite;
            marker.color = color;
            marker.sortingOrder = 10;

            GameObject headingObject = new GameObject("Heading", typeof(SpriteRenderer));
            headingObject.transform.SetParent(markerObject.transform, false);
            headingObject.transform.localPosition = new Vector3(0f, sprite.bounds.extents.y * 0.7f, 0f);
            headingObject.transform.localScale = new Vector3(0.28f, 0.18f, 1f);
            SpriteRenderer heading = headingObject.GetComponent<SpriteRenderer>();
            heading.sprite = sprite;
            heading.color = Color.black;
            heading.sortingOrder = 11;
            return marker;
        }

        private static GameObject CreateRect(string name, Transform parent)
        {
            GameObject gameObject = new GameObject(name, typeof(RectTransform));
            gameObject.transform.SetParent(parent, false);
            return gameObject;
        }

        private static void SetPreferredHeight(GameObject gameObject, float height)
        {
            LayoutElement element = gameObject.AddComponent<LayoutElement>();
            element.preferredHeight = height;
            element.minHeight = height;
        }

        private static Text CreateText(string name, Transform parent, Font font,
            string content, int fontSize, float height)
        {
            GameObject gameObject = CreateRect(name, parent);
            Text text = gameObject.AddComponent<Text>();
            text.font = font;
            text.text = content;
            text.fontSize = fontSize;
            text.color = TextColor;
            text.alignment = TextAnchor.MiddleLeft;
            text.raycastTarget = false;
            SetPreferredHeight(gameObject, height);
            return text;
        }

        private static GameObject CreateRow(string name, Transform parent, float height)
        {
            GameObject row = CreateRect(name, parent);
            SetPreferredHeight(row, height);
            HorizontalLayoutGroup layout = row.AddComponent<HorizontalLayoutGroup>();
            layout.spacing = 6f;
            layout.childControlWidth = true;
            layout.childControlHeight = true;
            layout.childForceExpandWidth = true;
            layout.childForceExpandHeight = true;
            return row;
        }

        private static Button CreateButton(string name, Transform parent, Font font,
            string caption, float height, out Text label)
        {
            GameObject gameObject = CreateRect(name, parent);
            Image image = gameObject.AddComponent<Image>();
            image.color = ButtonColor;
            Button button = gameObject.AddComponent<Button>();
            button.targetGraphic = image;
            ColorBlock colors = button.colors;
            colors.normalColor = Color.white;
            colors.highlightedColor = new Color(0.80f, 0.91f, 1f);
            colors.pressedColor = new Color(0.55f, 0.77f, 0.95f);
            colors.disabledColor = new Color(0.45f, 0.45f, 0.45f, 0.7f);
            button.colors = colors;
            SetPreferredHeight(gameObject, height);

            label = CreateText("Label", gameObject.transform, font, caption, 16, height);
            RectTransform rect = label.GetComponent<RectTransform>();
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = new Vector2(6f, 0f);
            rect.offsetMax = new Vector2(-6f, 0f);
            label.alignment = TextAnchor.MiddleCenter;
            return button;
        }

        private static Toggle CreateToggle(string name, Transform parent, Font font,
            string caption, float height)
        {
            GameObject gameObject = CreateRect(name, parent);
            SetPreferredHeight(gameObject, height);
            Toggle toggle = gameObject.AddComponent<Toggle>();
            GameObject box = CreateRect("Background", gameObject.transform);
            Image background = box.AddComponent<Image>();
            background.color = ButtonColor;
            RectTransform boxRect = box.GetComponent<RectTransform>();
            boxRect.anchorMin = new Vector2(0f, 0.5f);
            boxRect.anchorMax = new Vector2(0f, 0.5f);
            boxRect.pivot = new Vector2(0f, 0.5f);
            boxRect.sizeDelta = new Vector2(24f, 24f);

            GameObject check = CreateRect("Checkmark", box.transform);
            Image checkImage = check.AddComponent<Image>();
            checkImage.color = new Color(1f, 0.82f, 0.20f);
            RectTransform checkRect = check.GetComponent<RectTransform>();
            checkRect.anchorMin = Vector2.zero;
            checkRect.anchorMax = Vector2.one;
            checkRect.offsetMin = new Vector2(5f, 5f);
            checkRect.offsetMax = new Vector2(-5f, -5f);
            toggle.targetGraphic = background;
            toggle.graphic = checkImage;
            toggle.isOn = false;

            Text label = CreateText("Label", gameObject.transform, font, caption, 15, height);
            RectTransform labelRect = label.GetComponent<RectTransform>();
            labelRect.anchorMin = Vector2.zero;
            labelRect.anchorMax = Vector2.one;
            labelRect.offsetMin = new Vector2(32f, 0f);
            labelRect.offsetMax = Vector2.zero;
            return toggle;
        }

        private static void SetArray<T>(SerializedObject serialized, string propertyName, T[] values)
            where T : Object
        {
            SerializedProperty array = serialized.FindProperty(propertyName);
            array.arraySize = values.Length;
            for (int index = 0; index < values.Length; index++)
            {
                array.GetArrayElementAtIndex(index).objectReferenceValue = values[index];
            }
        }
    }
}
#endif
