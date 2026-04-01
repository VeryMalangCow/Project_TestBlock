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

    #endregion

    #region Lighting Operation

    /// <summary>
    /// Initial lighting calculation using BFS for natural propagation.
    /// </summary>
    public System.Collections.IEnumerator CalculateAllLightingCo()
    {
        if (MapManager.Instance == null || MapManager.Instance.activeMapData == null) yield break;

        MapData data = MapManager.Instance.activeMapData;
        int totalWidth = data.mapSize.x * ChunkData.ChunkSize.x;
        int totalHeight = data.mapSize.y * ChunkData.ChunkSize.y;

        lightQueue.Clear();

        int opsPerFrame = 100000;
        int currentOps = 0;

        // 1. Reset all to 0 and find initial sunlight sources
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

        // 2. Propagate
        yield return StartCoroutine(SpreadLightCo(null, opsPerFrame));
    }

    /// <summary>
    /// Updates lighting in a local area around the changed block.
    /// </summary>
    public void UpdateLightingAt(int worldX, int worldY)
    {
        int range = 15;
        MapData data = MapManager.Instance.activeMapData;
        int totalWidth = data.mapSize.x * ChunkData.ChunkSize.x;
        int totalHeight = data.mapSize.y * ChunkData.ChunkSize.y;

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
        foreach (var coord in chunksToRedraw) MeshManager.Instance.RequestChunkRedraw(coord.x, coord.y);
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

    #region Helper

    private void SetLightValue(int worldX, int worldY, byte value)
    {
        int width = ChunkData.ChunkSize.x;
        int height = ChunkData.ChunkSize.y;

        int cx = worldX / width;
        int cy = worldY / height;
        int lx = worldX % width;
        int ly = worldY % height;

        if (lx < 0) { lx += width; cx--; }
        if (ly < 0) { ly += height; cy--; }

        if (cx < 0 || cx >= MapManager.Instance.activeMapData.mapSize.x || cy < 0 || cy >= MapManager.Instance.activeMapData.mapSize.y) return;

        MapManager.Instance.activeMapData.chunks[cx, cy].lightValues[lx, ly] = value;
    }

    private byte GetLightValue(int worldX, int worldY)
    {
        int width = ChunkData.ChunkSize.x;
        int height = ChunkData.ChunkSize.y;

        int cx = worldX / width;
        int cy = worldY / height;
        int lx = worldX % width;
        int ly = worldY % height;

        if (lx < 0) { lx += width; cx--; }
        if (ly < 0) { ly += height; cy--; }

        if (cx < 0 || cx >= MapManager.Instance.activeMapData.mapSize.x || cy < 0 || cy >= MapManager.Instance.activeMapData.mapSize.y) return 0;
        
        return MapManager.Instance.activeMapData.chunks[cx, cy].lightValues[lx, ly];
    }

    private bool HasBlock(int worldX, int worldY)
    {
        int width = ChunkData.ChunkSize.x;
        int height = ChunkData.ChunkSize.y;

        int cx = worldX / width;
        int cy = worldY / height;
        int lx = worldX % width;
        int ly = worldY % height;

        if (lx < 0) { lx += width; cx--; }
        if (ly < 0) { ly += height; cy--; }

        if (cx < 0 || cx >= MapManager.Instance.activeMapData.mapSize.x || cy < 0 || cy >= MapManager.Instance.activeMapData.mapSize.y) return false;
        
        return MapManager.Instance.activeMapData.chunks[cx, cy].blocks[lx, ly].isActive;
    }

    #endregion
}
