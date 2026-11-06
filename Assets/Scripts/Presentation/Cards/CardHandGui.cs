using System.Collections.Generic;
using UnityEngine;

namespace NavalCommander.Presentation.Cards
{
    /// <summary>
    /// Dibuja la mano de cartas como imágenes en una franja (OnGUI). No guarda estado de selección:
    /// recibe la lista de seleccionadas y devuelve el id de la carta pulsada en este frame.
    /// Una carta sin arte se dibuja con su id como texto.
    /// </summary>
    public static class CardHandGui
    {
        // Proporción de la carta de origen: 450 x 630 px.
        private const float CardHeightPerWidth = 630f / 450f;
        private const float RaiseRatio = 0.12f;
        private const float GapRatio = 0.08f;
        private static readonly Color UnselectedTint = new Color(0.62f, 0.62f, 0.66f, 1f);
        private static readonly Color SelectedFrame = new Color(1f, 0.82f, 0.20f, 1f);
        private static readonly Color FallbackFill = new Color(0.18f, 0.29f, 0.43f, 1f);
        private static readonly Color FallbackSelectedFill = new Color(0.28f, 0.44f, 0.62f, 1f);

        private static GUIStyle _fallbackStyle;

        /// <summary>Devuelve el id de la carta pulsada, o null si no hubo clic sobre una carta.</summary>
        public static string Draw(Rect area, string[] hand, List<string> selected, CardArtCatalog art)
        {
            if (hand == null || hand.Length == 0 || area.width <= 0f || area.height <= 0f) return null;

            float raise = area.height * RaiseRatio;
            float cardHeight = area.height - raise;
            float cardWidth = cardHeight / CardHeightPerWidth;
            float gap = cardWidth * GapRatio;
            float total = hand.Length * cardWidth + (hand.Length - 1) * gap;
            if (total > area.width)
            {
                float scale = area.width / total;
                cardWidth *= scale;
                cardHeight *= scale;
                gap *= scale;
                raise *= scale;
                total = area.width;
            }

            float x = area.x + (area.width - total) * 0.5f;
            float baseY = area.yMax - cardHeight;
            string clicked = null;
            for (int index = 0; index < hand.Length; index++)
            {
                string cardId = hand[index];
                bool isSelected = selected.Contains(cardId);
                var rect = new Rect(x, isSelected ? baseY - raise : baseY, cardWidth, cardHeight);

                if (TryGetSprite(art, cardId, out Sprite sprite)) DrawSprite(rect, sprite, isSelected);
                else DrawFallback(rect, cardId, isSelected);
                if (isSelected) DrawFrame(rect, SelectedFrame, Mathf.Max(2f, cardWidth * 0.03f));

                // Botón invisible: consume el clic de la carta y devuelve true al soltar.
                if (GUI.Button(rect, GUIContent.none, GUIStyle.none)) clicked = cardId;
                x += cardWidth + gap;
            }

            // Un clic en la franja que no cae en ninguna carta tampoco llega a lo que hay debajo.
            if (Event.current.type == EventType.MouseDown && area.Contains(Event.current.mousePosition))
                Event.current.Use();

            return clicked;
        }

        private static bool TryGetSprite(CardArtCatalog art, string cardId, out Sprite sprite)
        {
            sprite = null;
            return art != null && art.TryGetSprite(cardId, out sprite);
        }

        private static void DrawSprite(Rect rect, Sprite sprite, bool isSelected)
        {
            Texture2D texture = sprite.texture;
            Rect pixels = sprite.textureRect;
            var uv = new Rect(pixels.x / texture.width, pixels.y / texture.height,
                pixels.width / texture.width, pixels.height / texture.height);

            Color previous = GUI.color;
            GUI.color = isSelected ? Color.white : UnselectedTint;
            GUI.DrawTextureWithTexCoords(rect, texture, uv);
            GUI.color = previous;
        }

        private static void DrawFallback(Rect rect, string cardId, bool isSelected)
        {
            if (_fallbackStyle == null)
            {
                _fallbackStyle = new GUIStyle(GUI.skin.box)
                {
                    wordWrap = true,
                    alignment = TextAnchor.MiddleCenter,
                    normal = { textColor = Color.white }
                };
            }

            _fallbackStyle.fontSize = Mathf.Clamp(Mathf.RoundToInt(rect.height * 0.09f), 9, 18);
            Color previous = GUI.color;
            GUI.color = Color.white;
            Color previousFill = GUI.backgroundColor;
            GUI.backgroundColor = isSelected ? FallbackSelectedFill : FallbackFill;
            GUI.Box(rect, cardId, _fallbackStyle);
            GUI.backgroundColor = previousFill;
            GUI.color = previous;
        }

        private static void DrawFrame(Rect rect, Color color, float thickness)
        {
            Color previous = GUI.color;
            GUI.color = color;
            Texture2D white = Texture2D.whiteTexture;
            GUI.DrawTexture(new Rect(rect.x, rect.y, rect.width, thickness), white);
            GUI.DrawTexture(new Rect(rect.x, rect.yMax - thickness, rect.width, thickness), white);
            GUI.DrawTexture(new Rect(rect.x, rect.y, thickness, rect.height), white);
            GUI.DrawTexture(new Rect(rect.xMax - thickness, rect.y, thickness, rect.height), white);
            GUI.color = previous;
        }
    }
}
