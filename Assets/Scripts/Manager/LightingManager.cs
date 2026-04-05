using System.Collections.Generic;
using UnityEngine;

public class LightingManager : Singleton<LightingManager>
{
    #region Variable

    [Header("# Lighting Settings")]
    [SerializeField, Range(0, 255)] private byte maxLightIntensity = 255;
    [SerializeField, Range(0, 255)] private byte airDecay = 15;
    [SerializeField, Range(0, 255)] private byte blockDecay = 60;

    private Queue<Vector2Int> lightQueue = new Queue<Vector2Int>();

    // GPU Lighting
    private Texture2D worldLightTexture;
    private Color32[] textureBuffer;
    private bool isTextureDirty = false;
    private int cachedTotalWidth;
    private int cachedTotalHeight;

    #endregion

    #region MonoBehaviour

    protected override void Awake()
    {
        base.Awake();
    }

    private void LateUpdate()
    {
        // 1. Only update if data has actually changed
        if (isTextureDirty && worldLightTexture != null)
        {
            worldLightTexture.SetPixels32(textureBuffer);
            worldLightTexture.Apply();
            isTextureDirty = false;

            // 2. Bind to shader ONLY when data changes
            UpdateShaderProperties();
        }
    }

    private void UpdateShaderProperties()
    {
        if (worldLightTexture == null) return;

        Shader.SetGlobalTexture("_WorldLightMap", worldLightTexture);
        Shader.SetGlobalVector("_WorldLightSettings", new Vector4(
            worldLightTexture.width,
            worldLightTexture.height,
            0, 0));
    }

    #endregion

    #region Init

    private bool InitializeLightTexture()
    {
        if (MapManager.Instance == null || MapManager.Instance.activeMapData == null) return false;

        MapData data = MapManager.Instance.activeMapData;
        cachedTotalWidth = data.mapSize.x * ChunkData.Size;
        cachedTotalHeight = data.mapSize.y * ChunkData.Size;

        if (cachedTotalWidth <= 0 || cachedTotalHeight <= 0) return false;

        if (worldLightTexture != null && worldLightTexture.width == cachedTotalWidth && worldLightTexture.height == cachedTotalHeight)
            return true;

        worldLightTexture = new Texture2D(cachedTotalWidth, cachedTotalHeight, TextureFormat.RGBA32, false);
        worldLightTexture.filterMode = FilterMode.Bilinear;
        worldLightTexture.wrapMode = TextureWrapMode.Clamp;
        
        textureBuffer = new Color32[cachedTotalWidth * cachedTotalHeight];
        for (int i = 0; i < textureBuffer.Length; i++) textureBuffer[i] = new Color32(0, 0, 0, 255);
        
        worldLightTexture.SetPixels32(textureBuffer);
        worldLightTexture.Apply();

        // Bind immediately upon creation
        UpdateShaderProperties();
        return true;
    }

    #endregion

    #region Lighting Operation

    public System.Collections.IEnumerator CalculateAllLightingCo()
    {
        if (MapManager.Instance == null || MapManager.Instance.activeMapData == null) yield break;

        if (!InitializeLightTexture()) yield break;

        int totalWidth = cachedTotalWidth;
        int totalHeight = cachedTotalHeight;

        lightQueue.Clear();

        int opsPerFrame = 100000;
        int currentOps = 0;

        for (int x = 0; x < totalWidth; x++)
        {
            bool hitBlock = false;
            for (int y = totalHeight - 1; y >= 0; y--)
            {
                if (!hitBlock)
                {
                    SetLightValue(x, y, maxLightIntensity);
                    lightQueue.Enqueue(new Vector2Int(x, y));
                    if (HasBlock(x, y)) hitBlock = true;
                }
                else
                {
                    SetLightValue(x, y, 0);
                }

                currentOps++;
                if (currentOps >= opsPerFrame) { currentOps = 0; yield return null; }
            }
        }

        yield return StartCoroutine(SpreadLightCo(null, opsPerFrame));
        isTextureDirty = true;
    }

