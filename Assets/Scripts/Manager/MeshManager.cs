using System.Collections.Generic;
using UnityEngine;

public class MeshManager : Singleton<MeshManager>
{
    #region Varialble

    [Header("### Map")]
    [Header("## Tile")]
    [Header("# Render")]
    [SerializeField] private Material tileMaterial;

    [Header("## Chunk")]
    [Header("# Sliding+Pool")]
    [SerializeField] private bool useSlidingWindow = true;
    [SerializeField] private Transform targetTransform; // Typically Main Camera or Player
    [SerializeField] private int viewDistance = 2; // Radius around center chunk (e.g., 2 = 5x5 grid)

    private Stack<MeshFilter> chunkPool = new Stack<MeshFilter>();
    private Dictionary<Vector2Int, MeshFilter> activeChunks = new Dictionary<Vector2Int, MeshFilter>();
    
    private Vector2Int lastCenterChunk = new Vector2Int(-999, -999);
    private bool lastSlidingState = true;

    #endregion

    #region MonoBehaviour


    private void Start()
    {
        lastSlidingState = useSlidingWindow;

        if (targetTransform == null)
        {
            if (Camera.main != null)
            {
                targetTransform = Camera.main.transform;
                Debug.Log("[MeshManager] Target transform set to Main Camera.");
            }
            else
            {
                Debug.LogWarning("[MeshManager] Main Camera not found! TargetTransform is null.");
            }
        }
        
        // Initialize a basic pool first
        // If sliding window is on, size depends on viewDistance (e.g., 2 -> 25 chunks)
        // If off, default to 100 or map size
        int initialPoolSize = 100;

        if (useSlidingWindow)
        {
            initialPoolSize = (viewDistance * 2 + 1) * (viewDistance * 2 + 1);
        }
        else if (MapManager.Instance != null && MapManager.Instance.activeMapData != null)
        {
            Vector2Int size = MapManager.Instance.activeMapData.mapSize;
            if (size.x > 0 && size.y > 0)
                initialPoolSize = size.x * size.y;
        }

        // Final safety check: Always create at least 1 chunk pool object
        if (initialPoolSize <= 0) initialPoolSize = 25; 

        Debug.Log($"[MeshManager] Initializing chunk pool with size: {initialPoolSize}");
        for (int i = 0; i < initialPoolSize; i++)
        {
            chunkPool.Push(CreateChunkObject(i + chunkPool.Count));
        }


        if (!useSlidingWindow)
            RefreshAllChunks();
    }

