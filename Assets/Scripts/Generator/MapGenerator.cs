using UnityEngine;
using System.Collections;

#region Enum

public enum WorldStyle
{
    Standard,   // Surface world (Flat 60%)
    GreatCave,  // Mid-level world (Flat 40%)
    Hell        // Deep-level world (Flat 20%)
}

#endregion

public class MapGenerator : MonoBehaviour
{
    #region Variable

    [Header("### General Settings")]
    [SerializeField] private string baseSeed = "Project_BlockTest";
    [SerializeField] private bool useRandomSeed = true;

    [Header("### Block Config")]
    [SerializeField] private int dirtBlockId = 0;

    [Header("### Map Sizes (Chunks)")]
    [SerializeField] private Vector2Int standardMapSize = MapData.StandardMapSize;
    [SerializeField] private Vector2Int greatCaveMapSize = MapData.GreatCaveMapSize;
    [SerializeField] private Vector2Int hellMapSize = MapData.HellMapSize;

    [Header("### Performance")]
    [SerializeField] private int chunksPerFrame = 500; // Increase for speed, decrease for smoothness

    // Runtime Cached Data
    private int chunksProcessedInFrame;

    public float LoadingProgress { get; private set; }
    public bool IsLoading { get; private set; }

    #endregion

    #region 0. Main Orchestrator (Async)

    public IEnumerator GenerateAllWorldsCo()
    {
        if (MapManager.Instance == null) yield break;

        IsLoading = true;
        LoadingProgress = 0;
        chunksProcessedInFrame = 0;

        if (useRandomSeed)
        {
            baseSeed = Random.Range(int.MinValue, int.MaxValue).ToString();
        }

        Debug.Log($"[MapGenerator] Starting Async Batch Generation. Base Seed: {baseSeed}");

        // Step 1: Standard World
        yield return StartCoroutine(GenerateStandardCo());
        LoadingProgress = 0.33f;
        Debug.Log("[MapGenerator] Standard World Generated.");

        // Step 2: Great Cave World
        yield return StartCoroutine(GenerateGreatCaveCo());
        LoadingProgress = 0.66f;
        Debug.Log("[MapGenerator] Great Cave World Generated.");

        // Step 3: Hell World
        yield return StartCoroutine(GenerateHellCo());
        LoadingProgress = 1.0f;
        Debug.Log("[MapGenerator] Hell World Generated.");

        IsLoading = false;
        Debug.Log("[MapGenerator] All worlds ready.");
    }

    #endregion

    #region 1. Standard Generation (Pass by Pass)

    private IEnumerator GenerateStandardCo()
    {
        // Use inspector-defined size
        MapData data = InitializeMap(baseSeed, WorldStyle.Standard, MapData.StandardMapSize);
        int totalHeight = data.mapSize.y * ChunkData.ChunkSize.y;
        int surfaceY = Mathf.FloorToInt(0.6f * totalHeight);

        for (int cx = 0; cx < data.mapSize.x; cx++)
        {
            for (int cy = 0; cy < data.mapSize.y; cy++)
            {
                FillFlatChunk(data.chunks[cx, cy], cy, surfaceY);
                
                // Direct yielding for performance
                chunksProcessedInFrame++;
                if (chunksProcessedInFrame >= chunksPerFrame)
                {
                    chunksProcessedInFrame = 0;
                    yield return null;
                }
            }
        }

        MapManager.Instance.StoreMap(WorldStyle.Standard, data);
    }

    #endregion

    #region 2. Great Cave Generation (Pass by Pass)

    private IEnumerator GenerateGreatCaveCo()
    {
        // Use inspector-defined size
        MapData data = InitializeMap(baseSeed, WorldStyle.GreatCave, MapData.GreatCaveMapSize);
        int totalHeight = data.mapSize.y * ChunkData.ChunkSize.y;
        int surfaceY = Mathf.FloorToInt(0.4f * totalHeight);

        for (int cx = 0; cx < data.mapSize.x; cx++)
        {
            for (int cy = 0; cy < data.mapSize.y; cy++)
            {
                FillFlatChunk(data.chunks[cx, cy], cy, surfaceY);
                
                chunksProcessedInFrame++;
                if (chunksProcessedInFrame >= chunksPerFrame)
                {
                    chunksProcessedInFrame = 0;
                    yield return null;
                }
            }
        }

        MapManager.Instance.StoreMap(WorldStyle.GreatCave, data);
    }

    #endregion

    #region 3. Hell Generation (Pass by Pass)

    private IEnumerator GenerateHellCo()
    {
        // Use inspector-defined size
        MapData data = InitializeMap(baseSeed, WorldStyle.Hell, MapData.HellMapSize);
        int totalHeight = data.mapSize.y * ChunkData.ChunkSize.y;
        int surfaceY = Mathf.FloorToInt(0.2f * totalHeight);

        for (int cx = 0; cx < data.mapSize.x; cx++)
        {
            for (int cy = 0; cy < data.mapSize.y; cy++)
            {
                FillFlatChunk(data.chunks[cx, cy], cy, surfaceY);
                
                chunksProcessedInFrame++;
                if (chunksProcessedInFrame >= chunksPerFrame)
                {
                    chunksProcessedInFrame = 0;
                    yield return null;
                }
            }
        }

        Debug.Log($"[MapGenerator] {WorldStyle.Hell} World Generated and Storing in MapManager.");
        MapManager.Instance.StoreMap(WorldStyle.Hell, data);
    }

    #endregion

    #region Shared Sub-Steps & Utils

    private MapData InitializeMap(string seed, WorldStyle style, Vector2Int size)
    {
        string saltedSeed = seed + "_" + style.ToString();
        int seedHash = saltedSeed.GetHashCode();
        Random.InitState(seedHash);
        return new MapData(size);
    }

    private void FillFlatChunk(ChunkData chunk, int cy, int surfaceY)
    {
        int height = ChunkData.ChunkSize.y;
        int width = ChunkData.ChunkSize.x;

        for (int x = 0; x < width; x++)
        {
            for (int y = 0; y < height; y++)
            {
                int worldY = cy * height + y;
                if (worldY <= surfaceY)
                {
                    int kindId = GetRandomKindId(dirtBlockId);
                    chunk.blocks[x, y] = new BlockData(dirtBlockId, kindId, true);
                }
            }
        }
    }

    private int GetRandomKindId(int blockId)
    {
        if (ResourceManager.Instance == null) return 0;
        return Random.Range(0, ResourceManager.Instance.GetTileKindCount(blockId));
    }

    #endregion
}