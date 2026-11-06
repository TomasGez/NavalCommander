namespace NavalCommander.Presentation
{
    /// <summary>
    /// Responsabilidad única: las rutas de las texturas del HUD de partida dentro de Resources.
    /// Viven aquí para que una prueba detecte un nombre mal escrito: el HUD, ante una textura que falta,
    /// vuelve al aspecto por defecto sin avisar.
    /// </summary>
    public static class HudArt
    {
        public const string Panel = "HudArt/hud-panel";
        public const string ButtonNormal = "HudArt/hud-button-normal";
        public const string ButtonHover = "HudArt/hud-button-hover";
        public const string ButtonPressed = "HudArt/hud-button-pressed";
        public const string ConfirmNormal = "HudArt/hud-confirm-normal";
        public const string ConfirmHover = "HudArt/hud-confirm-hover";
        public const string ConfirmPressed = "HudArt/hud-confirm-pressed";
        public const string AimNormal = "HudArt/hud-aim-normal";
        public const string AimHover = "HudArt/hud-aim-hover";
        public const string AimSelected = "HudArt/hud-aim-selected";

        public static readonly string[] All =
        {
            Panel,
            ButtonNormal, ButtonHover, ButtonPressed,
            ConfirmNormal, ConfirmHover, ConfirmPressed,
            AimNormal, AimHover, AimSelected
        };
    }
}
