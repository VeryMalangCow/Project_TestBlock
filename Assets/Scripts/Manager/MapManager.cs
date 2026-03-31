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
    public static readonly Vector2Int ChunkSize = new Vector2Int(8, 8);

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
    public static readonly Vector2Int StandardMapSize = new Vector2Int(300, 240);
    public static readonly Vector2Int GreatCaveMapSize = new Vector2Int(400, 200);
    public static readonly Vector2Int HellMapSize = new Vector2Int(240, 400);

    public Vector2Int mapSize;

    public ChunkData[,] chunks;

    public MapData(Vector2Int size)
    {
        this.mapSize = size;
        chunks = new ChunkData[mapSize.x, mapSize.y];
        for (int x = 0; x < mapSize.x; x++)
        {
            for (int y = 0; y < mapSize.y; y++)
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
        Debug.Log("[MapManager] Step 1: Starting MapGenerator.GenerateAllWorldsCo...");
        yield return StartCoroutine(mapGenerator.GenerateAllWorldsCo());

        Debug.Log("[MapManager] Step 2: Map Generation finished. Switching to Standard world...");
        yield return StartCoroutine(SwitchWorldCo(WorldStyle.Standard));
        Debug.Log("[MapManager] Step 3: Initial World Switch Sequence Complete.");
    }

    #endregion

    #region Switch

    public IEnumerator SwitchWorldCo(WorldStyle style)
    {
        Debug.Log($"[MapManager] Attempting to switch to world: {style}");

        if (!worldMaps.TryGetValue(style, out MapData data))
        {
            Debug.LogError($"[MapManager] FATAL: World {style} not found in worldMaps dictionary! Generation might have failed.");
            yield break;
        }

        // IMPORTANT: Clear existing chunks BEFORE changing the active map data
        if (MeshManager.Instance != null)
        {
            MeshManager.Instance.ClearAllChunks();
        }

        activeMapData = data;
        activeStyle = style;

        Debug.Log($"[MapManager] Data found for {style}. MapSize: {data.mapSize}. Starting lighting calculation...");

        // 1. Calculate lighting for the new map FIRST
        if (LightingManager.Instance != null)
        {
            LightingManager.Instance.CalculateAllLighting();
            Debug.Log("[MapManager] Lighting calculation finished.");
        }

        // 2. Then refresh visuals using the calculated light values
        if (MeshManager.Instance != null)
        {
            Debug.Log("[MapManager] Activating all chunks via MeshManager...");
            MeshManager.Instance.RefreshAllChunks();
            
            Debug.Log("[MapManager] Requesting MeshManager to redraw all chunks...");
            yield return StartCoroutine(MeshManager.Instance.RequestFullRedrawCo());
            Debug.Log("[MapManager] MeshManager.RequestFullRedrawCo finished.");
        }
        else
        {
            Debug.LogWarning("[MapManager] MeshManager.Instance is null! Cannot redraw.");
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

        if (cx < 0 || cx >= activeMapData.mapSize.x || cy < 0 || cy >= activeMapData.mapSize.y) return;

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
        if (activeMapData == null) return Vector2.zero;

        int totalWidth = activeMapData.mapSize.x * ChunkData.ChunkSize.x;
        int totalHeight = activeMapData.mapSize.y * ChunkData.ChunkSize.y;

        float x = (ratioX / 100f) * totalWidth;
        float y = (ratioY / 100f) * totalHeight;

        return new Vector2(x, y);
    }

    #endregion
}
