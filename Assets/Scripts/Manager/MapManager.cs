using System;
using UnityEngine;

#region Map

[Serializable]
public class BlockData
{
    public int id;
    public int kindId;

    public BlockData(int id, int kindId = 0)
    {
        this.id = id;
        this.kindId = kindId;
    }
}

[Serializable]
public class ChunkData
{
    public static readonly Vector2Int ChunkSize = new Vector2Int(16, 16);

    public BlockData[,] blocks;

    public ChunkData()
    {
        blocks = new BlockData[ChunkSize.x, ChunkSize.y];
    }
}

[Serializable]
public class MapData
{
    public static readonly Vector2Int MapSize = new Vector2Int(16, 16);

    public ChunkData[,] chunks;

    public MapData()
    {
        chunks = new ChunkData[MapSize.x, MapSize.y];
        for (int x = 0; x < MapSize.x; x++)
        {
            for (int y = 0; y < MapSize.y; y++)
            {
                chunks[x, y] = new ChunkData();
            }
        }
    }
}

#endregion

public class MapManager : Singleton<MapManager>
{
    #region Variable

    // Inspector
    [Header("# Generator")]
    [SerializeField] public MapGenerator mapGenerator;

    [Header("# Data")]
    public MapData mapData;

    #endregion

    #region Block Operation

    public void SetBlock(int worldX, int worldY, int id)
    {
        if (mapData == null) return;

        int width = ChunkData.ChunkSize.x;
        int height = ChunkData.ChunkSize.y;

        int cx = worldX / width;
        int cy = worldY / height;
        int lx = worldX % width;
        int ly = worldY % height;

        // Handle negative coordinates
        if (lx < 0) { lx += width; cx--; }
        if (ly < 0) { ly += height; cy--; }

        if (cx < 0 || cx >= MapData.MapSize.x || cy < 0 || cy >= MapData.MapSize.y) return;

        ChunkData chunk = mapData.chunks[cx, cy];
        if (chunk == null) return;

        // Apply change
        if (id < 0)
        {
            chunk.blocks[lx, ly] = null;
        }
        else
        {
            int maxKinds = ResourceManager.Instance != null ? ResourceManager.Instance.GetTileKindCount(id) : 1;
            int kindId = UnityEngine.Random.Range(0, maxKinds);
            chunk.blocks[lx, ly] = new BlockData(id, kindId);
        }

        // Redraw current and neighbors
        if (RenderManager.Instance != null)
        {
            RenderManager.Instance.RequestChunkRedraw(cx, cy);

            // Neighbors for auto-tiling update
            if (lx == 0) RenderManager.Instance.RequestChunkRedraw(cx - 1, cy);
            if (lx == width - 1) RenderManager.Instance.RequestChunkRedraw(cx + 1, cy);
            if (ly == 0) RenderManager.Instance.RequestChunkRedraw(cx, cy - 1);
            if (ly == height - 1) RenderManager.Instance.RequestChunkRedraw(cx, cy + 1);
        }
    }

    #endregion
}
