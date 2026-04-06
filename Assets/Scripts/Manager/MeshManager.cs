using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;
using Unity.Collections;
using Unity.Jobs;
using Unity.Burst;
using Unity.Mathematics;

public class MeshManager : Singleton<MeshManager>
{
    #region Variable

    [Header("### Map")]
    [Header("## Tile")]
    [Header("# Render")]
    [SerializeField] private Material tileMaterial;

    [Header("## Chunk")]
    [Header("# Sliding+Pool")]
    [SerializeField] private bool useSlidingWindow = true;
    [SerializeField] private Transform targetTransform;
    [SerializeField] private int viewDistanceX = 8;
    [SerializeField] private int viewDistanceY = 5;
    [SerializeField] private int viewBuffer = 1; // Minimum extra chunks beyond screen
    [SerializeField] private float updateInterval = 0.5f;
    [SerializeField] private float moveThreshold = 0.5f; 

    [Header("# Performance Tuning")]
    [SerializeField] private int baseMaxChunkBuilds = 2;
    [SerializeField] private int baseMaxColliderBuilds = 1;
    [SerializeField] private int emergencyBuildThreshold = 10; 

    private float lastUpdateTime = 0f;
    private Camera mainCam;
    private float lastOrthographicSize;
    private int lastScreenWidth;
    private int lastScreenHeight;

    private List<PlayerController> trackedPlayers = new List<PlayerController>();
    private Dictionary<PlayerController, Vector3> lastPlayerPositions = new Dictionary<PlayerController, Vector3>();
    private float playerListRefreshTimer = 0f;
    private const float PLAYER_LIST_REFRESH_INTERVAL = 1.0f;

    private Stack<MeshFilter> chunkPool = new Stack<MeshFilter>();
    private Dictionary<Vector2Int, MeshFilter> activeChunks = new Dictionary<Vector2Int, MeshFilter>();
    private Dictionary<Vector2Int, List<GameObject>> activeEdges = new Dictionary<Vector2Int, List<GameObject>>();
    private Stack<GameObject> edgePool = new Stack<GameObject>();

    private HashSet<Vector2Int> lastRequiredChunks = new HashSet<Vector2Int>();
    private HashSet<Vector2Int> currentRequiredChunks = new HashSet<Vector2Int>(); 
    private List<Vector2Int> toRemoveCache = new List<Vector2Int>(); 
    private bool lastSlidingState = true;

    private List<Vector3> cachedVertices = new List<Vector3>(1024);
    private List<int> cachedTriangles = new List<int>(2048);
    private List<Vector3> cachedUVs = new List<Vector3>(1024);
    private List<Color> cachedColors = new List<Color>(1024);
    private List<Vector2> cachedEdgePoints = new List<Vector2>(2); 

    // Job Data
    private NativeArray<int> ruleMappingArray;

    // Build Queues (Reverted to original names and types for compatibility)
    private Queue<Vector2Int> pendingDrawQueue = new Queue<Vector2Int>();
    private HashSet<Vector2Int> pendingDrawSet = new HashSet<Vector2Int>();
    private Queue<Vector2Int> pendingColliderQueue = new Queue<Vector2Int>();
    private HashSet<Vector2Int> pendingColliderSet = new HashSet<Vector2Int>();

    #endregion

    #region MonoBehaviour

    protected override void Awake()
    {
        base.Awake();
        cachedVertices.Clear();
        cachedTriangles.Clear();
        cachedUVs.Clear();
        cachedColors.Clear();

        mainCam = Camera.main;

        // Initialize rule mapping for Jobs
        int[] rawMapping = TileSpriteSet.GetRawMappingArray();
        ruleMappingArray = new NativeArray<int>(256, Allocator.Persistent);
        ruleMappingArray.CopyFrom(rawMapping);
    }

