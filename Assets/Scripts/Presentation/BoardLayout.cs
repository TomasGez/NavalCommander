using UnityEngine;

namespace NavalCommander.Presentation
{
    /// <summary>
    /// Responsabilidad única: convertir celdas del tablero en posiciones de mundo.
    /// Las celdas son cuadradas y el tablero se centra sobre el mar, alineado con su damero.
    /// </summary>
    public class BoardLayout : MonoBehaviour
    {
        private const float BoardDepth = -0.2f; // delante del mar, detrás de la cámara

        [SerializeField] private string seaObjectName = "Sea Tiles_0";
        [SerializeField] private float cellWorldSize = 1f;   // 1 = un cuadrado del damero del mar
        [SerializeField] private bool snapToSeaGrid = true;

        private SpriteRenderer _sea;

        public float CellSize => cellWorldSize;

        /// <summary>Centro de la celda (x, y) en coordenadas de mundo.</summary>
        public Vector3 CellToWorld(int x, int y, int width, int height)
        {
            Vector2 origin = GetOrigin(width, height);
            return new Vector3(origin.x + (x + 0.5f) * cellWorldSize, origin.y + (y + 0.5f) * cellWorldSize, BoardDepth);
        }

        /// <summary>Rectángulo que ocupa todo el tablero en el mundo.</summary>
        public Rect GetBoardRect(int width, int height)
        {
            Vector2 origin = GetOrigin(width, height);
            return new Rect(origin.x, origin.y, width * cellWorldSize, height * cellWorldSize);
        }

        private Vector2 GetOrigin(int width, int height)
        {
            SpriteRenderer sea = FindSea();
            Vector3 center = sea != null ? sea.bounds.center : Vector3.zero;

            float originX = center.x - width * cellWorldSize / 2f;
            float originY = center.y - height * cellWorldSize / 2f;

            if (snapToSeaGrid && sea != null)
            {
                Vector3 min = sea.bounds.min;
                originX = min.x + Mathf.Round((originX - min.x) / cellWorldSize) * cellWorldSize;
                originY = min.y + Mathf.Round((originY - min.y) / cellWorldSize) * cellWorldSize;
            }
            return new Vector2(originX, originY);
        }

        private SpriteRenderer FindSea()
        {
            if (_sea != null) return _sea;
            var seaObject = GameObject.Find(seaObjectName);
            if (seaObject != null) _sea = seaObject.GetComponent<SpriteRenderer>();
            return _sea;
        }
    }
}
