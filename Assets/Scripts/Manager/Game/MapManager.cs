using System;
using System.Collections;
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

#region Map Data Structures

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
    public const int Size = 16; // THE SOURCE OF TRUTH
    public const int TotalCells = Size * Size;
    public static readonly Vector2Int ChunkSize = new Vector2Int(Size, Size);

    public BlockData[] blocks;
    public byte[] lightValues;
    public bool isSynced; 

    public ChunkData()
    {
        blocks = new BlockData[TotalCells];
        lightValues = new byte[TotalCells];
        isSynced = false;
    }

    public static int GetIndex(int x, int y) => y * Size + x;
}

[Serializable]
public class MapData
{
    // Preset counts (Blocks = ChunkCount * ChunkData.Size)
    // 8x8: 300x240 (2400x1920)
    // 16x16: 150x120 (2400x1920)
    public static readonly Vector2Int StandardMapSize = new Vector2Int(150, 120);
    public static readonly Vector2Int GreatCaveMapSize = new Vector2Int(200, 100);
    public static readonly Vector2Int HellMapSize = new Vector2Int(120, 200);

    // Dynamic Spawn Pos based on World Pixels (Blocks)
    public static readonly Vector2 StanardSpawnPos = new Vector2(1200, 1360);
    public static readonly Vector2 GreatCaveSpawnPos = new Vector2(1600, 800);
    public static readonly Vector2 HellMapSpawnPos = new Vector2(960, 1600);

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

    [Header("# Generator")]
    [SerializeField] public MapGenerator mapGenerator;

    [Header("# Data")]
    public MapData activeMapData;
    public WorldStyle activeStyle;
    private Dictionary<WorldStyle, MapData> worldMaps = new Dictionary<WorldStyle, MapData>();

    private bool isMapReady = false;
    private HashSet<Vector2Int> requestedChunks = new HashSet<Vector2Int>();

    #endregion

    #region Network Lifecycle

    public override void OnNetworkSpawn()
    {
        if (IsServer)
        {
            string seed = mapGenerator.GetBaseSeed();
            StartCoroutine(ServerInitialGenerationCo(seed));
        }
        else
        {
            InitializeEmptyMaps();
            isMapReady = true; 
        }
    }

    private void InitializeEmptyMaps()
    {
        worldMaps[WorldStyle.Standard] = new MapData(MapData.StandardMapSize);
        activeMapData = worldMaps[WorldStyle.Standard];
        activeStyle = WorldStyle.Standard;
    }

    private IEnumerator ServerInitialGenerationCo(string seed)
    {
        yield return StartCoroutine(mapGenerator.GenerateAllWorldsCo(seed));
        yield return StartCoroutine(SwitchWorldCo(WorldStyle.Standard));
        isMapReady = true;
    }

    #endregion

    #region Chunk Sync (The Core Logic)

    public bool IsChunkSynced(int cx, int cy)
    {
        if (activeMapData == null || cx < 0 || cy < 0 || cx >= activeMapData.mapSize.x || cy >= activeMapData.mapSize.y) return true;
        return activeMapData.chunks[cx, cy].isSynced || IsServer;
    }

    public void RequestChunkSync(int cx, int cy)
    {
        if (IsServer) return;
        Vector2Int coord = new Vector2Int(cx, cy);
        if (requestedChunks.Contains(coord)) return;

        requestedChunks.Add(coord);
        RequestChunkServerRpc(cx, cy);
    }

    [ServerRpc()]
    private void RequestChunkServerRpc(int cx, int cy, ServerRpcParams rpcParams = default)
    {
        if (activeMapData == null) return;
        if (cx < 0 || cy < 0 || cx >= activeMapData.mapSize.x || cy >= activeMapData.mapSize.y) return;

        ChunkData chunk = activeMapData.chunks[cx, cy];
        
        List<int> compressedBlocks = new List<int>();
        if (chunk.blocks.Length > 0)
        {
            int currentId = chunk.blocks[0].id;
            int currentKind = chunk.blocks[0].kindId;
            bool currentActive = chunk.blocks[0].isActive;
            int count = 0;

            for (int i = 0; i < chunk.blocks.Length; i++)
            {
                if (chunk.blocks[i].id == currentId && chunk.blocks[i].kindId == currentKind && chunk.blocks[i].isActive == currentActive && count < 255)
                {
                    count++;
                }
                else
                {
                    compressedBlocks.Add(currentId);
                    compressedBlocks.Add(currentKind);
                    compressedBlocks.Add(currentActive ? 1 : 0);
                    compressedBlocks.Add(count);

                    currentId = chunk.blocks[i].id;
                    currentKind = chunk.blocks[i].kindId;
                    currentActive = chunk.blocks[i].isActive;
                    count = 1;
                }
            }
            compressedBlocks.Add(currentId);
            compressedBlocks.Add(currentKind);
            compressedBlocks.Add(currentActive ? 1 : 0);
            compressedBlocks.Add(count);
        }

        List<int> compressedLights = new List<int>();
        if (chunk.lightValues.Length > 0)
        {
            byte currentVal = chunk.lightValues[0];
            int count = 0;
            for (int i = 0; i < chunk.lightValues.Length; i++)
            {
                if (chunk.lightValues[i] == currentVal && count < 255) count++;
                else
                {
                    compressedLights.Add(currentVal);
                    compressedLights.Add(count);
                    currentVal = chunk.lightValues[i];
                    count = 1;
                }
            }
            compressedLights.Add(currentVal);
            compressedLights.Add(count);
        }

        DeliverChunkClientRpc(cx, cy, compressedBlocks.ToArray(), compressedLights.ToArray(), 
            new ClientRpcParams { Send = new ClientRpcSendParams { TargetClientIds = new[] { rpcParams.Receive.SenderClientId } } });
    }