    private void Start()
    {
        lastSlidingState = useSlidingWindow;
        
        RefreshResolutionSettings();

        int initialPoolSize = 100;

        if (useSlidingWindow)
        {
            initialPoolSize = (viewDistanceX * 2 + 1) * (viewDistanceY * 2 + 1) * 2; 
        }
        else if (MapManager.Instance != null && MapManager.Instance.activeMapData != null)
        {
            Vector2Int size = MapManager.Instance.activeMapData.mapSize;
            if (size.x > 0 && size.y > 0)
                initialPoolSize = size.x * size.y;
        }

        if (initialPoolSize <= 0) initialPoolSize = 25; 

        for (int i = 0; i < initialPoolSize; i++)
        {
            chunkPool.Push(CreateChunkObject(i + chunkPool.Count));
        }

        if (!useSlidingWindow)
            RefreshAllChunks();
    }

    private void Update()
    {
        if (useSlidingWindow != lastSlidingState)
        {
            if (useSlidingWindow)
            {
                lastRequiredChunks.Clear(); 
                UpdateSlidingWindow();
            }
            else
            {
                RefreshAllChunks();
            }
            lastSlidingState = useSlidingWindow;
        }

        if (useSlidingWindow)
        {
            UpdateDynamicViewDistances();
            UpdateSlidingWindow();
        }
    }

    private void LateUpdate()
    {
        // Adaptive Build Speed
        int currentMaxBuilds = baseMaxChunkBuilds;
        if (pendingDrawQueue.Count > emergencyBuildThreshold)
            currentMaxBuilds = baseMaxChunkBuilds + (pendingDrawQueue.Count / 5);

        int processedMesh = 0;
        while (processedMesh < currentMaxBuilds && pendingDrawQueue.Count > 0)
        {
            Vector2Int coord = pendingDrawQueue.Dequeue();
            pendingDrawSet.Remove(coord);
            
            if (activeChunks.TryGetValue(coord, out MeshFilter filter))
            {
                DrawChunk(filter, coord.x, coord.y);
                EnqueueColliderBuild(coord);
            }
            processedMesh++;
        }

        int currentMaxColliders = baseMaxColliderBuilds;
        if (pendingColliderQueue.Count > emergencyBuildThreshold)
            currentMaxColliders = baseMaxColliderBuilds + (pendingColliderQueue.Count / 5);

        int processedCollider = 0;
        while (processedCollider < currentMaxColliders && pendingColliderQueue.Count > 0)
        {
            Vector2Int coord = pendingColliderQueue.Dequeue();
            pendingColliderSet.Remove(coord);

            if (activeChunks.ContainsKey(coord))
                UpdateChunkCollider(coord.x, coord.y);
            
            processedCollider++;
        }

        if (redrawQueue.Count > 0)
        {
            foreach (var coord in redrawQueue) EnqueueChunkBuild(coord);
            redrawQueue.Clear();
        }
    }

    private void OnDestroy()
    {
        if (ruleMappingArray.IsCreated) ruleMappingArray.Dispose();
    }

    #endregion

    #region Resolution & ViewDistance

    public void RefreshResolutionSettings()
    {
        if (mainCam == null) mainCam = Camera.main;
        if (mainCam == null) return;

        lastOrthographicSize = mainCam.orthographicSize;
        lastScreenWidth = Screen.width;
        lastScreenHeight = Screen.height;

        float verticalSize = mainCam.orthographicSize * 2f;
        float horizontalSize = verticalSize * mainCam.aspect;

        viewDistanceY = Mathf.CeilToInt((verticalSize / 2f) / ChunkData.Size) + viewBuffer;
        viewDistanceX = Mathf.CeilToInt((horizontalSize / 2f) / ChunkData.Size) + viewBuffer;

        viewDistanceX = Mathf.Max(viewDistanceX, 2);
        viewDistanceY = Mathf.Max(viewDistanceY, 2);
    }

    private void UpdateDynamicViewDistances()
    {
        if (Mathf.Approximately(mainCam.orthographicSize, lastOrthographicSize) && 
            Screen.width == lastScreenWidth && Screen.height == lastScreenHeight)
            return;

        RefreshResolutionSettings();
    }