    /// <summary>
    /// Updates lighting in a local area around the changed block.
    /// </summary>
    public void UpdateLightingAt(int worldX, int worldY)
    {
        int range = 15;
        MapData data = MapManager.Instance.activeMapData;
        int totalWidth = data.mapSize.x * ChunkData.Size;
        int totalHeight = data.mapSize.y * ChunkData.Size;

        lightQueue.Clear();
        HashSet<Vector2Int> chunksToRedraw = new HashSet<Vector2Int>();

        for (int x = worldX - range; x <= worldX + range; x++)
        {
            if (x < 0 || x >= totalWidth) continue;
            bool sunlightSourceFound = false;
            for (int y = worldY + range; y >= worldY - range; y--)
            {
                if (y < 0 || y >= totalHeight) continue;
                bool nothingAbove = true;
                int checkLimit = Mathf.Min(totalHeight - 1, worldY + range + 10);
                for (int ty = y + 1; ty <= checkLimit; ty++)
                {
                    if (HasBlock(x, ty)) { nothingAbove = false; break; }
                }

                if (nothingAbove && !sunlightSourceFound)
                {
                    SetLightValue(x, y, maxLightIntensity);
                    lightQueue.Enqueue(new Vector2Int(x, y));
                    if (HasBlock(x, y)) sunlightSourceFound = true;
                }
                else
                {
                    if (x == worldX - range || x == worldX + range || y == worldY - range || y == worldY + range)
                    {
                        byte currentVal = GetLightValue(x, y);
                        if (currentVal > 0) lightQueue.Enqueue(new Vector2Int(x, y));
                    }
                    else SetLightValue(x, y, 0);
                }
            }
        }

        SpreadLight(chunksToRedraw);
        isTextureDirty = true;

        // Chunks no longer need full redraw for lighting, but they might need it for block changes
        // For pure lighting update, the Global Shader Texture handles it!
    }

    private void SpreadLight(HashSet<Vector2Int> affectedChunks)
    {
        Vector2Int[] dirs = { Vector2Int.up, Vector2Int.down, Vector2Int.left, Vector2Int.right };
        MapData data = MapManager.Instance.activeMapData;
        int totalWidth = data.mapSize.x * ChunkData.ChunkSize.x;
        int totalHeight = data.mapSize.y * ChunkData.ChunkSize.y;
        int cw = ChunkData.ChunkSize.x;
        int ch = ChunkData.ChunkSize.y;

        while (lightQueue.Count > 0)
        {
            Vector2Int curr = lightQueue.Dequeue();
            byte currLight = GetLightValue(curr.x, curr.y);
            if (currLight <= airDecay) continue;

            foreach (var dir in dirs)
            {
                Vector2Int next = curr + dir;
                if (next.x < 0 || next.x >= totalWidth || next.y < 0 || next.y >= totalHeight) continue;
                byte decay = HasBlock(next.x, next.y) ? blockDecay : airDecay;
                byte nextTargetLight = (byte)Mathf.Max(0, currLight - decay);

                if (GetLightValue(next.x, next.y) < nextTargetLight)
                {
                    SetLightValue(next.x, next.y, nextTargetLight);
                    lightQueue.Enqueue(next);
                    if (affectedChunks != null)
                    {
                        affectedChunks.Add(new Vector2Int(next.x / cw, next.y / ch));
                        if (next.x % cw == 0) affectedChunks.Add(new Vector2Int(next.x / cw - 1, next.y / ch));
                        if (next.x % cw == cw - 1) affectedChunks.Add(new Vector2Int(next.x / cw + 1, next.y / ch));
                        if (next.y % ch == 0) affectedChunks.Add(new Vector2Int(next.x / cw, next.y / ch - 1));
                        if (next.y % ch == ch - 1) affectedChunks.Add(new Vector2Int(next.x / cw, next.y / ch + 1));
                    }
                }
            }
        }
    }

    private System.Collections.IEnumerator SpreadLightCo(HashSet<Vector2Int> affectedChunks, int opsPerFrame)
    {
        Vector2Int[] dirs = { Vector2Int.up, Vector2Int.down, Vector2Int.left, Vector2Int.right };
        MapData data = MapManager.Instance.activeMapData;
        int totalWidth = data.mapSize.x * ChunkData.ChunkSize.x;
        int totalHeight = data.mapSize.y * ChunkData.ChunkSize.y;
        int cw = ChunkData.ChunkSize.x;
        int ch = ChunkData.ChunkSize.y;
        int currentOps = 0;

        while (lightQueue.Count > 0)
        {
            Vector2Int curr = lightQueue.Dequeue();
            byte currLight = GetLightValue(curr.x, curr.y);
            if (currLight <= airDecay) continue;

            foreach (var dir in dirs)
            {
                Vector2Int next = curr + dir;
                if (next.x < 0 || next.x >= totalWidth || next.y < 0 || next.y >= totalHeight) continue;
                byte decay = HasBlock(next.x, next.y) ? blockDecay : airDecay;
                byte nextTargetLight = (byte)Mathf.Max(0, currLight - decay);

                if (GetLightValue(next.x, next.y) < nextTargetLight)
                {
                    SetLightValue(next.x, next.y, nextTargetLight);
                    lightQueue.Enqueue(next);
                    if (affectedChunks != null)
                    {
                        affectedChunks.Add(new Vector2Int(next.x / cw, next.y / ch));
                        if (next.x % cw == 0) affectedChunks.Add(new Vector2Int(next.x / cw - 1, next.y / ch));
                        if (next.x % cw == cw - 1) affectedChunks.Add(new Vector2Int(next.x / cw + 1, next.y / ch));
                        if (next.y % ch == 0) affectedChunks.Add(new Vector2Int(next.x / cw, next.y / ch - 1));
                        if (next.y % ch == ch - 1) affectedChunks.Add(new Vector2Int(next.x / cw, next.y / ch + 1));
                    }
                }
            }

            currentOps++;
            if (currentOps >= opsPerFrame) { currentOps = 0; yield return null; }
        }
    }

