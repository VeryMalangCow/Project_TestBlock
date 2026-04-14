using UnityEngine;
using System.Collections;
using System.Collections.Generic;

#region Enum

public enum WorldStyle
{
    Standard,   // Surface world
    GreatCave,  // Mid-level world
    Hell        // Deep-level world
}

#endregion

public class MapGenerator : MonoBehaviour
{
    #region Variable

    [Header("### General Settings")]
    [SerializeField] private string baseSeed = "Project_BlockTest";
    [SerializeField] private bool useRandomSeed = true;

    [Header("### Terrain Settings (Multi-Octave)")]
    [Tooltip("Main hills (Large scale)")]
    [SerializeField] private float noiseFrequency = 0.015f; 
    [SerializeField] private float noiseAmplitude = 25f;

    [Tooltip("Secondary hills (Mid scale)")]
    [SerializeField] private float detailFrequency = 0.06f;
    [SerializeField] private float detailAmplitude = 6f;

    [Tooltip("Surface roughness (Small scale)")]
    [SerializeField] private float roughFrequency = 0.18f;
    [SerializeField] private float roughAmplitude = 1.8f;

    [SerializeField] private int dirtBlockId = 0;

    [Header("### Map Sizes (Chunks)")]
    [SerializeField] private Vector2Int standardMapSize = MapData.StandardMapSize;
    [SerializeField] private Vector2Int greatCaveMapSize = MapData.GreatCaveMapSize;
    [SerializeField] private Vector2Int hellMapSize = MapData.HellMapSize;

    [Header("### Performance")]
    [SerializeField] private int chunksPerFrame = 500; 

    // Runtime Cached Data
    private int chunksProcessedInFrame;

    public float LoadingProgress { get; private set; }
    public bool IsLoading { get; private set; }

    #endregion

    #region Seed Management

    public string GetBaseSeed()
    {
        if (useRandomSeed)
        {
            baseSeed = Random.Range(int.MinValue, int.MaxValue).ToString();
        }
        return baseSeed;
    }

    #endregion

    #region 0. Main Orchestrator (Async)

    public IEnumerator GenerateAllWorldsCo(string seed)
    {
        if (MapManager.Instance == null) yield break;

        IsLoading = true;
        LoadingProgress = 0;
        chunksProcessedInFrame = 0;
        baseSeed = seed;

        Debug.Log($"[MapGenerator] Starting Async Batch Generation. Seed: {baseSeed}");

        // Step 1: Standard World
        yield return StartCoroutine(GenerateStandardCo());
        
        LoadingProgress = 1.0f;
        IsLoading = false;
        Debug.Log("[MapGenerator] All worlds ready.");
    }

    #endregion

    #region 1. Standard Generation (Pass by Pass)

    private IEnumerator GenerateStandardCo()
    {
        MapData data = InitializeMap(baseSeed, WorldStyle.Standard, MapData.StandardMapSize);
        int seedHash = baseSeed.GetHashCode();

        // Spawn Rules Constants
        Vector2 spawnPos = MapData.StanardSpawnPos; // 1200, 1360
        int spawnX = Mathf.FloorToInt(spawnPos.x);
        int spawnY = Mathf.FloorToInt(spawnPos.y);
        int baselineY = spawnY - 1; // 1359

        // Calculate Fractal Height at Spawn Point
        float spawnHeightVal = GetFractalHeight(spawnX, seedHash);

        for (int cx = 0; cx < data.mapSize.x; cx++)
        {
            for (int cy = 0; cy < data.mapSize.y; cy++)
            {
                GenerateStandardChunk(data.chunks[cx, cy], cx, cy, spawnX, spawnY, baselineY, spawnHeightVal, seedHash);

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

    private void GenerateStandardChunk(ChunkData chunk, int cx, int cy, int spawnX, int spawnY, int baselineY, float spawnHeightVal, int seedHash)
    {
        int size = ChunkData.Size;
        int worldOffsetX = cx * size;
        int worldOffsetY = cy * size;

        for (int x = 0; x < size; x++)
        {
            int worldX = worldOffsetX + x;

            // 1. Calculate Multi-Octave Terrain Height
            float currentHeightVal = GetFractalHeight(worldX, seedHash);
            int heightOffset = Mathf.RoundToInt(currentHeightVal - spawnHeightVal);
            int currentSurfaceY = baselineY + heightOffset;

            for (int y = 0; y < size; y++)
            {
                int worldY = worldOffsetY + y;
                bool isActive = worldY <= currentSurfaceY;

                // 2. Apply Safe Spawn Zone Rules
                // Rule: 6x6 Empty Area
                if (worldX >= spawnX - 3 && worldX <= spawnX + 2 && 
                    worldY >= spawnY && worldY <= spawnY + 5)
                {
                    isActive = false;
                }

                // Rule: 6-block Flat Floor
                if (worldX >= spawnX - 3 && worldX <= spawnX + 2 && worldY == baselineY)
                {
                    isActive = true;
                }

                if (isActive)
                {
                    int kindId = GetRandomKindId(dirtBlockId);
                    chunk.blocks[ChunkData.GetIndex(x, y)] = new BlockData(dirtBlockId, kindId, true);
                }
            }
        }
    }

    #endregion

    #region Shared Sub-Steps & Utils

    private float GetFractalHeight(float x, int seedHash)
    {
        float val = 0;
        // Octave 1: Main Shape (Large Scale)
        val += Mathf.PerlinNoise(x * noiseFrequency, seedHash % 10000) * noiseAmplitude;
        // Octave 2: Details (Mid Scale)
        val += Mathf.PerlinNoise(x * detailFrequency, (seedHash + 123) % 10000) * detailAmplitude;
        // Octave 3: Roughness (Small Scale)
        val += Mathf.PerlinNoise(x * roughFrequency, (seedHash + 456) % 10000) * roughAmplitude;
        return val;
    }

    private MapData InitializeMap(string seed, WorldStyle style, Vector2Int size)
    {
        string saltedSeed = seed + "_" + style.ToString();
        int seedHash = saltedSeed.GetHashCode();
        Random.InitState(seedHash);
        return new MapData(size);
    }

    private int GetRandomKindId(int blockId)
    {
        if (ResourceManager.Instance == null) return 0;
        return Random.Range(0, ResourceManager.Instance.GetTileKindCount(blockId));
    }

    #endregion

    #region Test

    [Header("### Test")]
    [SerializeField] private UnityEngine.UI.Button showAllMapButton;

    private void Start()
    {
        if (showAllMapButton != null)
        {
            showAllMapButton.onClick.AddListener(ShowAllMap);
        }
    }

    public void ShowAllMap()
    {
        if (MeshManager.Instance != null)
        {
            // Toggle sliding window OFF to render everything
            MeshManager.Instance.SetSlidingWindow(false);
            Debug.Log("[MapGenerator] Rendering Entire Map... (Sliding Window Disabled)");
        }
    }

    #endregion
}
