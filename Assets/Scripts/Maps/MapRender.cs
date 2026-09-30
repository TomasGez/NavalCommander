using UnityEngine;

public class MapRender : MonoBehaviour
{
    [SerializeField] private float cellSize = 1f;

    private void Start()
    {
        DrawMap(_map01.Cells);
    }

    private void DrawMap(int[,] cells)
    {
        int height = cells.GetLength(0);
        int width = cells.GetLength(1);

        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                Debug.Log($"Celda [{x},{y}] = {cells[y, x]}");
            }
        }
    }

    private void OnDrawGizmos()
    {
        int[,] cells = _map01.Cells;

        int height = cells.GetLength(0);
        int width = cells.GetLength(1);

        for (int y = 0; y <= height; y++)
        {
            Vector3 start = new Vector3(0, 0, y * cellSize);
            Vector3 end = new Vector3(width * cellSize, 0, y * cellSize);

            Gizmos.DrawLine(start, end);
        }

        for (int x = 0; x <= width; x++)
        {
            Vector3 start = new Vector3(x * cellSize, 0, 0);
            Vector3 end = new Vector3(x * cellSize, 0, height * cellSize);

            Gizmos.DrawLine(start, end);
        }
    }
}