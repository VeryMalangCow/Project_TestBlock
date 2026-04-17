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
    
    private Dictionary<int, AsyncOperationHandle<Texture2D>> loadingHandles = new Dictionary<int, AsyncOperationHandle<Texture2D>>();
    
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

        // Not in cache, start loading
        RequestTileLoad(tileId, variation);
        return -1; // -1 means loading/not ready
    }

    private void RequestTileLoad(int tileId, int variation)
    {
        int key = (tileId << 2) | variation;
        if (loadingHandles.ContainsKey(key)) return;

        string address = $"TileAtlas_{tileId:D4}_{variation}";
        var handle = Addressables.LoadAssetAsync<Texture2D>(address);
        loadingHandles[key] = handle;

        handle.Completed += (op) =>
        {
            if (op.Status == AsyncOperationStatus.Succeeded)
            {
                int slot = AllocateSlot(key);
                Graphics.CopyTexture(op.Result, 0, 0, cacheArray, slot, 0);
                // We keep the handle or let Addressables cache it? 
                // For now, we don't release immediately to avoid flickering.
            }
            loadingHandles.Remove(key);
        };
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
