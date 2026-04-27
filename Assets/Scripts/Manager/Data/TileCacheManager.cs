using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.ResourceManagement.AsyncOperations;

public class TileCacheManager : PermanentSingleton<TileCacheManager>
{
    private const int CACHE_SIZE = 512; // 동시 표현 가능한 타일 종류 수
    private const int ATLAS_SIZE = 128;
    
    [Header("# Cache Settings")]
    [HideInInspector] [SerializeField] private Texture2DArray cacheArray;
    
    // Key: (TileID << 2) | Variation
    private Dictionary<int, int> tileToSlot = new Dictionary<int, int>();
    private Dictionary<int, int> slotToKey = new Dictionary<int, int>();
    private List<int> lruList = new List<int>(); // Least Recently Used eviction
    
    // Placeholder (Loading/Error)
    private Texture2D placeholderTex;

    protected override void Awake()
    {
        base.Awake();
        InitCache();
    }

    private void InitCache()
    {
        cacheArray = new Texture2DArray(ATLAS_SIZE, ATLAS_SIZE, CACHE_SIZE, TextureFormat.RGBA32, false);
        cacheArray.filterMode = FilterMode.Point;
        cacheArray.wrapMode = TextureWrapMode.Clamp;
        
        placeholderTex = new Texture2D(ATLAS_SIZE, ATLAS_SIZE, TextureFormat.RGBA32, false);
        Color[] pColors = new Color[ATLAS_SIZE * ATLAS_SIZE];
        for(int i=0; i<pColors.Length; i++) pColors[i] = new Color(1, 0, 1, 0.2f); // Pinkish transparent
        placeholderTex.SetPixels(pColors);
        placeholderTex.Apply();

        // Fill cache with placeholder
        for (int i = 0; i < CACHE_SIZE; i++)
        {
            Graphics.CopyTexture(placeholderTex, 0, 0, cacheArray, i, 0);
            lruList.Add(i);
        }
    }

    public Texture2DArray GetCacheArray() => cacheArray;

    public int GetSlot(int tileId, int variation)
    {
        int key = (tileId << 2) | variation;
        
        if (tileToSlot.TryGetValue(key, out int slot))
        {
            // Update LRU
            lruList.Remove(slot);
            lruList.Add(slot);
            return slot;
        }

        // Not in cache, start Synchronous loading
        return LoadTileSync(tileId, variation);
    }

    private int LoadTileSync(int tileId, int variation)
    {
        int key = (tileId << 2) | variation;
        string address = $"TileAtlas_{tileId:D4}_{variation}";

        try
        {
            var handle = Addressables.LoadAssetAsync<Texture2D>(address);
            Texture2D tex = handle.WaitForCompletion();

            if (handle.Status == AsyncOperationStatus.Succeeded && tex != null)
            {
                int slot = AllocateSlot(key);
                Graphics.CopyTexture(tex, 0, 0, cacheArray, slot, 0);
                
                // Note: We don't release the handle here to ensure the texture stays in memory.
                // Addressables will handle overall memory management.
                return slot;
            }
            else
            {
                Debug.LogWarning($"[TileCacheManager] Failed to load tile sync: {address}");
                if (handle.IsValid()) Addressables.Release(handle);
                return 0; // Fallback to slot 0 (Placeholder)
            }
        }
        catch (System.Exception e)
        {
            Debug.LogError($"[TileCacheManager] Error sync-loading tile {address}: {e.Message}");
            return 0; // Fallback to slot 0
        }
    }

    private int AllocateSlot(int key)
    {
        // Use LRU: Take the first one (oldest used)
        int slot = lruList[0];
        lruList.RemoveAt(0);
        lruList.Add(slot);

        // Remove old mapping if exists
        if (slotToKey.TryGetValue(slot, out int oldKey))
        {
            tileToSlot.Remove(oldKey);
        }

        tileToSlot[key] = slot;
        slotToKey[slot] = key;
        
        return slot;
    }

    public bool IsTileReady(int tileId, int variation)
    {
        return tileToSlot.ContainsKey((tileId << 2) | variation);
    }
}
