using UnityEngine;

namespace NavalCommander.Presentation
{
    /// <summary>Formas 2D básicas generadas por código (cuadrados de color) para no depender de assets.</summary>
    public static class Primitives2D
    {
        private static Sprite _square;

        /// <summary>Sprite cuadrado blanco de 1 x 1 unidades de mundo.</summary>
        public static Sprite Square()
        {
            if (_square != null) return _square;

            var texture = new Texture2D(4, 4) { filterMode = FilterMode.Point };
            var pixels = new Color[16];
            for (int i = 0; i < pixels.Length; i++) pixels[i] = Color.white;
            texture.SetPixels(pixels);
            texture.Apply();

            _square = Sprite.Create(texture, new Rect(0, 0, 4, 4), new Vector2(0.5f, 0.5f), 4f); // 4 px a 4 PPU = 1 unidad
            return _square;
        }

        public static GameObject CreateSquare(string name, Transform parent, Color color, int sortingOrder)
        {
            var go = new GameObject(name);
            if (parent != null) go.transform.SetParent(parent, false);

            var renderer = go.AddComponent<SpriteRenderer>();
            renderer.sprite = Square();
            renderer.color = color;
            renderer.sortingOrder = sortingOrder;
            return go;
        }
    }
}