    #endregion

    #region Sliding

    public void SetTarget(Transform target)
    {
        targetTransform = target;
    }

    public void ForceRenderChunk(int cx, int cy)
    {
        if (MapManager.Instance == null || MapManager.Instance.activeMapData == null) return;
        Vector2Int coord = new Vector2Int(cx, cy);
        
        if (!activeChunks.ContainsKey(coord))
        {
            ActivateChunk(coord, enqueueBuild: true);
            lastRequiredChunks.Add(coord);
        }
    }

    private void UpdateSlidingWindow()
    {
        if (MapManager.Instance == null || MapManager.Instance.activeMapData == null) return;
        if (NetworkManager.Singleton == null || !NetworkManager.Singleton.IsClient) return;

        if (Time.time - lastUpdateTime < updateInterval) return;
        lastUpdateTime = Time.time;

        playerListRefreshTimer += updateInterval;
        if (playerListRefreshTimer >= PLAYER_LIST_REFRESH_INTERVAL || trackedPlayers.Count == 0)
        {
            playerListRefreshTimer = 0f;
            PlayerController[] foundPlayers = Object.FindObjectsByType<PlayerController>(FindObjectsSortMode.None);
            trackedPlayers.Clear();
            if (foundPlayers != null)
            {
                foreach (var p in foundPlayers) if (p != null) trackedPlayers.Add(p);
            }
        }

        if (trackedPlayers.Count == 0) return;

        bool hasSignificantMovement = false;
        foreach (var player in trackedPlayers)
        {
            if (player == null || !player.IsSpawned) continue;
            
            Vector3 currentPos = player.transform.position;
            if (!lastPlayerPositions.TryGetValue(player, out Vector3 lastPos) || 
                Vector3.Distance(currentPos, lastPos) > moveThreshold)
            {
                hasSignificantMovement = true;
                lastPlayerPositions[player] = currentPos;
            }
        }

        if (!hasSignificantMovement && lastRequiredChunks.Count > 0) return;

        currentRequiredChunks.Clear();
        MapData data = MapManager.Instance.activeMapData;

        foreach (var player in trackedPlayers)
        {
            if (player == null || !player.IsSpawned) continue;

            Vector3 pos = player.transform.position;
            int cx = Mathf.FloorToInt(pos.x / ChunkData.ChunkSize.x);
            int cy = Mathf.FloorToInt(pos.y / ChunkData.ChunkSize.y);

            for (int x = -viewDistanceX; x <= viewDistanceX; x++)
            {
                for (int y = -viewDistanceY; y <= viewDistanceY; y++)
                {
                    Vector2Int coord = new Vector2Int(cx + x, cy + y);
                    if (coord.x >= 0 && coord.x < data.mapSize.x &&
                        coord.y >= 0 && coord.y < data.mapSize.y)
                    {
                        currentRequiredChunks.Add(coord);
                    }
                }
            }
        }

        if (!currentRequiredChunks.SetEquals(lastRequiredChunks))
        {
            RefreshChunksMultitarget(currentRequiredChunks);
            lastRequiredChunks.Clear();
            foreach (var coord in currentRequiredChunks) lastRequiredChunks.Add(coord);
        }
    }

    private void DeactivateChunk(Vector2Int coord, MeshFilter filter)
    {
        ClearChunkEdges(coord, filter.gameObject);
        filter.gameObject.SetActive(false);
        chunkPool.Push(filter);
        activeChunks.Remove(coord);
    }

    private void ClearChunkEdges(Vector2Int coord, GameObject chunkObj)
    {
        HashSet<GameObject> processed = new HashSet<GameObject>();
        if (activeEdges.TryGetValue(coord, out List<GameObject> edges))
        {
            foreach (var edgeObj in edges)
            {
                if (edgeObj == null) continue;
                edgeObj.SetActive(false);
                edgePool.Push(edgeObj);
                processed.Add(edgeObj);
            }
            edges.Clear();
        }
    }

