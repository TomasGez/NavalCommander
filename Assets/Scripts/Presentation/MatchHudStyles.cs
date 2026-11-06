using UnityEngine;

namespace NavalCommander.Presentation
{
    /// <summary>
    /// Responsabilidad única: los estilos IMGUI del HUD de partida, armados con las texturas de HudArt.
    /// Se crea una sola vez y dentro de OnGUI, porque GUI.skin no existe fuera de ese contexto.
    /// Si falta alguna textura de un grupo, ese grupo conserva el aspecto por defecto de IMGUI.
    /// </summary>
    public sealed class MatchHudStyles
    {
        private static readonly Color ColumnText = new Color32(0xE8, 0xF0, 0xFB, 0xFF);
        private static readonly Color PlateText = new Color32(0xF4, 0xF8, 0xFE, 0xFF);
        private static readonly Color TimerText = new Color32(0xFF, 0xB3, 0x47, 0xFF);

        public GUIStyle Panel { get; }
        public GUIStyle Label { get; }
        public GUIStyle Timer { get; }
        public GUIStyle Button { get; }
        public GUIStyle Confirm { get; }
        public GUIStyle Aim { get; }

        public MatchHudStyles()
        {
            Texture2D panel = Resources.Load<Texture2D>(HudArt.Panel);

            Panel = new GUIStyle { name = "hud-panel", padding = HudPanelLayout.Padding() };
            if (panel != null)
            {
                Panel.normal.background = panel;
                Panel.border = Uniform(HudPanelLayout.PanelBorder);
            }

            Label = panel != null ? CreateColumnText("hud-label", ColumnText, FontStyle.Normal) : GUI.skin.label;
            Timer = panel != null ? CreateColumnText("hud-timer", TimerText, FontStyle.Bold) : GUI.skin.label;

            Button = CreatePlate("hud-button", HudArt.ButtonNormal, HudArt.ButtonHover, HudArt.ButtonPressed, new RectOffset(0, 0, 9, 0));
            Confirm = CreatePlate("hud-confirm", HudArt.ConfirmNormal, HudArt.ConfirmHover, HudArt.ConfirmPressed, new RectOffset(0, 0, 6, 9));
            Aim = CreateAim();
        }

        private static GUIStyle CreateColumnText(string name, Color color, FontStyle fontStyle)
        {
            var style = new GUIStyle
            {
                name = name,
                wordWrap = true,
                fontStyle = fontStyle,
                // Entre dos controles vale el mayor de los márgenes enfrentados: 7 entre líneas de texto.
                margin = new RectOffset(0, 0, 7, 5)
            };
            style.normal.textColor = color;
            return style;
        }

        private static GUIStyle CreatePlate(string name, string normalPath, string hoverPath, string pressedPath, RectOffset margin)
        {
            Texture2D normal = Resources.Load<Texture2D>(normalPath);
            Texture2D hover = Resources.Load<Texture2D>(hoverPath);
            Texture2D pressed = Resources.Load<Texture2D>(pressedPath);
            if (normal == null || hover == null || pressed == null) return GUI.skin.button;

            GUIStyle style = CreatePlateBase(name, margin);
            SetState(style.normal, normal);
            SetState(style.hover, hover);
            SetState(style.active, pressed);
            return style;
        }

        // Los márgenes izquierdo y derecho de 4 separan los botones de la barra: 77 de ancho cada uno.
        private static GUIStyle CreateAim()
        {
            Texture2D normal = Resources.Load<Texture2D>(HudArt.AimNormal);
            Texture2D hover = Resources.Load<Texture2D>(HudArt.AimHover);
            Texture2D selected = Resources.Load<Texture2D>(HudArt.AimSelected);
            if (normal == null || hover == null || selected == null) return GUI.skin.button;

            GUIStyle style = CreatePlateBase("hud-aim", new RectOffset(4, 4, 8, 6));
            SetState(style.normal, normal);
            SetState(style.hover, hover);
            SetState(style.active, selected);
            SetState(style.onNormal, selected);
            SetState(style.onHover, selected);
            SetState(style.onActive, selected);
            return style;
        }

        // El nombre debe ser propio: la barra busca variantes por nombre ("buttonleft") y con el de la skin las usaría.
        private static GUIStyle CreatePlateBase(string name, RectOffset margin)
        {
            return new GUIStyle
            {
                name = name,
                border = Uniform(HudPanelLayout.PlateBorder),
                margin = margin,
                alignment = TextAnchor.MiddleCenter,
                fontStyle = FontStyle.Bold,
                fixedHeight = HudPanelLayout.PlateHeight
            };
        }

        private static void SetState(GUIStyleState state, Texture2D background)
        {
            state.background = background;
            state.textColor = PlateText;
        }

        private static RectOffset Uniform(int size) => new RectOffset(size, size, size, size);
    }
}