    [ClientRpc]
    private void DeliverChunkClientRpc(int cx, int cy, int[] compressedBlocks, int[] compressedLights, ClientRpcParams clientRpcParams = default)
    {
        if (IsServer) return;
        if (activeMapData == null) return;

        ChunkData chunk = activeMapData.chunks[cx, cy];

        int bIdx = 0;
        for (int i = 0; i < compressedBlocks.Length; i += 4)
        {
            int id = compressedBlocks[i];
            int kind = compressedBlocks[i + 1];
            bool active = compressedBlocks[i + 2] == 1;
            int count = compressedBlocks[i + 3];

            for (int j = 0; j < count; j++)
            {
                if (bIdx < chunk.blocks.Length)
                {
                    chunk.blocks[bIdx] = new BlockData(id, kind, active);
                    bIdx++;
                }
            }
        }

        int lIdx = 0;
        for (int i = 0; i < compressedLights.Length; i += 2)
        {
            byte val = (byte)compressedLights[i];
            int count = compressedLights[i + 1];
            for (int j = 0; j < count; j++)
            {
                if (lIdx < chunk.lightValues.Length)
                {
                    chunk.lightValues[lIdx] = val;
                    lIdx++;
                }
            }
        }

        chunk.isSynced = true;
        requestedChunks.Remove(new Vector2Int(cx, cy));

        if (LightingManager.Instance != null) LightingManager.Instance.SyncChunkLight(cx, cy, chunk.lightValues);

        if (MeshManager.Instance != null)
        {
            MeshManager.Instance.RequestChunkRedraw(cx, cy);
            MeshManager.Instance.RequestChunkRedraw(cx - 1, cy);
            MeshManager.Instance.RequestChunkRedraw(cx + 1, cy);
            MeshManager.Instance.RequestChunkRedraw(cx, cy - 1);
            MeshManager.Instance.RequestChunkRedraw(cx, cy + 1);
        }
    }

    #endregion

    #region Map Management

    public void StoreMap(WorldStyle style, MapData data) { worldMaps[style] = data; }

    public IEnumerator SwitchWorldCo(WorldStyle style)
    {
        if (!worldMaps.TryGetValue(style, out MapData data)) yield break;
        if (MeshManager.Instance != null) MeshManager.Instance.ClearAllChunks();
        activeMapData = data;
        activeStyle = style;
        if (IsServer && LightingManager.Instance != null) yield return StartCoroutine(LightingManager.Instance.CalculateAllLightingCo());
    }

    #endregion

    #region Block Operation

    public void SetBlock(int worldX, int worldY, int id)
    {
        InternalSetBlock(worldX, worldY, id);
        if (IsServer) SetBlockClientRpc(worldX, worldY, id);
        else SetBlockServerRpc(worldX, worldY, id);
    }

    [ServerRpc()]
    private void SetBlockServerRpc(int worldX, int worldY, int id, ServerRpcParams rpcParams = default)
    {
        InternalSetBlock(worldX, worldY, id);
        SetBlockClientRpc(worldX, worldY, id, new ClientRpcParams { Send = new ClientRpcSendParams { TargetClientIds = null } });
    }

    [ClientRpc]
    private void SetBlockClientRpc(int worldX, int worldY, int id, ClientRpcParams clientRpcParams = default)
    {
        if (IsServer) return;
        InternalSetBlock(worldX, worldY, id);
    }

