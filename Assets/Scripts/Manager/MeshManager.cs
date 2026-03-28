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
    [SerializeField] private Transform targetTransform; // Typically Main Camera or Player
    [SerializeField] private int viewDistance = 2; // Radius around center chunk (e.g., 2 = 5x5 grid)

    private Stack<MeshFilter> chunkPool = new Stack<MeshFilter>();
    private Dictionary<Vector2Int, MeshFilter> activeChunks = new Dictionary<Vector2Int, MeshFilter>();
    
    private Vector2Int lastCenterChunk = new Vector2Int(-999, -999);

    #endregion

    #region MonoBehaviour

    private void Start()
    {
        if (targetTransform == null)
        {
            if (Camera.main != null)
                targetTransform = Camera.main.transform;
        }
        
        // Initialize pool based on viewDistance
        int side = viewDistance * 2 + 1;
        int poolSize = side * side;
        for (int i = 0; i < poolSize; i++)
        {
            chunkPool.Push(CreateChunkObject(i));
        }
    }

    private void Update()
    {
        UpdateSlidingWindow();
    }

    #endregion

    #region Sliding

    private void UpdateSlidingWindow()
    {
        if (MapManager.Instance == null || MapManager.Instance.mapData == null || targetTransform == null) return;

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
        HashSet<Vector2Int> requiredCoords = new HashSet<Vector2Int>();

        // 1. Calculate required coordinates
        for (int x = -viewDistance; x <= viewDistance; x++)
        {
            for (int y = -viewDistance; y <= viewDistance; y++)
            {
                Vector2Int coord = new Vector2Int(center.x + x, center.y + y);
                // Check map boundaries
                if (coord.x >= 0 && coord.x < MapData.MapSize.x &&
                    coord.y >= 0 && coord.y < MapData.MapSize.y)
                {
                    requiredCoords.Add(coord);
                }
            }
        }

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
            if (!activeChunks.ContainsKey(coord))
            {
                if (chunkPool.Count > 0)
                {
                    MeshFilter filter = chunkPool.Pop();
                    filter.gameObject.SetActive(true);
                    activeChunks.Add(coord, filter);
                    DrawChunk(filter, coord.x, coord.y);
                    UpdateChunkCollider(coord.x, coord.y);
                }
                else
                {
                    // If pool is empty (due to map size or logic), create new
                    MeshFilter filter = CreateChunkObject(activeChunks.Count + chunkPool.Count);
                    filter.gameObject.SetActive(true);
                    activeChunks.Add(coord, filter);
                    DrawChunk(filter, coord.x, coord.y);
                    UpdateChunkCollider(coord.x, coord.y);
                }
            }
        }
    }


    #endregion

    #region Draw

    private void DrawChunk(MeshFilter targetFilter, int cx, int cy)
    {
        ChunkData chunk = MapManager.Instance.mapData.chunks[cx, cy];
        if (chunk == null) return;

        Mesh mesh = new Mesh();
        mesh.name = $"Chunk_{cx}_{cy}";

        List<Vector3> vertices = new List<Vector3>();
        List<int> triangles = new List<int>();
        List<Vector3> uvs = new List<Vector3>();

        int width = ChunkData.ChunkSize.x;
        int height = ChunkData.ChunkSize.y;

        for (int x = 0; x < width; x++)
        {
            for (int y = 0; y < height; y++)
            {
                BlockData block = chunk.blocks[x, y];
                if (block == null) continue;

                // Check neighbors for auto-tiling
                bool u = HasBlock(cx, cy, x, y + 1);
                bool d = HasBlock(cx, cy, x, y - 1);
                bool l = HasBlock(cx, cy, x - 1, y);
                bool r = HasBlock(cx, cy, x + 1, y);

                int bitmaskIdx = TileSpriteSet.GetBitmaskIndex(u, d, l, r);
                float arrayIdx = ResourceManager.Instance.GetTileArrayIndex(block.id, block.kindId, bitmaskIdx);

                int vIndex = vertices.Count;
                float worldX = cx * width + x;
                float worldY = cy * height + y;

                // Vertices (Order: BL, BR, TL, TR)
                vertices.Add(new Vector3(worldX, worldY, 0));           // 0: Bottom-Left
                vertices.Add(new Vector3(worldX + 1, worldY, 0));       // 1: Bottom-Right
                vertices.Add(new Vector3(worldX, worldY + 1, 0));       // 2: Top-Left
                vertices.Add(new Vector3(worldX + 1, worldY + 1, 0));   // 3: Top-Right

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
            }
        }

        mesh.SetVertices(vertices);
        mesh.SetTriangles(triangles, 0);
        mesh.SetUVs(0, uvs);
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
        
        ChunkData chunk = MapManager.Instance.mapData.chunks[cx, cy];
        if (chunk == null) return;

        GameObject chunkObj = filter.gameObject;
        
        // 1. Clean up existing colliders
        List<GameObject> toDestroy = new List<GameObject>();
        foreach (Transform child in chunkObj.transform)
        {
            if (child.name.StartsWith("Edge_"))
                toDestroy.Add(child.gameObject);
        }
        foreach (var obj in toDestroy) Destroy(obj);

        int width = ChunkData.ChunkSize.x;
        int height = ChunkData.ChunkSize.y;

        // 2. Greedy Edge Extraction
        // We scan 4 directions and merge adjacent segments in the same line.
        
        // Horizontal Segments (Top and Bottom)
        for (int y = 0; y <= height; y++)
        {
            // Top Edges (Block at y, Air at y+1)
            ExtractHorizontalSegments(chunkObj, cx, cy, y, true);
            // Bottom Edges (Block at y, Air at y-1)
            ExtractHorizontalSegments(chunkObj, cx, cy, y, false);
        }

        // Vertical Segments (Left and Right)
        for (int x = 0; x <= width; x++)
        {
            // Left Edges (Block at x, Air at x-1)
            ExtractVerticalSegments(chunkObj, cx, cy, x, true);
            // Right Edges (Block at x, Air at x+1)
            ExtractVerticalSegments(chunkObj, cx, cy, x, false);
        }
    }

    private void ExtractHorizontalSegments(GameObject parent, int cx, int cy, int y, bool isTop)
    {
        int width = ChunkData.ChunkSize.x;
        int startX = -1;

        for (int x = 0; x < width; x++)
        {
            bool hasFace = false;
            if (isTop)
            {
                // Block exists at (x,y) AND No block exists at (x,y+1)
                if (HasBlock(cx, cy, x, y) && !HasBlock(cx, cy, x, y + 1)) hasFace = true;
            }
            else
            {
                // Block exists at (x,y) AND No block exists at (x,y-1)
                if (HasBlock(cx, cy, x, y) && !HasBlock(cx, cy, x, y - 1)) hasFace = true;
            }

            if (hasFace)
            {
                if (startX == -1) startX = x; // Start new segment
            }
            else
            {
                if (startX != -1)
                {
                    // End segment and create collider
                    CreateEdge(parent, cx, cy, startX, y + (isTop ? 1 : 0), x, y + (isTop ? 1 : 0));
                    startX = -1;
                }
            }
        }
        // Handle segment reaching end of chunk
        if (startX != -1)
            CreateEdge(parent, cx, cy, startX, y + (isTop ? 1 : 0), width, y + (isTop ? 1 : 0));
    }

    private void ExtractVerticalSegments(GameObject parent, int cx, int cy, int x, bool isLeft)
    {
        int height = ChunkData.ChunkSize.y;
        int startY = -1;

        for (int y = 0; y < height; y++)
        {
            bool hasFace = false;
            if (isLeft)
            {
                // Block exists at (x,y) AND No block exists at (x-1,y)
                if (HasBlock(cx, cy, x, y) && !HasBlock(cx, cy, x - 1, y)) hasFace = true;
            }
            else
            {
                // Block exists at (x,y) AND No block exists at (x+1,y)
                if (HasBlock(cx, cy, x, y) && !HasBlock(cx, cy, x + 1, y)) hasFace = true;
            }

            if (hasFace)
            {
                if (startY == -1) startY = y;
            }
            else
            {
                if (startY != -1)
                {
                    CreateEdge(parent, cx, cy, x + (isLeft ? 0 : 1), startY, x + (isLeft ? 0 : 1), y);
                    startY = -1;
                }
            }
        }
        if (startY != -1)
            CreateEdge(parent, cx, cy, x + (isLeft ? 0 : 1), startY, x + (isLeft ? 0 : 1), height);
    }

    private void CreateEdge(GameObject parent, int cx, int cy, float x1, float y1, float x2, float y2)
    {
        GameObject edgeObj = new GameObject($"Edge_{x1}_{y1}");
        edgeObj.transform.SetParent(parent.transform);
        edgeObj.transform.localPosition = Vector3.zero;

        EdgeCollider2D edge = edgeObj.AddComponent<EdgeCollider2D>();
        
        // Convert local chunk coords to world-relative coords for the edge
        // Points are defined relative to the chunk object's position
        float worldOffsetX = cx * ChunkData.ChunkSize.x;
        float worldOffsetY = cy * ChunkData.ChunkSize.y;

        Vector2[] points = new Vector2[2];
        points[0] = new Vector2(worldOffsetX + x1, worldOffsetY + y1);
        points[1] = new Vector2(worldOffsetX + x2, worldOffsetY + y2);
        
        // The points must be local to the chunk object
        // Since the chunk object is at (0,0,0) in world for now (based on CreateChunkObject), 
        // we use the calculated world positions.
        edge.points = points;
    }

    #endregion

    #region Chunk

    private MeshFilter CreateChunkObject(int index)
    {
        GameObject chunkObj = new GameObject($"Chunk_Pool_{index}");
        chunkObj.transform.SetParent(this.transform);
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
        Vector2Int coord = new Vector2Int(cx, cy);
        if (activeChunks.TryGetValue(coord, out MeshFilter filter))
        {
            DrawChunk(filter, cx, cy);
            UpdateChunkCollider(cx, cy);
        }
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
        if (targetCX < 0 || targetCX >= MapData.MapSize.x || targetCY < 0 || targetCY >= MapData.MapSize.y)
            return false;

        ChunkData chunk = MapManager.Instance.mapData.chunks[targetCX, targetCY];
        if (chunk == null) return false;

        return chunk.blocks[targetX, targetY] != null;
    }

    #endregion
}