    private void RefreshChunksMultitarget(HashSet<Vector2Int> requiredCoords)
    {
        toRemoveCache.Clear();
        foreach (var activeCoord in activeChunks.Keys)
        {
            if (!requiredCoords.Contains(activeCoord)) toRemoveCache.Add(activeCoord);
        }

        foreach (var coord in toRemoveCache) DeactivateChunk(coord, activeChunks[coord]);
        foreach (var coord in requiredCoords) ActivateChunk(coord, enqueueBuild: true);
    }

    public void ClearAllChunks()
    {
        foreach (var entry in activeChunks) DeactivateChunk(entry.Key, entry.Value);
        activeChunks.Clear();
        lastRequiredChunks.Clear();
    }

    public void RefreshAllChunks()
    {
        if (MapManager.Instance == null || MapManager.Instance.activeMapData == null) return;
        MapData data = MapManager.Instance.activeMapData;
        for (int x = 0; x < data.mapSize.x; x++)
        {
            for (int y = 0; y < data.mapSize.y; y++) ActivateChunk(new Vector2Int(x, y), false);
        }
    }

    private void ActivateChunk(Vector2Int coord, bool enqueueBuild = true)
    {
        if (activeChunks.ContainsKey(coord)) return;
        if (!MapManager.Instance.IsChunkSynced(coord.x, coord.y)) MapManager.Instance.RequestChunkSync(coord.x, coord.y);

        MeshFilter filter = (chunkPool.Count > 0) ? chunkPool.Pop() : CreateChunkObject(activeChunks.Count + chunkPool.Count);
        filter.gameObject.SetActive(true);
        activeChunks.Add(coord, filter);
        if (enqueueBuild) EnqueueChunkBuild(coord);
    }

    #endregion

    #region Draw (Job System + Burst)

    [BurstCompile]
    public struct ChunkMeshJob : IJob
    {
        [ReadOnly] public NativeArray<BlockData> blocks; 
        [ReadOnly] public NativeArray<int> ruleMapping;
        public int worldOffsetX;
        public int worldOffsetY;
        public NativeList<float3> vertices;
        public NativeList<int> triangles;
        public NativeList<float3> uvs;

        public void Execute()
        {
            int sizeWithPadding = ChunkData.Size + 2; 
            for (int y = 0; y < ChunkData.Size; y++)
            {
                for (int x = 0; x < ChunkData.Size; x++)
                {
                    int lx = x + 1; int ly = y + 1;
                    int idx = ly * sizeWithPadding + lx;
                    BlockData block = blocks[idx];
                    if (!block.isActive) continue;

                    int bitmask = 0;
                    if (blocks[idx + sizeWithPadding].isActive) bitmask |= (1 << 0);
                    if (blocks[idx - 1].isActive) bitmask |= (1 << 1);
                    if (blocks[idx + 1].isActive) bitmask |= (1 << 2);
                    if (blocks[idx - sizeWithPadding].isActive) bitmask |= (1 << 3);

                    bool hasT = (bitmask & (1 << 0)) != 0;
                    bool hasL = (bitmask & (1 << 1)) != 0;
                    bool hasR = (bitmask & (1 << 2)) != 0;
                    bool hasB = (bitmask & (1 << 3)) != 0;

                    if (hasT && hasL && !blocks[idx + sizeWithPadding - 1].isActive) bitmask |= (1 << 4);
                    if (hasT && hasR && !blocks[idx + sizeWithPadding + 1].isActive) bitmask |= (1 << 5);
                    if (hasB && hasL && !blocks[idx - sizeWithPadding - 1].isActive) bitmask |= (1 << 6);
                    if (hasB && hasR && !blocks[idx - sizeWithPadding + 1].isActive) bitmask |= (1 << 7);

                    int ruleId = ruleMapping[bitmask & 0xFF];
                    int variation = block.kindId % 3;
                    float arrayIdx = (block.id * 141) + (ruleId * 3) + variation;

                    int vIndex = vertices.Length;
                    float wx = worldOffsetX + x; float wy = worldOffsetY + y;
                    vertices.Add(new float3(wx, wy, 0));
                    vertices.Add(new float3(wx + 1, wy, 0));
                    vertices.Add(new float3(wx, wy + 1, 0));
                    vertices.Add(new float3(wx + 1, wy + 1, 0));
                    triangles.Add(vIndex + 0); triangles.Add(vIndex + 2); triangles.Add(vIndex + 3);
                    triangles.Add(vIndex + 0); triangles.Add(vIndex + 3); triangles.Add(vIndex + 1);
                    uvs.Add(new float3(0, 0, arrayIdx)); uvs.Add(new float3(1, 0, arrayIdx));
                    uvs.Add(new float3(0, 1, arrayIdx)); uvs.Add(new float3(1, 1, arrayIdx));
                }
            }
        }
    }

