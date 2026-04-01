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
    public static readonly Vector2Int ChunkSize = new Vector2Int(8, 8);
    public BlockData[,] blocks;
    public byte[,] lightValues;
    public bool isSynced; // Local flag for clients

    public ChunkData()
    {
        blocks = new BlockData[ChunkSize.x, ChunkSize.y];
        lightValues = new byte[ChunkSize.x, ChunkSize.y];
        isSynced = false;
    }
}

[Serializable]
public class MapData
{
    public static readonly Vector2Int StandardMapSize = new Vector2Int(300, 240);
    public static readonly Vector2Int GreatCaveMapSize = new Vector2Int(400, 200);
    public static readonly Vector2Int HellMapSize = new Vector2Int(240, 400);

    // 곧 사용할 것. 지우기 X
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
        
        // Flatten 2D to 1D for network transport (NGO doesn't support 2D arrays directly)
        BlockData[] blockArray = new BlockData[64];
        byte[] lightArray = new byte[64];

        for (int y = 0; y < 8; y++)
        {
            for (int x = 0; x < 8; x++)
            {
                int idx = y * 8 + x;
                blockArray[idx] = chunk.blocks[x, y];
                lightArray[idx] = chunk.lightValues[x, y];
            }
        }

        // Send back to the specific client who requested it
        DeliverChunkClientRpc(cx, cy, blockArray, lightArray, new ClientRpcParams { Send = new ClientRpcSendParams { TargetClientIds = new[] { rpcParams.Receive.SenderClientId } } });
    }

    [ClientRpc]
    private void DeliverChunkClientRpc(int cx, int cy, BlockData[] blockArray, byte[] lightArray, ClientRpcParams clientRpcParams = default)
    {
        if (IsServer) return;
        if (activeMapData == null) return;

        ChunkData chunk = activeMapData.chunks[cx, cy];
        for (int i = 0; i < 64; i++)
        {
            int x = i % 8;
            int y = i / 8;
            chunk.blocks[x, y] = blockArray[i];
            chunk.lightValues[x, y] = lightArray[i];
        }

        chunk.isSynced = true;
        requestedChunks.Remove(new Vector2Int(cx, cy));

        // Notify MeshManager to draw this chunk and its 4 neighbors
        // Neighbors need to redraw because they now have data to connect to this new chunk.
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

    public void SetBlock(int worldX, int worldY, int id)
    {
        InternalSetBlock(worldX, worldY, id);
        if (IsServer) SetBlockClientRpc(worldX, worldY, id);
    }

    [ClientRpc]
    private void SetBlockClientRpc(int worldX, int worldY, int id) { if (!IsServer) InternalSetBlock(worldX, worldY, id); }

    private void InternalSetBlock(int worldX, int worldY, int id)
    {
        if (activeMapData == null) return;
        int width = ChunkData.ChunkSize.x;
        int height = ChunkData.ChunkSize.y;
        int cx = worldX / width; int cy = worldY / height;
        int lx = worldX % width; int ly = worldY % height;
        if (lx < 0) { lx += width; cx--; } if (ly < 0) { ly += height; cy--; }
        if (cx < 0 || cx >= activeMapData.mapSize.x || cy < 0 || cy >= activeMapData.mapSize.y) return;

        ChunkData chunk = activeMapData.chunks[cx, cy];
        if (id < 0) chunk.blocks[lx, ly] = default;
        else
        {
            int maxKinds = ResourceManager.Instance != null ? ResourceManager.Instance.GetTileKindCount(id) : 1;
            chunk.blocks[lx, ly] = new BlockData(id, UnityEngine.Random.Range(0, maxKinds), true);
        }

        if (LightingManager.Instance != null) LightingManager.Instance.UpdateLightingAt(worldX, worldY);
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
                if (chunk.blocks[lx, ly].isActive) return new Vector2(worldX + 0.5f, (cy * 8) + ly + 2f);
            }
        }
        return new Vector2(worldX + 0.5f, (activeMapData.mapSize.y * 8) * 0.6f + 5f);
    }

    public bool IsMapReady() => isMapReady;

    #endregion
}
