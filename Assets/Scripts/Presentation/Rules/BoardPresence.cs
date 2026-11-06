namespace NavalCommander.Presentation
{
    /// <summary>
    /// Responsabilidad única: decidir si el tablero está dibujado en este cuadro. Sin conexión se limpia;
    /// con conexión aparece recién cuando llegan los barcos y, una vez dibujado, se mantiene.
    /// </summary>
    public static class BoardPresence
    {
        public static bool Next(bool wasDrawn, bool connected, bool hasShips)
        {
            if (!connected) return false;
            return wasDrawn || hasShips;
        }
    }
}
