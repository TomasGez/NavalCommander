using System;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace NavalCommander.Presentation.Cards.Tests
{
    // Cada id de CardDefinition del proyecto debe tener un Sprite en CardArtCatalog.
    // Este asmdef no referencia Assembly-CSharp, así que lee los campos serializados por nombre.
    public sealed class CardArtCatalogTests
    {
        private const string CatalogResourcePath = "CardArt/CardArtCatalog";

        [Test]
        public void EveryCardDefinitionId_ResolvesToSprite()
        {
            var catalog = Resources.Load<ScriptableObject>(CatalogResourcePath);
            Assert.That(catalog, Is.Not.Null,
                $"Missing Resources/{CatalogResourcePath}.asset with the card art.");

            Dictionary<string, Sprite> art = ReadArt(catalog);
            List<string> cardIds = ReadCardDefinitionIds();
            Assert.That(cardIds, Is.Not.Empty, "Expected CardDefinition assets under Assets.");

            foreach (string cardId in cardIds)
            {
                Assert.That(art.TryGetValue(cardId, out Sprite sprite) && sprite != null, Is.True,
                    $"Card '{cardId}' has no sprite in the card art catalog.");
            }
        }

        private static Dictionary<string, Sprite> ReadArt(ScriptableObject catalog)
        {
            var result = new Dictionary<string, Sprite>(StringComparer.Ordinal);
            SerializedProperty entries = new SerializedObject(catalog).FindProperty("entries");
            for (int index = 0; index < entries.arraySize; index++)
            {
                SerializedProperty entry = entries.GetArrayElementAtIndex(index);
                string cardId = entry.FindPropertyRelative("cardId").stringValue;
                result[cardId] = entry.FindPropertyRelative("sprite").objectReferenceValue as Sprite;
            }

            return result;
        }

        private static List<string> ReadCardDefinitionIds()
        {
            var ids = new List<string>();
            foreach (string guid in AssetDatabase.FindAssets("t:CardDefinition", new[] { "Assets" }))
            {
                UnityEngine.Object asset = AssetDatabase.LoadMainAssetAtPath(AssetDatabase.GUIDToAssetPath(guid));
                if (asset == null)
                {
                    continue;
                }

                ids.Add(new SerializedObject(asset).FindProperty("cardId").stringValue);
            }

            return ids;
        }
    }
}
