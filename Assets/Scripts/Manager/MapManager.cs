using System;
using System.Collections;
using Unity.Netcode;
using Unity.Collections;
using UnityEngine;

#region Map

[Serializable]
public struct BlockData : INetworkSerializable
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

    public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
    {
        serializer.SerializeValue(ref isActive);
        serializer.SerializeValue(ref id);
        serializer.SerializeValue(ref kindId);
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

public class MapManager : SingletonNetworkBehaviour<MapManager>
{
    #region Variable

    // Inspector
    [Header("# Generator")]
    [SerializeField] public MapGenerator mapGenerator;

    [Header("# Data")]
    public MapData activeMapData; // Currently rendered map
    public WorldStyle activeStyle;
    
    private System.Collections.Generic.Dictionary<WorldStyle, MapData> worldMaps = new System.Collections.Generic.Dictionary<WorldStyle, MapData>();

    // Network Sync
    private NetworkVariable<FixedString32Bytes> worldSeed = new NetworkVariable<FixedString32Bytes>(string.Empty, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
    private bool isMapReady = false;

    #endregion

    #region Network Event

    public override void OnNetworkSpawn()
    {
        Debug.Log($"[MapManager] OnNetworkSpawn. IsServer: {IsServer}");
        if (IsServer)
        {
            // Server generates seed or uses existing one
            string seed = mapGenerator.GetBaseSeed();
            worldSeed.Value = seed;
            StartCoroutine(GenerateMapCo(seed));
        }
        else
        {
            // Client waits for seed and generates
            if (worldSeed.Value != string.Empty)
            {
                StartCoroutine(GenerateMapCo(worldSeed.Value.ToString()));
            }
            worldSeed.OnValueChanged += (oldVal, newVal) =>
            {
                if (!isMapReady && newVal != string.Empty)
                {
                    StartCoroutine(GenerateMapCo(newVal.ToString()));
                }
            };
        }
    }

    #endregion

    #region Map Management

    public void StoreMap(WorldStyle style, MapData data)
    {
        worldMaps[style] = data;
    }


    #endregion

    #region Map Generate

    public IEnumerator GenerateMapCo(string seed)
    {
        if (isMapReady) yield break;

        Debug.Log($"[MapManager] Step 1: Starting MapGenerator.GenerateAllWorldsCo with seed: {seed}");
        yield return StartCoroutine(mapGenerator.GenerateAllWorldsCo(seed));

        Debug.Log("[MapManager] Step 2: Map Generation finished. Switching to Standard world...");
        yield return StartCoroutine(SwitchWorldCo(WorldStyle.Standard));
        
        isMapReady = true;
        Debug.Log("[MapManager] Step 3: Initial World Switch Sequence Complete. isMapReady = true.");
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
            yield return StartCoroutine(LightingManager.Instance.CalculateAllLightingCo());
            Debug.Log("[MapManager] Lighting calculation finished.");
        }

        // 2. Then refresh visuals based on rendering mode
        if (MeshManager.Instance != null)
        {
            if (MeshManager.Instance.IsSlidingWindowEnabled())
            {
                Debug.Log("[MapManager] Sliding Window is enabled. Chunks will be loaded dynamically around players.");
                // Just clear and let UpdateSlidingWindow handle it
                MeshManager.Instance.ClearAllChunks();
            }
            else
            {
                Debug.Log("[MapManager] Sliding Window is disabled. Activating all chunks...");
                MeshManager.Instance.RefreshAllChunks();
                yield return StartCoroutine(MeshManager.Instance.RequestFullRedrawCo());
                Debug.Log("[MapManager] MeshManager.RequestFullRedrawCo finished.");
            }
        }

        Debug.Log($"[MapManager] Successfully switched to {style} world.");
    }

    #endregion

    #region Block Operation

    public void SetBlock(int worldX, int worldY, int id)
    {
        InternalSetBlock(worldX, worldY, id);

        if (IsServer)
        {
            SetBlockClientRpc(worldX, worldY, id);
        }
    }

    [ClientRpc]
    private void SetBlockClientRpc(int worldX, int worldY, int id)
    {
        if (!IsServer)
        {
            InternalSetBlock(worldX, worldY, id);
        }
    }

    private void InternalSetBlock(int worldX, int worldY, int id)
    {
        if (activeMapData == null) return;

        int width = ChunkData.ChunkSize.x;
        int height = ChunkData.ChunkSize.y;

        int cx = worldX / width;
        int cy = worldY / height;
        int lx = worldX % width;
        int ly = worldY % height;

        if (lx < 0) { lx += width; cx--; }
        if (ly < 0) { ly += height; cy--; }

        if (cx < 0 || cx >= activeMapData.mapSize.x || cy < 0 || cy >= activeMapData.mapSize.y) return;

        ChunkData chunk = activeMapData.chunks[cx, cy];
        if (chunk == null) return;

        if (id < 0)
        {
            chunk.blocks[lx, ly] = default;
        }
        else
        {
            int maxKinds = ResourceManager.Instance != null ? ResourceManager.Instance.GetTileKindCount(id) : 1;
            int kindId = UnityEngine.Random.Range(0, maxKinds);
            chunk.blocks[lx, ly] = new BlockData(id, kindId, true);
        }

        if (LightingManager.Instance != null)
        {
            LightingManager.Instance.UpdateLightingAt(worldX, worldY);
        }

        if (MeshManager.Instance != null)
        {
            MeshManager.Instance.RequestChunkRedraw(cx, cy);
            if (lx == 0) MeshManager.Instance.RequestChunkRedraw(cx - 1, cy);
            if (lx == width - 1) MeshManager.Instance.RequestChunkRedraw(cx + 1, cy);
            if (ly == 0) MeshManager.Instance.RequestChunkRedraw(cx, cy - 1);
            if (ly == height - 1) MeshManager.Instance.RequestChunkRedraw(cx, cy + 1);
        }
    }

    #endregion

    #region Utility

    public Vector2 GetPositionByRatio(float ratioX, float ratioY)
    {
        if (activeMapData == null) return Vector2.zero;

        int totalWidth = activeMapData.mapSize.x * ChunkData.ChunkSize.x;
        int totalHeight = activeMapData.mapSize.y * ChunkData.ChunkSize.y;

        float x = (ratioX / 100f) * totalWidth;
        float y = (ratioY / 100f) * totalHeight;

        return new Vector2(x, y);
    }

    public Vector2 GetSurfacePosition(float ratioX)
    {
        if (activeMapData == null) return Vector2.zero;

        int totalWidth = activeMapData.mapSize.x * ChunkData.ChunkSize.x;
        int totalHeight = activeMapData.mapSize.y * ChunkData.ChunkSize.y;
        int worldX = Mathf.Clamp(Mathf.FloorToInt((ratioX / 100f) * totalWidth), 0, totalWidth - 1);

        int cx = worldX / ChunkData.ChunkSize.x;
        int lx = worldX % ChunkData.ChunkSize.x;

        for (int cy = activeMapData.mapSize.y - 1; cy >= 0; cy--)
        {
            ChunkData chunk = activeMapData.chunks[cx, cy];
            if (chunk == null) continue;

            for (int ly = ChunkData.ChunkSize.y - 1; ly >= 0; ly--)
            {
                if (chunk.blocks[lx, ly].isActive)
                {
                    float worldY = (cy * ChunkData.ChunkSize.y) + ly + 2f;
                    return new Vector2(worldX + 0.5f, worldY);
                }
            }
        }

        return new Vector2(worldX + 0.5f, totalHeight * 0.6f + 5f);
    }

    public bool IsMapReady() => isMapReady;

    #endregion
}