    private void InternalSetBlock(int worldX, int worldY, int id)
    {
        if (activeMapData == null) return;
        int size = ChunkData.Size;

        int cx = Mathf.FloorToInt((float)worldX / size);
        int cy = Mathf.FloorToInt((float)worldY / size);
        int lx = worldX - (cx * size);
        int ly = worldY - (cy * size);

        if (cx < 0 || cx >= activeMapData.mapSize.x || cy < 0 || cy >= activeMapData.mapSize.y) return;

        ChunkData chunk = activeMapData.chunks[cx, cy];
        int idx = ChunkData.GetIndex(lx, ly);

        if (chunk.blocks[idx].isActive && chunk.blocks[idx].id == id && id >= 0) return;
        if (!chunk.blocks[idx].isActive && id < 0) return;

        if (id < 0) chunk.blocks[idx] = default;
        else
        {
            int maxKinds = ResourceManager.Instance != null ? ResourceManager.Instance.GetTileKindCount(id) : 1;
            chunk.blocks[idx] = new BlockData(id, UnityEngine.Random.Range(0, maxKinds), true);
        }

        if (LightingManager.Instance != null) LightingManager.Instance.UpdateLightingAt(worldX, worldY);
        if (MeshManager.Instance != null)
        {
            MeshManager.Instance.RequestChunkRedraw(cx, cy);
            bool isL = (lx == 0); bool isR = (lx == size - 1);
            bool isB = (ly == 0); bool isT = (ly == size - 1);
            if (isL) MeshManager.Instance.RequestChunkRedraw(cx - 1, cy);
            if (isR) MeshManager.Instance.RequestChunkRedraw(cx + 1, cy);
            if (isB) MeshManager.Instance.RequestChunkRedraw(cx, cy - 1);
            if (isT) MeshManager.Instance.RequestChunkRedraw(cx, cy + 1);
            if (isL && isB) MeshManager.Instance.RequestChunkRedraw(cx - 1, cy - 1);
            if (isL && isT) MeshManager.Instance.RequestChunkRedraw(cx - 1, cy + 1);
            if (isR && isB) MeshManager.Instance.RequestChunkRedraw(cx + 1, cy - 1);
            if (isR && isT) MeshManager.Instance.RequestChunkRedraw(cx + 1, cy + 1);
        }
    }

    #endregion

    #region Utility

    public BlockData GetBlock(int worldX, int worldY)
    {
        if (activeMapData == null) return default;
        int size = ChunkData.Size;

        int cx = Mathf.FloorToInt((float)worldX / size);
        int cy = Mathf.FloorToInt((float)worldY / size);
        int lx = worldX - (cx * size);
        int ly = worldY - (cy * size);

        if (cx < 0 || cx >= activeMapData.mapSize.x || cy < 0 || cy >= activeMapData.mapSize.y) return default;

        ChunkData chunk = activeMapData.chunks[cx, cy];
        return chunk.blocks[ChunkData.GetIndex(lx, ly)];
    }

    public bool IsBlockActive(int worldX, int worldY)
    {
        return GetBlock(worldX, worldY).isActive;
    }

    public Vector2 GetSurfacePosition(float ratioX)
    {
        if (activeMapData == null) return Vector2.zero;
        int size = ChunkData.Size;
        int totalWidth = activeMapData.mapSize.x * size;
        int worldX = Mathf.Clamp(Mathf.FloorToInt((ratioX / 100f) * totalWidth), 0, totalWidth - 1);
        int cx = worldX / size;
        int lx = worldX % size;

        for (int cy = activeMapData.mapSize.y - 1; cy >= 0; cy--)
        {
            ChunkData chunk = activeMapData.chunks[cx, cy];
            for (int ly = size - 1; ly >= 0; ly--)
            {
                if (chunk.blocks[ChunkData.GetIndex(lx, ly)].isActive) return new Vector2(worldX + 0.5f, (cy * size) + ly + 2f);
            }
        }
        return new Vector2(worldX + 0.5f, (activeMapData.mapSize.y * size) * 0.6f + 5f);
    }

    public bool IsMapReady() => isMapReady;

    public bool IsTerrainReadyAt(Vector2 worldPos)
    {
        if (activeMapData == null) return false;
        int size = ChunkData.Size;
        int cx = Mathf.FloorToInt(worldPos.x / size);
        int cy = Mathf.FloorToInt(worldPos.y / size);

        // Check if the central chunk is synced and FULLY BUILT (Mesh + Collider)
        if (!IsChunkSynced(cx, cy)) return false;
        if (MeshManager.Instance != null && !MeshManager.Instance.IsChunkFullyBuilt(cx, cy)) return false;

        return true;
    }

    #endregion
}
