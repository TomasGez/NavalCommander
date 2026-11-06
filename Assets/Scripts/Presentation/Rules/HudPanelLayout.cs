using UnityEngine;

namespace NavalCommander.Presentation
{
    /// <summary>
    /// Responsabilidad única: la geometría de la columna del HUD de partida, en el espacio de 1280x720.
    /// El panel va de x 4 a 348, el contenido de x 18 a 338 y el tablero empieza en x 352.
    /// La altura del panel la define su contenido; Area solo fija hasta dónde puede crecer.
    /// </summary>
    public static class HudPanelLayout
    {
        public const int PanelBorder = 24;
        public const int PlateBorder = 10;
        public const float PlateHeight = 32f;
        public const float BoardLeftEdge = 352f;

        private const float PanelMargin = 4f;
        private const float PanelWidth = 344f;

        public static Rect Area(float hudHeight) =>
            new Rect(PanelMargin, PanelMargin, PanelWidth, hudHeight - PanelMargin * 2f);

        public static RectOffset Padding() => new RectOffset(14, 10, 14, 12);
    }
}