    private void Update()
    {
        // Handle Runtime Toggle
        if (useSlidingWindow != lastSlidingState)
        {
            if (useSlidingWindow)
            {
                lastCenterChunk = new Vector2Int(-999, -999); // Force refresh
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

    private void UpdateSlidingWindow()
    {
        if (MapManager.Instance == null || MapManager.Instance.activeMapData == null || targetTransform == null) return;

        // Calculate target's current chunk coordinate
        int cx = Mathf.FloorToInt(targetTransform.position.x / ChunkData.ChunkSize.x);
        int cy = Mathf.FloorToInt(targetTransform.position.y / ChunkData.ChunkSize.y);
        Vector2Int currentCenter = new Vector2Int(cx, cy);

        // Only refresh if center chunk changed
        if (currentCenter != lastCenterChunk)
        {
            RefreshChunks(currentCenter);
            lastCenterChunk = currentCenter;
        }
    }

    private void RefreshChunks(Vector2Int center)
    {
        if (MapManager.Instance == null || MapManager.Instance.activeMapData == null) return;
        
        MapData data = MapManager.Instance.activeMapData;
        HashSet<Vector2Int> requiredCoords = new HashSet<Vector2Int>();

        // 1. Calculate required coordinates
        for (int x = -viewDistance; x <= viewDistance; x++)
        {
            for (int y = -viewDistance; y <= viewDistance; y++)
            {
                Vector2Int coord = new Vector2Int(center.x + x, center.y + y);
                // Check map boundaries
                if (coord.x >= 0 && coord.x < data.mapSize.x &&
                    coord.y >= 0 && coord.y < data.mapSize.y)
                {
                    requiredCoords.Add(coord);
                }
            }
        }

        Debug.Log($"[MeshManager] Refreshing chunks. Required count: {requiredCoords.Count}");

        // 2. Recycle chunks that are out of range
        List<Vector2Int> toRemove = new List<Vector2Int>();
        foreach (var activeCoord in activeChunks.Keys)
        {
            if (!requiredCoords.Contains(activeCoord))
            {
                toRemove.Add(activeCoord);
            }
        }

        foreach (var coord in toRemove)
        {
            MeshFilter filter = activeChunks[coord];
            filter.gameObject.SetActive(false);
            chunkPool.Push(filter);
            activeChunks.Remove(coord);
        }

        // 3. Activate and update chunks for new required coordinates
        foreach (var coord in requiredCoords)
        {
            ActivateChunk(coord, true); // true = draw immediately for sliding
        }
    }

    public void ClearAllChunks()
    {
        Debug.Log("[MeshManager] Clearing all active chunks...");
        foreach (var entry in activeChunks)
        {
            MeshFilter filter = entry.Value;
            filter.gameObject.SetActive(false);
            chunkPool.Push(filter);
        }
        activeChunks.Clear();
        lastCenterChunk = new Vector2Int(-999, -999);
    }

    public void RefreshAllChunks()
    {
        if (MapManager.Instance == null || MapManager.Instance.activeMapData == null)
        {
            Debug.LogWarning("[MeshManager] RefreshAllChunks aborted: activeMapData is null.");
            return;
        }

        MapData data = MapManager.Instance.activeMapData;
        Debug.Log($"[MeshManager] Activating {data.mapSize.x * data.mapSize.y} chunks (Activation only)...");

        // Activate every chunk in the map size without drawing immediately
        for (int x = 0; x < data.mapSize.x; x++)
        {
            for (int y = 0; y < data.mapSize.y; y++)
            {
                ActivateChunk(new Vector2Int(x, y), false); // false = Don't draw now
            }
        }
        Debug.Log("[MeshManager] All chunks activated in hierarchy.");
    }

    private void ActivateChunk(Vector2Int coord, bool drawImmediately = true)
    {
        if (activeChunks.ContainsKey(coord)) return;

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

    #region Draw

    private void DrawChunk(MeshFilter targetFilter, int cx, int cy)
    {
        ChunkData[,] allChunks = MapManager.Instance.activeMapData.chunks;
        ChunkData chunk = allChunks[cx, cy];
        if (chunk == null) return;

        // Optimization: Cache all block states (Self + Neighbors) into a local array
        int width = ChunkData.ChunkSize.x;
        int height = ChunkData.ChunkSize.y;
        bool[,] neighborStates = new bool[width + 2, height + 2];
        for (int x = -1; x <= width; x++)
        {
            for (int y = -1; y <= height; y++)
            {
                neighborStates[x + 1, y + 1] = HasBlock(cx, cy, x, y);
            }
        }

        // Optimization: Reuse existing mesh
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

        List<Vector3> vertices = new List<Vector3>();
        List<int> triangles = new List<int>();
        List<Vector3> uvs = new List<Vector3>();
        List<Color> colors = new List<Color>();

        for (int x = 0; x < width; x++)
        {
            for (int y = 0; y < height; y++)
            {
                BlockData block = chunk.blocks[x, y];
                if (!block.isActive) continue;

                // Local coordinates in neighborStates: x+1, y+1
                int lx = x + 1;
                int ly = y + 1;

                // 8-Direction Neighbor Check (Phone Keypad: 2=Up, 8=Down, 4=Left, 6=Right)
                bool has2 = neighborStates[lx, ly + 1]; // Up
                bool has4 = neighborStates[lx - 1, ly]; // Left
                bool has6 = neighborStates[lx + 1, ly]; // Right
                bool has8 = neighborStates[lx, ly - 1]; // Down

                int bitmask = 0;
                if (has2) bitmask |= (1 << 0);
                if (has4) bitmask |= (1 << 1);
                if (has6) bitmask |= (1 << 2);
                if (has8) bitmask |= (1 << 3);

                // Diagonal (Missing: 1,3,7,9)
                // Conditions: Adjacent orthogonals must exist to check diagonal absence
                if (has2 && has4 && !neighborStates[lx - 1, ly + 1]) bitmask |= (1 << 4); // 1: TL Missing
                if (has2 && has6 && !neighborStates[lx + 1, ly + 1]) bitmask |= (1 << 5); // 3: TR Missing
                if (has8 && has4 && !neighborStates[lx - 1, ly - 1]) bitmask |= (1 << 6); // 7: BL Missing
                if (has8 && has6 && !neighborStates[lx + 1, ly - 1]) bitmask |= (1 << 7); // 9: BR Missing

                // Use kindId (0~2) for variation
                int variation = block.kindId % 3;
                float arrayIdx = ResourceManager.Instance.GetTileArrayIndex(block.id, bitmask, variation);

                int vIndex = vertices.Count;
                int wx = cx * width + x;
                int wy = cy * height + y;

                // Vertices (Order: BL, BR, TL, TR)
                vertices.Add(new Vector3(wx, wy, 0));           // 0: Bottom-Left
                vertices.Add(new Vector3(wx + 1, wy, 0));       // 1: Bottom-Right
                vertices.Add(new Vector3(wx, wy + 1, 0));       // 2: Top-Left
                vertices.Add(new Vector3(wx + 1, wy + 1, 0));   // 3: Top-Right

                // Triangles (Clockwise winding)
                triangles.Add(vIndex + 0);
                triangles.Add(vIndex + 2);
                triangles.Add(vIndex + 3);
                triangles.Add(vIndex + 0);
                triangles.Add(vIndex + 3);
                triangles.Add(vIndex + 1);

                // UVs with Array Index in Z
                uvs.Add(new Vector3(0, 0, arrayIdx));
                uvs.Add(new Vector3(1, 0, arrayIdx));
                uvs.Add(new Vector3(0, 1, arrayIdx));
                uvs.Add(new Vector3(1, 1, arrayIdx));

                // Smooth Vertex Colors (Interpolated)
                if (LightingManager.Instance != null)
                {
                    float bl = LightingManager.Instance.GetInterpolatedLight(wx, wy);
                    float br = LightingManager.Instance.GetInterpolatedLight(wx + 1, wy);
                    float tl = LightingManager.Instance.GetInterpolatedLight(wx, wy + 1);
                    float tr = LightingManager.Instance.GetInterpolatedLight(wx + 1, wy + 1);

                    colors.Add(new Color(bl, bl, bl, 1f));
                    colors.Add(new Color(br, br, br, 1f));
                    colors.Add(new Color(tl, tl, tl, 1f));
                    colors.Add(new Color(tr, tr, tr, 1f));
                }
                else
                {
                    colors.Add(Color.white);
                    colors.Add(Color.white);
                    colors.Add(Color.white);
                    colors.Add(Color.white);
                }
            }
        }

        mesh.SetVertices(vertices);
        mesh.SetTriangles(triangles, 0);
        mesh.SetUVs(0, uvs);
        mesh.SetColors(colors);
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();

        targetFilter.mesh = mesh;
    }

    #endregion

    #region Physics (High Performance Edge)

    /// <summary>
    /// Generates the most performant EdgeCollider2D by merging contiguous exposed faces.
    /// Call manually when needed.
    /// </summary>
    public void UpdateChunkCollider(int cx, int cy)
    {
        if (!activeChunks.TryGetValue(new Vector2Int(cx, cy), out MeshFilter filter)) return;
        
        ChunkData[,] allChunks = MapManager.Instance.activeMapData.chunks;
        ChunkData chunk = allChunks[cx, cy];
        if (chunk == null) return;

        // Pre-fetch neighbors
        MapData data = MapManager.Instance.activeMapData;
        ChunkData chunkU = (cy + 1 < data.mapSize.y) ? allChunks[cx, cy + 1] : null;
        ChunkData chunkD = (cy - 1 >= 0) ? allChunks[cx, cy - 1] : null;
        ChunkData chunkL = (cx - 1 >= 0) ? allChunks[cx - 1, cy] : null;
        ChunkData chunkR = (cx + 1 < data.mapSize.x) ? allChunks[cx + 1, cy] : null;

        GameObject chunkObj = filter.gameObject;
        
        // 1. Pooling: Get all existing edge objects and deactivate them
        List<GameObject> edgePool = new List<GameObject>();
        foreach (Transform child in chunkObj.transform)
        {
            if (child.name.StartsWith("Edge_"))
            {
                child.gameObject.SetActive(false);
                edgePool.Add(child.gameObject);
            }
        }

        int poolIndex = 0;
        int width = ChunkData.ChunkSize.x;
        int height = ChunkData.ChunkSize.y;

        // 2. Greedy Edge Extraction
        // Horizontal Segments (Top and Bottom)
        for (int y = 0; y <= height; y++)
        {
            ExtractHorizontalSegments(chunkObj, cx, cy, y, true, chunk, chunkU, chunkD, edgePool, ref poolIndex);
            ExtractHorizontalSegments(chunkObj, cx, cy, y, false, chunk, chunkU, chunkD, edgePool, ref poolIndex);
        }

        // Vertical Segments (Left and Right)
        for (int x = 0; x <= width; x++)
        {
            ExtractVerticalSegments(chunkObj, cx, cy, x, true, chunk, chunkL, chunkR, edgePool, ref poolIndex);
            ExtractVerticalSegments(chunkObj, cx, cy, x, false, chunk, chunkL, chunkR, edgePool, ref poolIndex);
        }
    }

    private void ExtractHorizontalSegments(GameObject parent, int cx, int cy, int y, bool isTop, ChunkData c, ChunkData uC, ChunkData dC, List<GameObject> pool, ref int poolIdx)
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
                    GetOrCreateEdge(parent, cx, cy, startX, y + (isTop ? 1 : 0), x, y + (isTop ? 1 : 0), pool, ref poolIdx);
                    startX = -1;
                }
            }
        }
        if (startX != -1)
            GetOrCreateEdge(parent, cx, cy, startX, y + (isTop ? 1 : 0), width, y + (isTop ? 1 : 0), pool, ref poolIdx);
    }

