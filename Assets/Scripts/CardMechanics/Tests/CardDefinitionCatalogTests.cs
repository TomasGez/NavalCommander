using System;
using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace NavalCommander.CardMechanics.Tests
{
    public sealed class CardDefinitionCatalogTests
    {
        private sealed class CardExpectation
        {
            public CardExpectation(string name, string category, int? damage = null)
            {
                Name = name;
                Category = category;
                Damage = damage;
            }

            public string Name { get; }
            public string Category { get; }
            public int? Damage { get; }
        }

        private static readonly CardExpectation[] ExpectedCards =
        {
            new CardExpectation("Shield", "Defense"),
            new CardExpectation("Mirror", "Defense"),
            new CardExpectation("Rotate Right 90°", "Movement"),
            new CardExpectation("Rotate Left 90°", "Movement"),
            new CardExpectation("Rotate 180°", "Movement"),
            new CardExpectation("Move Left", "Movement"),
            new CardExpectation("Move Right", "Movement"),
            new CardExpectation("Move Up", "Movement"),
            new CardExpectation("Move Down", "Movement"),
            new CardExpectation("Missile", "Attack", 30),
            new CardExpectation("Torpedo", "Attack", 50),
            new CardExpectation("Three-Shot", "Attack", 40)
        };

        [Test]
        public void Catalog_ContainsExactlyTwelveCardDefinitionAssets()
        {
            var assets = FindCardDefinitionAssets();
            Assert.That(
                assets,
                Has.Length.EqualTo(12),
                "Expected exactly 12 CardDefinition assets under Assets.");

            var actualNames = assets.Select(asset => asset.name)
                .OrderBy(name => name, StringComparer.Ordinal)
                .ToArray();
            var expectedNames = ExpectedCards.Select(card => card.Name)
                .OrderBy(name => name, StringComparer.Ordinal)
                .ToArray();

            Assert.That(
                actualNames,
                Is.EqualTo(expectedNames),
                "The catalog must contain exactly the 12 agreed card names.");
        }

        [Test]
        public void Catalog_CardsHaveTheirAgreedCategories()
        {
            var assets = FindCardDefinitionAssets();

            foreach (var expectedCard in ExpectedCards)
            {
                var asset = FindAssetByName(assets, expectedCard.Name);
                Assert.That(asset, Is.Not.Null, $"Missing card asset '{expectedCard.Name}'.");
                if (asset == null)
                {
                    continue;
                }

                Assert.That(
                    HasSerializedCategory(asset, expectedCard.Category),
                    Is.True,
                    $"Card '{expectedCard.Name}' must serialize category '{expectedCard.Category}'.");
            }
        }

        [Test]
        public void AttackCards_UseAgreedDamageValuesPerHitCell()
        {
            var assets = FindCardDefinitionAssets();

            foreach (var expectedCard in ExpectedCards.Where(card => card.Damage.HasValue))
            {
                var asset = FindAssetByName(assets, expectedCard.Name);
                Assert.That(asset, Is.Not.Null, $"Missing attack card asset '{expectedCard.Name}'.");
                if (asset == null)
                {
                    continue;
                }

                Assert.That(
                    HasSerializedDamageValue(asset, expectedCard.Damage.Value),
                    Is.True,
                    $"Attack '{expectedCard.Name}' must serialize damage {expectedCard.Damage.Value} per hit cell.");
            }
        }

        private static UnityEngine.Object[] FindCardDefinitionAssets()
        {
            return AssetDatabase.FindAssets("t:CardDefinition", new[] { "Assets" })
                .Select(guid => AssetDatabase.GUIDToAssetPath(guid))
                .Select(path => AssetDatabase.LoadMainAssetAtPath(path))
                .Where(asset => asset != null)
                .ToArray();
        }

        private static UnityEngine.Object FindAssetByName(UnityEngine.Object[] assets, string expectedName)
        {
            return assets.FirstOrDefault(asset =>
                string.Equals(asset.name, expectedName, StringComparison.Ordinal));
        }

        private static bool HasSerializedCategory(UnityEngine.Object asset, string expectedCategory)
        {
            var iterator = new SerializedObject(asset).GetIterator();
            while (iterator.Next(true))
            {
                if (iterator.propertyPath.IndexOf("category", StringComparison.OrdinalIgnoreCase) < 0)
                {
                    continue;
                }

                if (iterator.propertyType == SerializedPropertyType.String &&
                    string.Equals(iterator.stringValue, expectedCategory, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }

                if (iterator.propertyType == SerializedPropertyType.Enum)
                {
                    var enumNames = iterator.enumDisplayNames;
                    var enumIndex = iterator.enumValueIndex;
                    if (enumIndex >= 0 && enumIndex < enumNames.Length &&
                        string.Equals(enumNames[enumIndex], expectedCategory, StringComparison.OrdinalIgnoreCase))
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        private static bool HasSerializedDamageValue(UnityEngine.Object asset, int expectedDamage)
        {
            var iterator = new SerializedObject(asset).GetIterator();
            while (iterator.Next(true))
            {
                if (iterator.propertyPath.IndexOf("damage", StringComparison.OrdinalIgnoreCase) < 0)
                {
                    continue;
                }

                if (iterator.propertyType == SerializedPropertyType.Integer &&
                    iterator.intValue == expectedDamage)
                {
                    return true;
                }

                if (iterator.propertyType == SerializedPropertyType.Float &&
                    Math.Abs(iterator.floatValue - expectedDamage) < 0.001f)
                {
                    return true;
                }
            }

            return false;
        }
    }
}