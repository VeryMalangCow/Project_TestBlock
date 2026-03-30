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
    public void CalculateAllLighting()
    {
        if (MapManager.Instance == null || MapManager.Instance.activeMapData == null) return;

        int totalWidth = MapData.MapSize.x * ChunkData.ChunkSize.x;
        int totalHeight = MapData.MapSize.y * ChunkData.ChunkSize.y;

        lightQueue.Clear();

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
            }
        }

        // 2. Propagate
        SpreadLight();
    }

    /// <summary>
    /// Updates lighting in a local area around the changed block.
    /// </summary>
    public void UpdateLightingAt(int worldX, int worldY)
    {
        int range = 25; // Increased range for better spread
        int totalWidth = MapData.MapSize.x * ChunkData.ChunkSize.x;
        int totalHeight = MapData.MapSize.y * ChunkData.ChunkSize.y;

        lightQueue.Clear();

        // 1. Prepare local area and identify sources
        for (int x = worldX - range; x <= worldX + range; x++)
        {
            if (x < 0 || x >= totalWidth) continue;

            for (int y = worldY - range; y <= worldY + range; y++)
            {
                if (y < 0 || y >= totalHeight) continue;

                // Check if this tile should be sunlight source (Nothing is strictly ABOVE it)
                bool nothingAbove = true;
                for (int ty = totalHeight - 1; ty > y; ty--) // Checks ty > y
                {
                    if (HasBlock(x, ty)) { nothingAbove = false; break; }
                }

                if (nothingAbove)
                {
                    SetLightValue(x, y, maxLightIntensity);
                    lightQueue.Enqueue(new Vector2Int(x, y));
                }
                else
                {
                    // If it's the edge of our update range, use existing light to let it flow in
                    if (x == worldX - range || x == worldX + range || y == worldY - range || y == worldY + range)
                    {
                        byte currentVal = GetLightValue(x, y);
                        if (currentVal > 0) lightQueue.Enqueue(new Vector2Int(x, y));
                    }
                    else
                    {
                        // Reset internal non-sky tiles to 0 to allow recalculation
                        SetLightValue(x, y, 0);
                    }
                }
            }
        }

        // 2. Re-propagate from sources
        SpreadLight();
        
        // Redraw affected chunks
        RedrawAffectedChunks(worldX, worldY);
    }

    private void SpreadLight()
    {
        Vector2Int[] dirs = { Vector2Int.up, Vector2Int.down, Vector2Int.left, Vector2Int.right };
        int totalWidth = MapData.MapSize.x * ChunkData.ChunkSize.x;
        int totalHeight = MapData.MapSize.y * ChunkData.ChunkSize.y;

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
                }
            }
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

        if (cx < 0 || cx >= MapData.MapSize.x || cy < 0 || cy >= MapData.MapSize.y) return;

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

        if (cx < 0 || cx >= MapData.MapSize.x || cy < 0 || cy >= MapData.MapSize.y) return 0;
        
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

        if (cx < 0 || cx >= MapData.MapSize.x || cy < 0 || cy >= MapData.MapSize.y) return false;
        
        return MapManager.Instance.activeMapData.chunks[cx, cy].blocks[lx, ly].isActive;
    }

    private void RedrawAffectedChunks(int worldX, int worldY)
    {
        int cx = worldX / ChunkData.ChunkSize.x;
        int cy = worldY / ChunkData.ChunkSize.y;

        for (int x = -2; x <= 2; x++)
        {
            for (int y = -2; y <= 2; y++)
            {
                MeshManager.Instance.RequestChunkRedraw(cx + x, cy + y);
            }
        }
    }

    #endregion
}