    private void DrawChunk(MeshFilter targetFilter, int cx, int cy)
    {
        if (MapManager.Instance.activeMapData == null) return;
        MapData data = MapManager.Instance.activeMapData;
        
        ChunkData[,] neighborChunks = new ChunkData[3, 3];
        for (int y = -1; y <= 1; y++)
        {
            for (int x = -1; x <= 1; x++)
            {
                int nx = cx + x; int ny = cy + y;
                if (nx >= 0 && nx < data.mapSize.x && ny >= 0 && ny < data.mapSize.y)
                    neighborChunks[x + 1, y + 1] = data.chunks[nx, ny];
            }
        }

        int sizeWithPadding = ChunkData.Size + 2;
        NativeArray<BlockData> blockData = new NativeArray<BlockData>(sizeWithPadding * sizeWithPadding, Allocator.TempJob);

        for (int y = -1; y <= ChunkData.Size; y++)
        {
            for (int x = -1; x <= ChunkData.Size; x++)
            {
                int jobIdx = (y + 1) * sizeWithPadding + (x + 1);
                int tx = x, ty = y, ncx = 1, ncy = 1;
                if (tx < 0) { tx += ChunkData.Size; ncx = 0; } 
                else if (tx >= ChunkData.Size) { tx -= ChunkData.Size; ncx = 2; }
                if (ty < 0) { ty += ChunkData.Size; ncy = 0; } 
                else if (ty >= ChunkData.Size) { ty -= ChunkData.Size; ncy = 2; }

                ChunkData targetChunk = neighborChunks[ncx, ncy];
                blockData[jobIdx] = (targetChunk != null) ? targetChunk.blocks[ChunkData.GetIndex(tx, ty)] : default;
            }
        }

        NativeList<float3> vertices = new NativeList<float3>(256, Allocator.TempJob);
        NativeList<int> triangles = new NativeList<int>(512, Allocator.TempJob);
        NativeList<float3> uvs = new NativeList<float3>(256, Allocator.TempJob);

        ChunkMeshJob job = new ChunkMeshJob { blocks = blockData, ruleMapping = ruleMappingArray, worldOffsetX = cx * ChunkData.Size, worldOffsetY = cy * ChunkData.Size, vertices = vertices, triangles = triangles, uvs = uvs };
        job.Schedule().Complete(); 

        Mesh mesh = targetFilter.sharedMesh;
        if (mesh == null) { mesh = new Mesh(); mesh.name = $"Chunk_{cx}_{cy}"; mesh.MarkDynamic(); targetFilter.sharedMesh = mesh; }
        else mesh.Clear();

        if (vertices.Length > 0)
        {
            mesh.SetVertices(vertices.AsArray());
            mesh.SetTriangles(triangles.AsArray().ToArray(), 0);
            mesh.SetUVs(0, uvs.AsArray());
            mesh.RecalculateBounds();
        }

        blockData.Dispose(); vertices.Dispose(); triangles.Dispose(); uvs.Dispose();
    }

    #endregion

    #region Physics