    private void ExtractVerticalSegments(GameObject parent, int cx, int cy, int x, bool isLeft, ChunkData c, ChunkData lC, ChunkData rC, List<GameObject> pool, ref int poolIdx)
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
                    GetOrCreateEdge(parent, cx, cy, x + (isLeft ? 0 : 1), startY, x + (isLeft ? 0 : 1), y, pool, ref poolIdx);
                    startY = -1;
                }
            }
        }
        if (startY != -1)
            GetOrCreateEdge(parent, cx, cy, x + (isLeft ? 0 : 1), startY, x + (isLeft ? 0 : 1), height, pool, ref poolIdx);
    }

    private void GetOrCreateEdge(GameObject parent, int cx, int cy, float x1, float y1, float x2, float y2, List<GameObject> pool, ref int poolIdx)
    {
        GameObject edgeObj;

        // Reuse from pool if available
        if (poolIdx < pool.Count)
        {
            edgeObj = pool[poolIdx];
            edgeObj.SetActive(true);
        }
        else
        {
            // Create new if pool is empty
            edgeObj = new GameObject($"Edge_{poolIdx}");
            edgeObj.transform.SetParent(parent.transform);
            edgeObj.transform.localPosition = Vector3.zero;
            edgeObj.layer = LayerMask.NameToLayer("Ground");
            edgeObj.AddComponent<EdgeCollider2D>();
        }
        poolIdx++;

        EdgeCollider2D edge = edgeObj.GetComponent<EdgeCollider2D>();
        
        float worldOffsetX = cx * ChunkData.ChunkSize.x;
        float worldOffsetY = cy * ChunkData.ChunkSize.y;

        Vector2[] points = new Vector2[2];
        points[0] = new Vector2(worldOffsetX + x1, worldOffsetY + y1);
        points[1] = new Vector2(worldOffsetX + x2, worldOffsetY + y2);
        
        edge.points = points;
    }

    #endregion

    #region Chunk

    private MeshFilter CreateChunkObject(int index)
    {
        GameObject chunkObj = new GameObject($"Chunk_Pool_{index}");
        chunkObj.transform.SetParent(this.transform);
        
        int layer = LayerMask.NameToLayer("Ground");
        if (layer == -1)
        {
            Debug.LogWarning("[MeshManager] 'Ground' layer not found! Using default layer (0).");
            layer = 0;
        }
        chunkObj.layer = layer;
        chunkObj.SetActive(false);

        MeshFilter mf = chunkObj.AddComponent<MeshFilter>();
        MeshRenderer mr = chunkObj.AddComponent<MeshRenderer>();
        if (tileMaterial == null) Debug.LogWarning("[MeshManager] TileMaterial is not assigned in the inspector!");
        mr.material = tileMaterial;

        return mf;
    }

    #endregion

    #region Chunk Redraw

    public void RequestChunkRedraw(int cx, int cy)
    {
        Vector2Int coord = new Vector2Int(cx, cy);
        if (activeChunks.TryGetValue(coord, out MeshFilter filter))
        {
            DrawChunk(filter, cx, cy);
            UpdateChunkCollider(cx, cy);
        }
    }

    public void RequestFullRedraw()
    {
        StartCoroutine(RequestFullRedrawCo());
    }

    public System.Collections.IEnumerator RequestFullRedrawCo()
    {
        int chunksProcessed = 0;
        int redrawLimitPerFrame = 200; 
        
        // Copy the entries to a list to avoid "Collection modified" exception
        // if Sliding Window modifies activeChunks while we are yielding.
        var entries = new List<KeyValuePair<Vector2Int, MeshFilter>>(activeChunks);
        int totalChunks = entries.Count;

        Debug.Log($"[MeshManager] Starting Full Redraw of {totalChunks} chunks...");

        foreach (var entry in entries)
        {
            // Extra safety: Check if the chunk is still active and valid
            if (entry.Value == null || !entry.Value.gameObject.activeInHierarchy) continue;

            DrawChunk(entry.Value, entry.Key.x, entry.Key.y);
            UpdateChunkCollider(entry.Key.x, entry.Key.y);

            chunksProcessed++;
            if (chunksProcessed % redrawLimitPerFrame == 0)
            {
                // Progress log every 10%
                int step = totalChunks / 10;
                if (step <= 0) step = 1;
                if (chunksProcessed % step == 0)
                {
                    float progress = (float)chunksProcessed / totalChunks * 100f;
                    Debug.Log($"[MeshManager] Redraw Progress: {progress:F1}% ({chunksProcessed}/{totalChunks})");
                }
                yield return null;
            }
        }
        Debug.Log("[MeshManager] Full Redraw Complete.");
    }

    #endregion

    #region Block

    private bool HasBlock(int cx, int cy, int x, int y)
    {
        int targetX = x;
        int targetY = y;
        int targetCX = cx;
        int targetCY = cy;

        // Handle chunk transitions
        if (targetX < 0) { targetX += ChunkData.ChunkSize.x; targetCX--; }
        else if (targetX >= ChunkData.ChunkSize.x) { targetX -= ChunkData.ChunkSize.x; targetCX++; }

        if (targetY < 0) { targetY += ChunkData.ChunkSize.y; targetCY--; }
        else if (targetY >= ChunkData.ChunkSize.y) { targetY -= ChunkData.ChunkSize.y; targetCY++; }

        // Check map boundaries
        MapData data = MapManager.Instance.activeMapData;
        if (targetCX < 0 || targetCX >= data.mapSize.x || targetCY < 0 || targetCY >= data.mapSize.y)
            return false;

        ChunkData chunk = MapManager.Instance.activeMapData.chunks[targetCX, targetCY];
        if (chunk == null) return false;

        return chunk.blocks[targetX, targetY].isActive;
    }

    #endregion
}