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
    public const int Size = 8;
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
    public static readonly Vector2Int StandardMapSize = new Vector2Int(300, 240);
    public static readonly Vector2Int GreatCaveMapSize = new Vector2Int(400, 200);
    public static readonly Vector2Int HellMapSize = new Vector2Int(240, 400);

    // �� ����� ��. ����� X
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
            // Client: Just initialize empty map containers instantly
            InitializeEmptyMaps();
            isMapReady = true; 
            Debug.Log("[MapManager] Client: Empty map structures initialized. Waiting for chunk sync...");
        }
    }

    private void InitializeEmptyMaps()
    {
        // Add more styles if needed
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

    [ServerRpc(RequireOwnership = false)]
    private void RequestChunkServerRpc(int cx, int cy, ServerRpcParams rpcParams = default)
    {
        if (activeMapData == null) return;
        if (cx < 0 || cy < 0 || cx >= activeMapData.mapSize.x || cy >= activeMapData.mapSize.y) return;

        ChunkData chunk = activeMapData.chunks[cx, cy];
        
        // 1. Compress BlockData using RLE
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

        // 2. Compress LightValues using RLE
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

        // 1. Decompress BlockData
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

        // 2. Decompress LightValues
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

        // Visualize received light data on GPU
        if (LightingManager.Instance != null)
        {
            LightingManager.Instance.SyncChunkLight(cx, cy, chunk.lightValues);
        }

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

        if (IsServer && LightingManager.Instance != null)
        {
            yield return StartCoroutine(LightingManager.Instance.CalculateAllLightingCo());
        }
    }

    #endregion

    #region Block Operation

    /// <summary>
    /// Entry point for block changes. Handles prediction for local player and validation for server.
    /// </summary>
    public void SetBlock(int worldX, int worldY, int id)
    {
        // 1. Client-Side Prediction: Apply change immediately on the requester's side
        InternalSetBlock(worldX, worldY, id);

        // 2. Network Request
        if (IsServer)
        {
            // If server performs action (e.g. via world event), notify all clients
            SetBlockClientRpc(worldX, worldY, id);
        }
        else
        {
            // If client performs action, request server to validate and sync
            SetBlockServerRpc(worldX, worldY, id);
        }
    }

    [ServerRpc(RequireOwnership = false)]
    private void SetBlockServerRpc(int worldX, int worldY, int id, ServerRpcParams rpcParams = default)
    {
        // 1. Apply change to server's master MapData
        InternalSetBlock(worldX, worldY, id);

        // 2. Selective Sync: Notify other clients only
        // Exclude the sender because they already predicted the change locally
        SetBlockClientRpc(worldX, worldY, id, new ClientRpcParams
        {
            Send = new ClientRpcSendParams { TargetClientIds = null } // Default logic should handle exclusion if we filter in RPC
        });
    }

    [ClientRpc]
    private void SetBlockClientRpc(int worldX, int worldY, int id, ClientRpcParams clientRpcParams = default)
    {
        if (IsServer) return;

        // In a real scenario, we'd use TargetClientIds to skip the requester.
        // For now, InternalSetBlock has a guard: "if (same block) return", 
        // which prevents redundant redraws even if the packet arrives.
        InternalSetBlock(worldX, worldY, id);
    }

    private void InternalSetBlock(int worldX, int worldY, int id)
    {
        if (activeMapData == null) return;
        int width = ChunkData.Size;
        int height = ChunkData.Size;

        int cx = Mathf.FloorToInt((float)worldX / width);
        int cy = Mathf.FloorToInt((float)worldY / height);
        int lx = worldX - (cx * width);
        int ly = worldY - (cy * height);

        if (cx < 0 || cx >= activeMapData.mapSize.x || cy < 0 || cy >= activeMapData.mapSize.y) return;

        ChunkData chunk = activeMapData.chunks[cx, cy];
        int idx = ChunkData.GetIndex(lx, ly);

        // Don't redraw if it's the same block (optimization)
        if (chunk.blocks[idx].isActive && chunk.blocks[idx].id == id && id >= 0) return;
        if (!chunk.blocks[idx].isActive && id < 0) return;

        if (id < 0) chunk.blocks[idx] = default;
        else
        {
            int maxKinds = ResourceManager.Instance != null ? ResourceManager.Instance.GetTileKindCount(id) : 1;
            chunk.blocks[idx] = new BlockData(id, UnityEngine.Random.Range(0, maxKinds), true);
        }

        // Lighting & Mesh update
        if (LightingManager.Instance != null) LightingManager.Instance.UpdateLightingAt(worldX, worldY);
        if (MeshManager.Instance != null)
        {
            // 1. Current Chunk
            MeshManager.Instance.RequestChunkRedraw(cx, cy);

            // 2. Neighbor Chunks (Including Diagonals for Bitmask)
            bool isL = (lx == 0); bool isR = (lx == width - 1);
            bool isB = (ly == 0); bool isT = (ly == height - 1);

            if (isL) MeshManager.Instance.RequestChunkRedraw(cx - 1, cy);
            if (isR) MeshManager.Instance.RequestChunkRedraw(cx + 1, cy);
            if (isB) MeshManager.Instance.RequestChunkRedraw(cx, cy - 1);
            if (isT) MeshManager.Instance.RequestChunkRedraw(cx, cy + 1);

            // Diagonal neighbors for accurate diagonal bitmask
            if (isL && isB) MeshManager.Instance.RequestChunkRedraw(cx - 1, cy - 1);
            if (isL && isT) MeshManager.Instance.RequestChunkRedraw(cx - 1, cy + 1);
            if (isR && isB) MeshManager.Instance.RequestChunkRedraw(cx + 1, cy - 1);
            if (isR && isT) MeshManager.Instance.RequestChunkRedraw(cx + 1, cy + 1);
        }
    }

    #endregion

    #region Utility

    public Vector2 GetSurfacePosition(float ratioX)
    {
        if (activeMapData == null) return Vector2.zero;
        int totalWidth = activeMapData.mapSize.x * ChunkData.ChunkSize.x;
        int worldX = Mathf.Clamp(Mathf.FloorToInt((ratioX / 100f) * totalWidth), 0, totalWidth - 1);
        int cx = worldX / ChunkData.ChunkSize.x;
        int lx = worldX % ChunkData.ChunkSize.x;

        for (int cy = activeMapData.mapSize.y - 1; cy >= 0; cy--)
        {
            ChunkData chunk = activeMapData.chunks[cx, cy];
            for (int ly = ChunkData.ChunkSize.y - 1; ly >= 0; ly--)
            {
                if (chunk.blocks[ChunkData.GetIndex(lx, ly)].isActive) return new Vector2(worldX + 0.5f, (cy * 8) + ly + 2f);
            }
        }
        return new Vector2(worldX + 0.5f, (activeMapData.mapSize.y * 8) * 0.6f + 5f);
    }

    public bool IsMapReady() => isMapReady;

    #endregion
}
