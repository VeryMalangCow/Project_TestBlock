using System.Collections.Generic;
using UnityEngine;

public class RenderManager : Singleton<RenderManager>
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
                }
                else
                {
                    // If pool is empty (due to map size or logic), create new
                    MeshFilter filter = CreateChunkObject(activeChunks.Count + chunkPool.Count);
                    filter.gameObject.SetActive(true);
                    activeChunks.Add(coord, filter);
                    DrawChunk(filter, coord.x, coord.y);
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
