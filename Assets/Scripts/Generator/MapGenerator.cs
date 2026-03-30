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

    [Header("### Performance")]
    [SerializeField] private int chunksPerFrame = 500; // Increase for speed, decrease for smoothness

    // Runtime Cached Data
    private int totalWidth;
    private int totalHeight;
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

        totalWidth = MapData.MapSize.x * ChunkData.ChunkSize.x;
        totalHeight = MapData.MapSize.y * ChunkData.ChunkSize.y;

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
        MapData data = InitializeMap(baseSeed, WorldStyle.Standard);
        int surfaceY = Mathf.FloorToInt(0.6f * totalHeight);

        for (int cx = 0; cx < MapData.MapSize.x; cx++)
        {
            for (int cy = 0; cy < MapData.MapSize.y; cy++)
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
        MapData data = InitializeMap(baseSeed, WorldStyle.GreatCave);
        int surfaceY = Mathf.FloorToInt(0.4f * totalHeight);

        for (int cx = 0; cx < MapData.MapSize.x; cx++)
        {
            for (int cy = 0; cy < MapData.MapSize.y; cy++)
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
        MapData data = InitializeMap(baseSeed, WorldStyle.Hell);
        int surfaceY = Mathf.FloorToInt(0.2f * totalHeight);

        for (int cx = 0; cx < MapData.MapSize.x; cx++)
        {
            for (int cy = 0; cy < MapData.MapSize.y; cy++)
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

        MapManager.Instance.StoreMap(WorldStyle.Hell, data);
    }

    #endregion

    #region Shared Sub-Steps & Utils

    private MapData InitializeMap(string seed, WorldStyle style)
    {
        string saltedSeed = seed + "_" + style.ToString();
        int seedHash = saltedSeed.GetHashCode();
        Random.InitState(seedHash);
        return new MapData();
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
