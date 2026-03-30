using System;
using System.Collections;
using UnityEngine;

#region Map

[Serializable]
public struct BlockData
{
    public bool isActive;
    public ushort id;
    public byte kindId;

    public BlockData(int id, int kindId = 0, bool isActive = true)
    {
        this.id = (ushort)id;
        this.kindId = (byte)kindId;
        this.isActive = isActive;
    }
}

[Serializable]
public class ChunkData
{
    public static readonly Vector2Int ChunkSize = new Vector2Int(16, 16);

    public BlockData[,] blocks;
    public byte[,] lightValues;

    public ChunkData()
    {
        blocks = new BlockData[ChunkSize.x, ChunkSize.y];
        lightValues = new byte[ChunkSize.x, ChunkSize.y];
    }
}

[Serializable]
public class MapData
{
    public static readonly Vector2Int MapSize = new Vector2Int(128, 128);

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
    public MapData activeMapData; // Currently rendered map
    public WorldStyle activeStyle;
    
    private System.Collections.Generic.Dictionary<WorldStyle, MapData> worldMaps = new System.Collections.Generic.Dictionary<WorldStyle, MapData>();

    #endregion

    #region Map Management

    public void StoreMap(WorldStyle style, MapData data)
    {
        worldMaps[style] = data;
    }


    #endregion

    #region Map Generate

    public IEnumerator GenerateMapCo()
    {
        yield return StartCoroutine(mapGenerator.GenerateAllWorldsCo());

        yield return StartCoroutine(SwitchWorldCo(WorldStyle.Standard));
    }

    #endregion

    #region Switch

    public IEnumerator SwitchWorldCo(WorldStyle style)
    {
        if (!worldMaps.TryGetValue(style, out MapData data))
        {
            Debug.LogWarning($"[MapManager] World {style} not found! Generate it first.");
            yield break;
        }

        activeMapData = data;
        activeStyle = style;

        Debug.Log($"[MapManager] Switching to {style} world asynchronously...");

        // 1. Calculate lighting for the new map FIRST
        if (LightingManager.Instance != null)
        {
            LightingManager.Instance.CalculateAllLighting();
        }

        // 2. Then refresh visuals using the calculated light values
        if (MeshManager.Instance != null)
        {
            yield return StartCoroutine(MeshManager.Instance.RequestFullRedrawCo());
        }

        Debug.Log($"[MapManager] Successfully switched to {style} world.");
    }

    #endregion

    #region Block Operation

    public void SetBlock(int worldX, int worldY, int id)
    {
        if (activeMapData == null) return;

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

        ChunkData chunk = activeMapData.chunks[cx, cy];
        if (chunk == null) return;

        // Apply change
        if (id < 0)
        {
            chunk.blocks[lx, ly] = default; // Air (isActive = false)
        }
        else
        {
            int maxKinds = ResourceManager.Instance != null ? ResourceManager.Instance.GetTileKindCount(id) : 1;
            int kindId = UnityEngine.Random.Range(0, maxKinds);
            chunk.blocks[lx, ly] = new BlockData(id, kindId, true);
        }

        // Update lighting
        if (LightingManager.Instance != null)
        {
            LightingManager.Instance.UpdateLightingAt(worldX, worldY);
        }

        // Redraw current and neighbors
        if (MeshManager.Instance != null)
        {
            MeshManager.Instance.RequestChunkRedraw(cx, cy);

            // Neighbors for auto-tiling update
            if (lx == 0) MeshManager.Instance.RequestChunkRedraw(cx - 1, cy);
            if (lx == width - 1) MeshManager.Instance.RequestChunkRedraw(cx + 1, cy);
            if (ly == 0) MeshManager.Instance.RequestChunkRedraw(cx, cy - 1);
            if (ly == height - 1) MeshManager.Instance.RequestChunkRedraw(cx, cy + 1);
        }
    }

    #endregion

    #region Utility

    /// <summary>
    /// Returns a world position based on the ratio of the total map size.
    /// </summary>
    /// <param name="ratioX">X ratio (0-100)</param>
    /// <param name="ratioY">Y ratio (0-100)</param>
    /// <returns>Calculated world position</returns>
    public Vector2 GetPositionByRatio(float ratioX, float ratioY)
    {
        int totalWidth = MapData.MapSize.x * ChunkData.ChunkSize.x;
        int totalHeight = MapData.MapSize.y * ChunkData.ChunkSize.y;

        float x = (ratioX / 100f) * totalWidth;
        float y = (ratioY / 100f) * totalHeight;

        return new Vector2(x, y);
    }

    #endregion
}
