using UnityEngine;

namespace NavalCommander.Presentation
{
    /// <summary>
    /// Responsabilidad única: calcular cuánto se reduce el HUD de partida (IMGUI) en ventanas chicas.
    /// A 1280x720 o más vale 1, así el diseño no cambia. Por debajo, el HUD se dibuja como si la ventana
    /// midiera 1280x720 y se escala por el lado que menos entra.
    /// </summary>
    public static class HudScale
    {
        private const float ReferenceWidth = 1280f;
        private const float ReferenceHeight = 720f;

        public static float For(int screenWidth, int screenHeight)
        {
            return Mathf.Min(1f, screenWidth / ReferenceWidth, screenHeight / ReferenceHeight);
        }
    }
}
