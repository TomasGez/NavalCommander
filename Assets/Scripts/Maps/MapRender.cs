using UnityEngine;

public class MapRender : MonoBehaviour
{
    [SerializeField]
    private float cellSize = 1f;

    [SerializeField]
    private Vector3 offset;

    [SerializeField]
    private GameObject obstaclePrefab;

    [SerializeField]
    private GameObject player1Prefab;

    [SerializeField]
    private GameObject player2Prefab;

    [SerializeField]
    private GameObject player3Prefab;

    [SerializeField]
    private GameObject player4Prefab;

    private void Start()
    {
        DrawMap(_map01.Cells);
    }

    private void DrawMap(int[,] cells)
    {
        int height = cells.GetLength(0);
        int width = cells.GetLength(1);

        float mapWidth = width * cellSize;
        float mapHeight = height * cellSize;

        float startX = -mapWidth / 2f;
        float startZ = -mapHeight / 2f;

        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                int cellValue = cells[y, x];

                if (cellValue == 1)
                {
                    CreateObstacle(x, y, startX, startZ);
                }
                else if (cellValue >= 2 && cellValue <= 5)
                {
                    CreatePlayer(cellValue, x, y, startX, startZ);
                }
            }
        }
    }

    private void CreateObstacle(int x, int y, float startX, float startZ)
    {
        Vector3 position = GetCellPosition(x, y, startX, startZ);

        GameObject obstacle = Instantiate(obstaclePrefab, position, Quaternion.identity, transform);

        obstacle.transform.localScale = new Vector3(cellSize, 1f, cellSize);
    }

    private void CreatePlayer(int playerId, int x, int y, float startX, float startZ)
    {
        GameObject playerPrefab = GetPlayerPrefab(playerId);

        Vector3 position = GetCellPosition(x, y, startX, startZ);

        Instantiate(playerPrefab, position, Quaternion.identity, transform);
    }

    private GameObject GetPlayerPrefab(int playerId)
    {
        switch (playerId)
        {
            case 2:
                return player1Prefab;

            case 3:
                return player2Prefab;

            case 4:
                return player3Prefab;

            case 5:
                return player4Prefab;

            default:
                return null;
        }
    }

    private Vector3 GetCellPosition(int x, int y, float startX, float startZ)
    {
        return new Vector3(
                startX + x * cellSize + cellSize / 2f,
                0f,
                startZ + y * cellSize + cellSize / 2f
            ) + offset;
    }

    private void OnDrawGizmos()
    {
        int[,] cells = _map01.Cells;

        int height = cells.GetLength(0);
        int width = cells.GetLength(1);

        float mapWidth = width * cellSize;
        float mapHeight = height * cellSize;

        float startX = -mapWidth / 2f;
        float startZ = -mapHeight / 2f;

        for (int y = 0; y <= height; y++)
        {
            float z = startZ + y * cellSize;

            Vector3 start = new Vector3(startX, 0f, z) + offset;

            Vector3 end = new Vector3(startX + mapWidth, 0f, z) + offset;

            Gizmos.DrawLine(start, end);
        }

        for (int x = 0; x <= width; x++)
        {
            float xPosition = startX + x * cellSize;

            Vector3 start = new Vector3(xPosition, 0f, startZ) + offset;

            Vector3 end = new Vector3(xPosition, 0f, startZ + mapHeight) + offset;

            Gizmos.DrawLine(start, end);
        }
    }
}
