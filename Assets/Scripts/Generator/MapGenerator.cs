using UnityEngine;

public class MapGenerator : MonoBehaviour
{
    #region Variable

    [Header("# TF")]
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
            for (int j = 0; j < 8; j++)
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
                int tileId = 0;
                int maxKinds = ResourceManager.Instance != null ? ResourceManager.Instance.GetTileKindCount(tileId) : 1;
                int kindId = Random.Range(0, maxKinds);
                chunk.blocks[i, j] = new BlockData(tileId, kindId, true);
            }
        }
    }

    #endregion
}