    /// <summary>
    /// Calculates interpolated light value at a vertex by averaging 4 neighbor tiles.
    /// </summary>
    public float GetInterpolatedLight(int wx, int wy)
    {
        float sum = 0;
        sum += GetLightValue(wx - 1, wy - 1);
        sum += GetLightValue(wx, wy - 1);
        sum += GetLightValue(wx - 1, wy);
        sum += GetLightValue(wx, wy);
        return (sum / 4f) / 255f;
    }

    #endregion

    #region Chunk Sync (For Clients)

    /// <summary>
    /// Called by MapManager on clients when a new chunk is synced.
    /// Updates the GPU Light Map for that specific chunk area.
    /// </summary>
    public void SyncChunkLight(int cx, int cy, byte[] lights)
    {
        if (!InitializeLightTexture()) return;

        int width = ChunkData.Size;
        int height = ChunkData.Size;
        int worldOffsetX = cx * width;
        int worldOffsetY = cy * height;

        for (int ly = 0; ly < height; ly++)
        {
            for (int lx = 0; lx < width; lx++)
            {
                int wx = worldOffsetX + lx;
                int wy = worldOffsetY + ly;
                byte value = lights[ChunkData.GetIndex(lx, ly)];

                int texIdx = wy * cachedTotalWidth + wx;
                if (texIdx >= 0 && texIdx < textureBuffer.Length)
                {
                    textureBuffer[texIdx] = new Color32(value, value, value, 255);
                }
            }
        }
        isTextureDirty = true;
    }

    #endregion

    #region Helper

    private void SetLightValue(int worldX, int worldY, byte value)
    {
        int width = ChunkData.Size;
        int height = ChunkData.Size;

        int cx = Mathf.FloorToInt((float)worldX / width);
        int cy = Mathf.FloorToInt((float)worldY / height);
        int lx = worldX - (cx * width);
        int ly = worldY - (cy * height);

        if (cx < 0 || cx >= MapManager.Instance.activeMapData.mapSize.x || cy < 0 || cy >= MapManager.Instance.activeMapData.mapSize.y) return;

        // 1. Update Logical Data
        ChunkData chunk = MapManager.Instance.activeMapData.chunks[cx, cy];
        chunk.lightValues[ChunkData.GetIndex(lx, ly)] = value;

        // 2. Update Texture Buffer for GPU
        if (textureBuffer != null)
        {
            int texIdx = worldY * cachedTotalWidth + worldX;
            if (texIdx >= 0 && texIdx < textureBuffer.Length)
            {
                textureBuffer[texIdx] = new Color32(value, value, value, 255);
                isTextureDirty = true;
            }
        }
    }

    private byte GetLightValue(int worldX, int worldY)
    {
        int width = ChunkData.Size;
        int height = ChunkData.Size;

        int cx = Mathf.FloorToInt((float)worldX / width);
        int cy = Mathf.FloorToInt((float)worldY / height);
        int lx = worldX - (cx * width);
        int ly = worldY - (cy * height);

        if (cx < 0 || cx >= MapManager.Instance.activeMapData.mapSize.x || cy < 0 || cy >= MapManager.Instance.activeMapData.mapSize.y) return 0;
        
        ChunkData chunk = MapManager.Instance.activeMapData.chunks[cx, cy];
        return chunk.lightValues[ChunkData.GetIndex(lx, ly)];
    }

    private bool HasBlock(int worldX, int worldY)
    {
        int width = ChunkData.Size;
        int height = ChunkData.Size;

        int cx = Mathf.FloorToInt((float)worldX / width);
        int cy = Mathf.FloorToInt((float)worldY / height);
        int lx = worldX - (cx * width);
        int ly = worldY - (cy * height);

        if (cx < 0 || cx >= MapManager.Instance.activeMapData.mapSize.x || cy < 0 || cy >= MapManager.Instance.activeMapData.mapSize.y) return false;
        
        ChunkData chunk = MapManager.Instance.activeMapData.chunks[cx, cy];
        return chunk.blocks[ChunkData.GetIndex(lx, ly)].isActive;
    }

    #endregion
}
