using UnityEngine;

public class MapGenerator : MonoBehaviour
{
    #region Variable

    [SerializeField] private Transform mapParent;

    #endregion

    #region Generate

    public void GenerateMap()
    {
        if (MapManager.Instance == null) return;

        MapManager.Instance.mapData = new MapData();
        // RandomMapGenerator

        // Test
        for (int i = 0; i < 16; i++)
        {
            for (int j = 0; j < 16; j++)
            {
                GenerateChunk(MapManager.Instance.mapData.chunks[i, j]);
            }
        }
    }

    public void GenerateChunk(ChunkData chunk)
    {
        for (int i = 0; i < 16; i++)
        {
            for (int j = 0; j < 16; j++)
            {
                chunk.blocks[i, j] = new BlockData(0);
            }
        }
    }

    #endregion
}

