using System.Collections;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace NavalCommander.CardMechanics.PlayModeTests
{
    public sealed class LocalCardPlaytestSmokeTests
    {
        private const string GameScenePath = "Assets/Scenes/GameScene.unity";
        private const string PlaytestRootName = "Local Card Playtest";

        [UnityTest]
        public IEnumerator GameScene_ShowsLocalHandAndTargetsWithoutNetworkSession()
        {
            AsyncOperation load = SceneManager.LoadSceneAsync(GameScenePath, LoadSceneMode.Single);
            Assert.That(load, Is.Not.Null, "GameScene must be enabled in Build Settings.");
            yield return load;
            yield return null;

            Scene scene = SceneManager.GetActiveScene();
            Assert.That(scene.path, Is.EqualTo(GameScenePath));

            GameObject root = null;
            foreach (GameObject candidate in scene.GetRootGameObjects())
            {
                if (candidate.name == PlaytestRootName)
                {
                    root = candidate;
                    break;
                }
            }

            Assert.That(root, Is.Not.Null, "GameScene must create its local card playtest root in Play Mode.");
            Assert.That(root.activeInHierarchy, Is.True);

            PropertyInfo handCount = null;
            MonoBehaviour presenter = null;
            foreach (MonoBehaviour component in root.GetComponents<MonoBehaviour>())
            {
                if (component == null)
                {
                    continue;
                }

                PropertyInfo candidate = component.GetType().GetProperty(
                    "HandCount", BindingFlags.Instance | BindingFlags.Public);
                if (candidate != null && candidate.PropertyType == typeof(int))
                {
                    presenter = component;
                    handCount = candidate;
                    break;
                }
            }

            Assert.That(presenter, Is.Not.Null, "The local playtest root must expose its current HandCount.");
            Assert.That(handCount.GetValue(presenter), Is.EqualTo(3), "The opening hand must contain three cards.");
            AssertEditableUi(root, presenter);

            Camera camera = Camera.main;
            Assert.That(camera, Is.Not.Null, "GameScene needs its Main Camera.");
            AssertCardPanelFitsBesideBoard(root, camera);
            AssertVisibleMarker(root, "Player Ship", camera);
            AssertVisibleMarker(root, "Target Ship", camera);

            foreach (MonoBehaviour component in Object.FindObjectsByType<MonoBehaviour>(
                         FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                if (component == null || component.GetType().FullName != "Unity.Netcode.NetworkManager")
                {
                    continue;
                }

                PropertyInfo isListening = component.GetType().GetProperty("IsListening");
                Assert.That(isListening, Is.Not.Null);
                Assert.That(isListening.GetValue(component), Is.False, "The local playtest must not start an NGO session.");
            }
        }

        private static void AssertEditableUi(GameObject root, MonoBehaviour presenter)
        {
            Transform canvas = null;
            foreach (Transform child in root.GetComponentsInChildren<Transform>(true))
            {
                if (HasComponent(child.gameObject, "UnityEngine.Canvas"))
                {
                    canvas = child;
                    break;
                }
            }

            Assert.That(canvas, Is.Not.Null, "The playtest needs an editable UGUI Canvas in its hierarchy.");
            Assert.That(canvas.gameObject.activeInHierarchy, Is.True);

            for (int slot = 1; slot <= 3; slot++)
            {
                AssertNamedComponent(canvas, $"Card Slot {slot}", "UnityEngine.UI.Button");
            }

            foreach (string direction in new[] { "Forward", "Right", "Backward", "Left" })
            {
                AssertNamedComponent(canvas, $"Aim {direction}", "UnityEngine.UI.Button");
            }

            AssertNamedComponent(canvas, "Target Fire", "UnityEngine.UI.Toggle");
            AssertNamedComponent(canvas, "Resolve Turn", "UnityEngine.UI.Button");
            AssertNamedComponent(canvas, "Reset Match", "UnityEngine.UI.Button");
            AssertNamedText(canvas, "Status");
            AssertNamedText(canvas, "Player Health");
            AssertNamedText(canvas, "Target Health");

            MonoBehaviour inputModule = null;
            foreach (MonoBehaviour component in Object.FindObjectsByType<MonoBehaviour>(
                         FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                if (component != null &&
                    component.GetType().FullName == "UnityEngine.InputSystem.UI.InputSystemUIInputModule")
                {
                    inputModule = component;
                    break;
                }
            }

            Assert.That(inputModule, Is.Not.Null, "The UGUI Canvas needs an InputSystemUIInputModule.");
            Assert.That(HasComponent(inputModule.gameObject, "UnityEngine.EventSystems.EventSystem"), Is.True);
            Assert.That(inputModule.gameObject.activeInHierarchy, Is.True);

            AssertPositiveSerializedHealth(presenter, "playerInitialHealth");
            AssertPositiveSerializedHealth(presenter, "targetInitialHealth");
        }

        private static void AssertPositiveSerializedHealth(MonoBehaviour presenter, string fieldName)
        {
            FieldInfo field = presenter.GetType().GetField(
                fieldName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            Assert.That(field, Is.Not.Null, $"Expose {fieldName} on the playtest presenter.");
            Assert.That(field.FieldType, Is.EqualTo(typeof(int)));
            Assert.That(field.IsDefined(typeof(SerializeField), true), Is.True,
                $"{fieldName} must be editable in the Inspector.");
            Assert.That((int)field.GetValue(presenter), Is.GreaterThan(0));
        }

        private static void AssertCardPanelFitsBesideBoard(GameObject root, Camera camera)
        {
            RectTransform panel = root.transform.Find("Canvas/Card Panel") as RectTransform;
            Assert.That(panel, Is.Not.Null, "The editable UI needs a Card Panel.");
            Canvas.ForceUpdateCanvases();

            var panelCorners = new Vector3[4];
            panel.GetWorldCorners(panelCorners);
            foreach (RectTransform child in panel.GetComponentsInChildren<RectTransform>(false))
            {
                if (child.parent != panel)
                {
                    continue;
                }

                var childCorners = new Vector3[4];
                child.GetWorldCorners(childCorners);
                Assert.That(childCorners[0].y, Is.GreaterThanOrEqualTo(panelCorners[0].y - 2f),
                    $"{child.name} must not extend below the panel.");
                Assert.That(childCorners[1].x, Is.GreaterThanOrEqualTo(panelCorners[1].x - 2f),
                    $"{child.name} must not extend left of the panel.");
                Assert.That(childCorners[2].y, Is.LessThanOrEqualTo(panelCorners[2].y + 2f),
                    $"{child.name} must not extend above the panel.");
                Assert.That(childCorners[3].x, Is.LessThanOrEqualTo(panelCorners[3].x + 2f),
                    $"{child.name} must not extend right of the panel.");
            }

            if (Screen.width < 800 || Screen.width < Screen.height * 1.2f)
            {
                return;
            }

            SpriteRenderer sea = GameObject.Find("Sea Tiles_0")?.GetComponent<SpriteRenderer>();
            Assert.That(sea, Is.Not.Null, "GameScene needs its sea board.");
            Vector3 seaLeft = camera.WorldToScreenPoint(sea.bounds.min);
            Assert.That(seaLeft.x, Is.GreaterThanOrEqualTo(panelCorners[3].x + 8f),
                "The sea board must not sit underneath the card panel in landscape Game View.");
        }

        private static void AssertNamedComponent(Transform canvas, string name, string componentType)
        {
            Transform child = FindNamedChild(canvas, name);
            Assert.That(child, Is.Not.Null, $"The editable card UI needs {name}.");
            Assert.That(HasComponent(child.gameObject, componentType), Is.True,
                $"{name} must have a {componentType} component.");
        }

        private static void AssertNamedText(Transform canvas, string name)
        {
            Transform child = FindNamedChild(canvas, name);
            Assert.That(child, Is.Not.Null, $"The editable card UI needs {name}.");
            Assert.That(HasComponent(child.gameObject, "UnityEngine.UI.Text") ||
                HasComponent(child.gameObject, "TMPro.TextMeshProUGUI"), Is.True,
                $"{name} must be an editable UGUI text label.");
        }

        private static Transform FindNamedChild(Transform parent, string name)
        {
            foreach (Transform child in parent.GetComponentsInChildren<Transform>(true))
            {
                if (child.name == name)
                {
                    return child;
                }
            }

            return null;
        }

        private static bool HasComponent(GameObject gameObject, string fullTypeName)
        {
            foreach (Component component in gameObject.GetComponents<Component>())
            {
                if (component != null && component.GetType().FullName == fullTypeName)
                {
                    return true;
                }
            }

            return false;
        }

        private static void AssertVisibleMarker(GameObject root, string markerName, Camera camera)
        {
            SpriteRenderer marker = null;
            foreach (SpriteRenderer candidate in root.GetComponentsInChildren<SpriteRenderer>(true))
            {
                if (candidate.gameObject.name == markerName)
                {
                    marker = candidate;
                    break;
                }
            }

            Assert.That(marker, Is.Not.Null, $"The playtest needs a {markerName} marker.");
            Assert.That(marker.gameObject.activeInHierarchy, Is.True);
            Assert.That(marker.enabled, Is.True);
            Assert.That(marker.sprite, Is.Not.Null);
            Assert.That(marker.color.a, Is.GreaterThan(0f));

            Vector3 viewport = camera.WorldToViewportPoint(marker.bounds.center);
            Assert.That(viewport.z, Is.GreaterThan(0f), $"{markerName} must be in front of the camera.");
            Assert.That(viewport.x, Is.InRange(0f, 1f), $"{markerName} must be on screen.");
            Assert.That(viewport.y, Is.InRange(0f, 1f), $"{markerName} must be on screen.");
        }
    }
}