    public void UpdateChunkCollider(int cx, int cy)
    {
        Vector2Int coord = new Vector2Int(cx, cy);
        if (!activeChunks.TryGetValue(coord, out MeshFilter filter)) return;
        
        ChunkData[,] allChunks = MapManager.Instance.activeMapData.chunks;
        ChunkData chunk = allChunks[cx, cy];
        if (chunk == null) return;

        MapData data = MapManager.Instance.activeMapData;
        ChunkData chunkU = (cy + 1 < data.mapSize.y) ? allChunks[cx, cy + 1] : null;
        ChunkData chunkD = (cy - 1 >= 0) ? allChunks[cx, cy - 1] : null;
        ChunkData chunkL = (cx - 1 >= 0) ? allChunks[cx - 1, cy] : null;
        ChunkData chunkR = (cx + 1 < data.mapSize.x) ? allChunks[cx + 1, cy] : null;

        GameObject chunkObj = filter.gameObject;
        if (!activeEdges.TryGetValue(coord, out List<GameObject> edges)) { edges = new List<GameObject>(); activeEdges.Add(coord, edges); }
        foreach (var edgeObj in edges) { edgeObj.SetActive(false); edgePool.Push(edgeObj); }
        edges.Clear();

        int width = ChunkData.ChunkSize.x; int height = ChunkData.ChunkSize.y;
        for (int y = 0; y <= height; y++)
        {
            ExtractHorizontalSegments(chunkObj, coord, y, true, chunk, chunkU, chunkD, edges);
            ExtractHorizontalSegments(chunkObj, coord, y, false, chunk, chunkU, chunkD, edges);
        }
        for (int x = 0; x <= width; x++)
        {
            ExtractVerticalSegments(chunkObj, coord, x, true, chunk, chunkL, chunkR, edges);
            ExtractVerticalSegments(chunkObj, coord, x, false, chunk, chunkL, chunkR, edges);
        }
    }

    private void ExtractHorizontalSegments(GameObject parent, Vector2Int coord, int y, bool isTop, ChunkData c, ChunkData uC, ChunkData dC, List<GameObject> chunkEdges)
    {
        int width = ChunkData.ChunkSize.x; int height = ChunkData.ChunkSize.y; int startX = -1;
        for (int x = 0; x < width; x++)
        {
            bool hasFace = false;
            if (isTop)
            {
                bool blockExists = (y < height) && (c.blocks[ChunkData.GetIndex(x, y)].isActive);
                bool neighborExists = (y + 1 < height) ? (c.blocks[ChunkData.GetIndex(x, y + 1)].isActive) : (uC != null && uC.blocks[ChunkData.GetIndex(x, 0)].isActive);
                if (blockExists && !neighborExists) hasFace = true;
            }
            else
            {
                bool blockExists = (y < height) && (c.blocks[ChunkData.GetIndex(x, y)].isActive);
                bool neighborExists = (y - 1 >= 0) ? (c.blocks[ChunkData.GetIndex(x, y - 1)].isActive) : (dC != null && dC.blocks[ChunkData.GetIndex(x, height - 1)].isActive);
                if (blockExists && !neighborExists) hasFace = true;
            }
            if (hasFace) { if (startX == -1) startX = x; }
            else { if (startX != -1) { GetOrCreateEdge(parent, coord, startX, y + (isTop ? 1 : 0), x, y + (isTop ? 1 : 0), chunkEdges); startX = -1; } }
        }
        if (startX != -1) GetOrCreateEdge(parent, coord, startX, y + (isTop ? 1 : 0), width, y + (isTop ? 1 : 0), chunkEdges);
    }

