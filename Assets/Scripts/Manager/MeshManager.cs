using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

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
    [SerializeField] private int viewDistance = 2; 
    [SerializeField] private float updateInterval = 0.1f; // Run update 10 times per second instead of every frame
    [SerializeField] private float moveThreshold = 0.5f; // Minimum distance player must move to trigger update

    private float lastUpdateTime = 0f;
    private List<PlayerController> trackedPlayers = new List<PlayerController>();
    private Dictionary<PlayerController, Vector3> lastPlayerPositions = new Dictionary<PlayerController, Vector3>();
    private float playerListRefreshTimer = 0f;
    private const float PLAYER_LIST_REFRESH_INTERVAL = 1.0f;

    private Stack<MeshFilter> chunkPool = new Stack<MeshFilter>();
    private Dictionary<Vector2Int, MeshFilter> activeChunks = new Dictionary<Vector2Int, MeshFilter>();
    private Dictionary<Vector2Int, List<GameObject>> activeEdges = new Dictionary<Vector2Int, List<GameObject>>();
    private Stack<GameObject> edgePool = new Stack<GameObject>();

    private HashSet<Vector2Int> lastRequiredChunks = new HashSet<Vector2Int>();
    private HashSet<Vector2Int> currentRequiredChunks = new HashSet<Vector2Int>(); // Added for reuse
    private List<Vector2Int> toRemoveCache = new List<Vector2Int>(); // Added for reuse
    private bool lastSlidingState = true;

    private List<Vector3> cachedVertices = new List<Vector3>(1024);
    private List<int> cachedTriangles = new List<int>(2048);
    private List<Vector3> cachedUVs = new List<Vector3>(1024);
    private List<Color> cachedColors = new List<Color>(1024);
    private List<Vector2> cachedEdgePoints = new List<Vector2>(2); // Added for EdgeCollider optimization

    private bool[,] neighborStatesCache; // Added for reuse

    #endregion

    #region MonoBehaviour

    protected override void Awake()
    {
        base.Awake();
        cachedVertices.Clear();
        cachedTriangles.Clear();
        cachedUVs.Clear();
        cachedColors.Clear();
    }


    private void Start()
    {
        lastSlidingState = useSlidingWindow;

        int initialPoolSize = 100;

        if (useSlidingWindow)
        {
            initialPoolSize = (viewDistance * 2 + 1) * (viewDistance * 2 + 1) * 2; 
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
            UpdateSlidingWindow();
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
        
        // Immediately activate and draw
        if (!activeChunks.ContainsKey(coord))
        {
            ActivateChunk(coord, true);
            lastRequiredChunks.Add(coord); // Add to tracking so sliding window doesn't prune it immediately
        }
    }

    private void UpdateSlidingWindow()
    {
        if (MapManager.Instance == null || MapManager.Instance.activeMapData == null) return;
        if (NetworkManager.Singleton == null || !NetworkManager.Singleton.IsClient) return;

        // 1. Frequency Control (Timer)
        if (Time.time - lastUpdateTime < updateInterval) return;
        lastUpdateTime = Time.time;

        // 2. Refresh Player List (Less frequent)
        playerListRefreshTimer += updateInterval;
        if (playerListRefreshTimer >= PLAYER_LIST_REFRESH_INTERVAL || trackedPlayers.Count == 0)
        {
            playerListRefreshTimer = 0f;
            PlayerController[] foundPlayers = Object.FindObjectsByType<PlayerController>(FindObjectsSortMode.None);
            trackedPlayers.Clear();
            if (foundPlayers != null)
            {
                foreach (var p in foundPlayers)
                {
                    if (p != null) trackedPlayers.Add(p);
                }
            }
        }

        if (trackedPlayers.Count == 0) return;

        // 3. Check for Significant Movement
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

        // 4. Calculate Required Chunks
        currentRequiredChunks.Clear();
        MapData data = MapManager.Instance.activeMapData;

        foreach (var player in trackedPlayers)
        {
            if (player == null || !player.IsSpawned) continue;

            Vector3 pos = player.transform.position;
            int cx = Mathf.FloorToInt(pos.x / ChunkData.ChunkSize.x);
            int cy = Mathf.FloorToInt(pos.y / ChunkData.ChunkSize.y);

            for (int x = -viewDistance; x <= viewDistance; x++)
            {
                for (int y = -viewDistance; y <= viewDistance; y++)
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

        // 5. Apply Changes only if needed
        if (!currentRequiredChunks.SetEquals(lastRequiredChunks))
        {
            RefreshChunksMultitarget(currentRequiredChunks);
            lastRequiredChunks.Clear();
            foreach (var coord in currentRequiredChunks) lastRequiredChunks.Add(coord);
        }
    }

    private void RefreshChunksMultitarget(HashSet<Vector2Int> requiredCoords)
    {
        toRemoveCache.Clear();
        foreach (var activeCoord in activeChunks.Keys)
        {
            if (!requiredCoords.Contains(activeCoord))
            {
                toRemoveCache.Add(activeCoord);
            }
        }

        foreach (var coord in toRemoveCache)
        {
            MeshFilter filter = activeChunks[coord];
            filter.gameObject.SetActive(false);
            chunkPool.Push(filter);
            activeChunks.Remove(coord);
        }

        foreach (var coord in requiredCoords)
        {
            ActivateChunk(coord, true);
        }
    }

    public void ClearAllChunks()
    {
        foreach (var entry in activeChunks)
        {
            MeshFilter filter = entry.Value;
            filter.gameObject.SetActive(false);
            chunkPool.Push(filter);
        }
        activeChunks.Clear();
        lastRequiredChunks.Clear();
    }

    public void RefreshAllChunks()
    {
        if (MapManager.Instance == null || MapManager.Instance.activeMapData == null) return;

        MapData data = MapManager.Instance.activeMapData;
        for (int x = 0; x < data.mapSize.x; x++)
        {
            for (int y = 0; y < data.mapSize.y; y++)
            {
                ActivateChunk(new Vector2Int(x, y), false); 
            }
        }
    }

    private void ActivateChunk(Vector2Int coord, bool drawImmediately = true)
    {
        if (activeChunks.ContainsKey(coord)) return;

        if (!MapManager.Instance.IsChunkSynced(coord.x, coord.y))
        {
            MapManager.Instance.RequestChunkSync(coord.x, coord.y);
        }

        MeshFilter filter;
        if (chunkPool.Count > 0)
        {
            filter = chunkPool.Pop();
        }
        else
        {
            filter = CreateChunkObject(activeChunks.Count + chunkPool.Count);
        }

        filter.gameObject.SetActive(true);
        activeChunks.Add(coord, filter);

        if (drawImmediately)
        {
            DrawChunk(filter, coord.x, coord.y);
            UpdateChunkCollider(coord.x, coord.y);
        }
    }

    #endregion

    #region Update (Internal)

    private HashSet<Vector2Int> redrawQueue = new HashSet<Vector2Int>();

    private void LateUpdate()
    {
        if (redrawQueue.Count > 0)
        {
            foreach (var coord in redrawQueue)
            {
                if (activeChunks.TryGetValue(coord, out MeshFilter filter))
                {
                    DrawChunk(filter, coord.x, coord.y);
                    UpdateChunkCollider(coord.x, coord.y);
                }
            }
            redrawQueue.Clear();
        }
    }

    #endregion

    #region Draw

    private void DrawChunk(MeshFilter targetFilter, int cx, int cy)
    {
        if (MapManager.Instance.activeMapData == null) return;
        ChunkData[,] allChunks = MapManager.Instance.activeMapData.chunks;
        ChunkData chunk = allChunks[cx, cy];
        if (chunk == null) return;

        int width = ChunkData.ChunkSize.x;
        int height = ChunkData.ChunkSize.y;

        // Reuse neighborStatesCache
        if (neighborStatesCache == null || neighborStatesCache.GetLength(0) != width + 2 || neighborStatesCache.GetLength(1) != height + 2)
        {
            neighborStatesCache = new bool[width + 2, height + 2];
        }

        for (int x = -1; x <= width; x++)
        {
            for (int y = -1; y <= height; y++)
            {
                neighborStatesCache[x + 1, y + 1] = HasBlock(cx, cy, x, y);
            }
        }

        Mesh mesh = targetFilter.sharedMesh;
        if (mesh == null)
        {
            mesh = new Mesh();
            mesh.name = $"Chunk_{cx}_{cy}";
            targetFilter.sharedMesh = mesh;
        }
        else
        {
            mesh.Clear();
            mesh.name = $"Chunk_{cx}_{cy}";
        }

        cachedVertices.Clear();
        cachedTriangles.Clear();
        cachedUVs.Clear();
        cachedColors.Clear();

        for (int x = 0; x < width; x++)
        {
            for (int y = 0; y < height; y++)
            {
                BlockData block = chunk.blocks[x, y];
                if (!block.isActive) continue;

                int lx = x + 1;
                int ly = y + 1;

                bool has2 = neighborStatesCache[lx, ly + 1]; 
                bool has4 = neighborStatesCache[lx - 1, ly]; 
                bool has6 = neighborStatesCache[lx + 1, ly]; 
                bool has8 = neighborStatesCache[lx, ly - 1]; 

                int bitmask = 0;
                if (has2) bitmask |= (1 << 0);
                if (has4) bitmask |= (1 << 1);
                if (has6) bitmask |= (1 << 2);
                if (has8) bitmask |= (1 << 3);

                if (has2 && has4 && !neighborStatesCache[lx - 1, ly + 1]) bitmask |= (1 << 4);
                if (has2 && has6 && !neighborStatesCache[lx + 1, ly + 1]) bitmask |= (1 << 5);
                if (has8 && has4 && !neighborStatesCache[lx - 1, ly - 1]) bitmask |= (1 << 6);
                if (has8 && has6 && !neighborStatesCache[lx + 1, ly - 1]) bitmask |= (1 << 7);

                int variation = block.kindId % 3;
                float arrayIdx = ResourceManager.Instance.GetTileArrayIndex(block.id, bitmask, variation);

                int vIndex = cachedVertices.Count;
                int wx = cx * width + x;
                int wy = cy * height + y;

                cachedVertices.Add(new Vector3(wx, wy, 0));
                cachedVertices.Add(new Vector3(wx + 1, wy, 0));
                cachedVertices.Add(new Vector3(wx, wy + 1, 0));
                cachedVertices.Add(new Vector3(wx + 1, wy + 1, 0));

                cachedTriangles.Add(vIndex + 0);
                cachedTriangles.Add(vIndex + 2);
                cachedTriangles.Add(vIndex + 3);
                cachedTriangles.Add(vIndex + 0);
                cachedTriangles.Add(vIndex + 3);
                cachedTriangles.Add(vIndex + 1);

                cachedUVs.Add(new Vector3(0, 0, arrayIdx));
                cachedUVs.Add(new Vector3(1, 0, arrayIdx));
                cachedUVs.Add(new Vector3(0, 1, arrayIdx));
                cachedUVs.Add(new Vector3(1, 1, arrayIdx));

                if (LightingManager.Instance != null)
                {
                    float bl = LightingManager.Instance.GetInterpolatedLight(wx, wy);
                    float br = LightingManager.Instance.GetInterpolatedLight(wx + 1, wy);
                    float tl = LightingManager.Instance.GetInterpolatedLight(wx, wy + 1);
                    float tr = LightingManager.Instance.GetInterpolatedLight(wx + 1, wy + 1);

                    cachedColors.Add(new Color(bl, bl, bl, 1f));
                    cachedColors.Add(new Color(br, br, br, 1f));
                    cachedColors.Add(new Color(tl, tl, tl, 1f));
                    cachedColors.Add(new Color(tr, tr, tr, 1f));
                }
                else
                {
                    cachedColors.Add(Color.white);
                    cachedColors.Add(Color.white);
                    cachedColors.Add(Color.white);
                    cachedColors.Add(Color.white);
                }
            }
        }

        mesh.SetVertices(cachedVertices);
        mesh.SetTriangles(cachedTriangles, 0);
        mesh.SetUVs(0, cachedUVs);
        mesh.SetColors(cachedColors);
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();

        targetFilter.mesh = mesh;
    }

    #endregion

    #region Physics (High Performance Edge)

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
        
        if (!activeEdges.TryGetValue(coord, out List<GameObject> edges))
        {
            edges = new List<GameObject>();
            activeEdges.Add(coord, edges);
        }

        foreach (var edgeObj in edges)
        {
            edgeObj.SetActive(false);
            edgePool.Push(edgeObj);
        }
        edges.Clear();

        int width = ChunkData.ChunkSize.x;
        int height = ChunkData.ChunkSize.y;

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
        int width = ChunkData.ChunkSize.x;
        int height = ChunkData.ChunkSize.y;
        int startX = -1;

        for (int x = 0; x < width; x++)
        {
            bool hasFace = false;
            if (isTop)
            {
                bool blockExists = (y < height) && (c.blocks[x, y].isActive);
                bool neighborExists = (y + 1 < height) ? (c.blocks[x, y + 1].isActive) : (uC != null && uC.blocks[x, 0].isActive);
                if (blockExists && !neighborExists) hasFace = true;
            }
            else
            {
                bool blockExists = (y < height) && (c.blocks[x, y].isActive);
                bool neighborExists = (y - 1 >= 0) ? (c.blocks[x, y - 1].isActive) : (dC != null && dC.blocks[x, height - 1].isActive);
                if (blockExists && !neighborExists) hasFace = true;
            }

            if (hasFace)
            {
                if (startX == -1) startX = x;
            }
            else
            {
                if (startX != -1)
                {
                    GetOrCreateEdge(parent, coord, startX, y + (isTop ? 1 : 0), x, y + (isTop ? 1 : 0), chunkEdges);
                    startX = -1;
                }
            }
        }
        if (startX != -1)
            GetOrCreateEdge(parent, coord, startX, y + (isTop ? 1 : 0), width, y + (isTop ? 1 : 0), chunkEdges);
    }

    private void ExtractVerticalSegments(GameObject parent, Vector2Int coord, int x, bool isLeft, ChunkData c, ChunkData lC, ChunkData rC, List<GameObject> chunkEdges)
    {
        int width = ChunkData.ChunkSize.x;
        int height = ChunkData.ChunkSize.y;
        int startY = -1;

        for (int y = 0; y < height; y++)
        {
            bool hasFace = false;
            if (isLeft)
            {
                bool blockExists = (x < width) && (c.blocks[x, y].isActive);
                bool neighborExists = (x - 1 >= 0) ? (c.blocks[x - 1, y].isActive) : (lC != null && lC.blocks[width - 1, y].isActive);
                if (blockExists && !neighborExists) hasFace = true;
            }
            else
            {
                bool blockExists = (x < width) && (c.blocks[x, y].isActive);
                bool neighborExists = (x + 1 < width) ? (c.blocks[x + 1, y].isActive) : (rC != null && rC.blocks[0, y].isActive);
                if (blockExists && !neighborExists) hasFace = true;
            }

            if (hasFace)
            {
                if (startY == -1) startY = y;
            }
            else
            {
                if (startY != -1)
                {
                    GetOrCreateEdge(parent, coord, x + (isLeft ? 0 : 1), startY, x + (isLeft ? 0 : 1), y, chunkEdges);
                    startY = -1;
                }
            }
        }
        if (startY != -1)
            GetOrCreateEdge(parent, coord, x + (isLeft ? 0 : 1), startY, x + (isLeft ? 0 : 1), height, chunkEdges);
    }

    private void GetOrCreateEdge(GameObject parent, Vector2Int coord, float x1, float y1, float x2, float y2, List<GameObject> chunkEdges)
    {
        GameObject edgeObj;

        if (edgePool.Count > 0)
        {
            edgeObj = edgePool.Pop();
            edgeObj.SetActive(true);
            edgeObj.transform.SetParent(parent.transform);
        }
        else
        {
            edgeObj = new GameObject("Edge_Optimized");
            edgeObj.transform.SetParent(parent.transform);
            edgeObj.layer = LayerMask.NameToLayer("Ground");
            edgeObj.AddComponent<EdgeCollider2D>();
        }
        edgeObj.transform.localPosition = Vector3.zero;
        chunkEdges.Add(edgeObj);

        EdgeCollider2D edge = edgeObj.GetComponent<EdgeCollider2D>();
        
        float worldOffsetX = coord.x * ChunkData.ChunkSize.x;
        float worldOffsetY = coord.y * ChunkData.ChunkSize.y;

        // GC Free Edge Update using cached List<Vector2>
        cachedEdgePoints.Clear();
        cachedEdgePoints.Add(new Vector2(worldOffsetX + x1, worldOffsetY + y1));
        cachedEdgePoints.Add(new Vector2(worldOffsetX + x2, worldOffsetY + y2));
        
        edge.SetPoints(cachedEdgePoints);
    }

    #endregion

    #region Chunk

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
        mr.material = tileMaterial;

        return mf;
    }

    #endregion

    #region Chunk Redraw

    public void RequestChunkRedraw(int cx, int cy)
    {
        if (cx < 0 || cy < 0) return;
        redrawQueue.Add(new Vector2Int(cx, cy));
    }

    public System.Collections.IEnumerator RequestFullRedrawCo()
    {
        int chunksProcessed = 0;
        int redrawLimitPerFrame = 200; 
        
        // Directly iterate over the dictionary to avoid allocation of a new list
        foreach (var entry in activeChunks)
        {
            if (entry.Value == null || !entry.Value.gameObject.activeInHierarchy) continue;

            DrawChunk(entry.Value, entry.Key.x, entry.Key.y);
            UpdateChunkCollider(entry.Key.x, entry.Key.y);

            chunksProcessed++;
            if (chunksProcessed % redrawLimitPerFrame == 0) yield return null;
        }
    }

    public bool IsChunkActive(int cx, int cy)
    {
        return activeChunks.ContainsKey(new Vector2Int(cx, cy));
    }

    public bool IsSlidingWindowEnabled() => useSlidingWindow;

    #endregion

    #region Block

    private bool HasBlock(int cx, int cy, int x, int y)
    {
        int targetX = x;
        int targetY = y;
        int targetCX = cx;
        int targetCY = cy;

        if (targetX < 0) { targetX += ChunkData.ChunkSize.x; targetCX--; }
        else if (targetX >= ChunkData.ChunkSize.x) { targetX -= ChunkData.ChunkSize.x; targetCX++; }

        if (targetY < 0) { targetY += ChunkData.ChunkSize.y; targetCY--; }
        else if (targetY >= ChunkData.ChunkSize.y) { targetY -= ChunkData.ChunkSize.y; targetCY++; }

        if (MapManager.Instance.activeMapData == null) return false;
        MapData data = MapManager.Instance.activeMapData;
        if (targetCX < 0 || targetCX >= data.mapSize.x || targetCY < 0 || targetCY >= data.mapSize.y)
            return false;

        ChunkData chunk = MapManager.Instance.activeMapData.chunks[targetCX, targetCY];
        if (chunk == null) return false;

        return chunk.blocks[targetX, targetY].isActive;
    }

    #endregion
}