    private void ExtractVerticalSegments(GameObject parent, Vector2Int coord, int x, bool isLeft, ChunkData c, ChunkData lC, ChunkData rC, List<GameObject> chunkEdges)
    {
        int width = ChunkData.ChunkSize.x; int height = ChunkData.ChunkSize.y; int startY = -1;
        for (int y = 0; y < height; y++)
        {
            bool hasFace = false;
            if (isLeft)
            {
                bool blockExists = (x < width) && (c.blocks[ChunkData.GetIndex(x, y)].isActive);
                bool neighborExists = (x > 0) ? c.blocks[ChunkData.GetIndex(x - 1, y)].isActive : (lC != null && lC.blocks[ChunkData.GetIndex(width - 1, y)].isActive);
                if (blockExists && !neighborExists) hasFace = true;
            }
            else
            {
                bool blockExists = (x < width) && (c.blocks[ChunkData.GetIndex(x, y)].isActive);
                bool neighborExists = (x + 1 < width) ? c.blocks[ChunkData.GetIndex(x + 1, y)].isActive : (rC != null && rC.blocks[ChunkData.GetIndex(0, y)].isActive);
                if (blockExists && !neighborExists) hasFace = true;
            }
            if (hasFace) { if (startY == -1) startY = y; }
            else { if (startY != -1) { GetOrCreateEdge(parent, coord, x + (isLeft ? 0 : 1), startY, x + (isLeft ? 0 : 1), y, chunkEdges); startY = -1; } }
        }
        if (startY != -1) GetOrCreateEdge(parent, coord, x + (isLeft ? 0 : 1), startY, x + (isLeft ? 0 : 1), height, chunkEdges);
    }

    private void GetOrCreateEdge(GameObject parent, Vector2Int coord, float x1, float y1, float x2, float y2, List<GameObject> chunkEdges)
    {
        GameObject edgeObj = (edgePool.Count > 0) ? edgePool.Pop() : new GameObject("Edge_Optimized");
        edgeObj.SetActive(true); edgeObj.transform.SetParent(parent.transform); edgeObj.layer = LayerMask.NameToLayer("Ground");
        if (edgeObj.GetComponent<EdgeCollider2D>() == null) edgeObj.AddComponent<EdgeCollider2D>();
        edgeObj.transform.localPosition = Vector3.zero; chunkEdges.Add(edgeObj);
        EdgeCollider2D edge = edgeObj.GetComponent<EdgeCollider2D>();
        float worldOffsetX = coord.x * ChunkData.Size; float worldOffsetY = coord.y * ChunkData.Size;
        cachedEdgePoints.Clear(); cachedEdgePoints.Add(new Vector2(worldOffsetX + x1, worldOffsetY + y1)); cachedEdgePoints.Add(new Vector2(worldOffsetX + x2, worldOffsetY + y2));
        edge.SetPoints(cachedEdgePoints);
    }

    #endregion

    #region Chunk Utility

    private MeshFilter CreateChunkObject(int index)
    {
        GameObject chunkObj = new GameObject($"Chunk_Pool_{index}");
        chunkObj.transform.SetParent(this.transform);
        int layer = LayerMask.NameToLayer("Ground");
        if (layer == -1) layer = 0;
        chunkObj.layer = layer;
        chunkObj.SetActive(false);
        MeshFilter mf = chunkObj.AddComponent<MeshFilter>();
        MeshRenderer mr = chunkObj.AddComponent<MeshRenderer>();
        mr.sharedMaterial = tileMaterial; mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; mr.receiveShadows = false;
        mr.sortingLayerName = "Default"; mr.sortingOrder = 0; 
        return mf;
    }

    public void RequestChunkRedraw(int cx, int cy) { if (cx >= 0 && cy >= 0) redrawQueue.Add(new Vector2Int(cx, cy)); }

    private HashSet<Vector2Int> redrawQueue = new HashSet<Vector2Int>();

    private void EnqueueChunkBuild(Vector2Int coord) { if (pendingDrawSet.Add(coord)) pendingDrawQueue.Enqueue(coord); }
    private void EnqueueColliderBuild(Vector2Int coord) { if (pendingColliderSet.Add(coord)) pendingColliderQueue.Enqueue(coord); }

    public bool IsChunkActive(int cx, int cy) => activeChunks.ContainsKey(new Vector2Int(cx, cy));

    #endregion
}
