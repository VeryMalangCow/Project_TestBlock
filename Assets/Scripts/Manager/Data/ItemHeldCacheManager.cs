using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.ResourceManagement.AsyncOperations;

public class ItemHeldCacheManager : PermanentSingleton<ItemHeldCacheManager>
{
    private const int CACHE_SIZE = 256; 
    private const int HELD_SIZE = 64;   // 들고 있는 아이템 규격 (64x64)
    private const int ICON_SIZE = 48;   // 기존 아이콘 규격 (48x48)
    
    [Header("# Cache Settings")]
    [SerializeField] private Texture2DArray heldArray;
    [SerializeField] private Material itemHeldMaterial; // 월드에서 공유할 머티리얼
    
    // Key: ItemID, Value: SlotIndex
    private Dictionary<int, int> itemToSlot = new Dictionary<int, int>();
    private Dictionary<int, int> slotToItem = new Dictionary<int, int>();
    private List<int> lruList = new List<int>(); 
    
    private Dictionary<int, AsyncOperationHandle<Texture2D>> loadingHandles = new Dictionary<int, AsyncOperationHandle<Texture2D>>();
    
    private Texture2D placeholderTex;

    public Material ItemHeldMaterial => itemHeldMaterial;

    protected override void Awake()
    {
        base.Awake();
        InitCache();
    }

    private void InitCache()
    {
        heldArray = new Texture2DArray(HELD_SIZE, HELD_SIZE, CACHE_SIZE, TextureFormat.RGBA32, false);
        heldArray.filterMode = FilterMode.Point;
        heldArray.wrapMode = TextureWrapMode.Clamp;
        
        placeholderTex = new Texture2D(HELD_SIZE, HELD_SIZE, TextureFormat.RGBA32, false);
        Color[] pColors = new Color[HELD_SIZE * HELD_SIZE];
        for(int i=0; i<pColors.Length; i++) pColors[i] = new Color(0, 0, 0, 0); 
        placeholderTex.SetPixels(pColors);
        placeholderTex.Apply();

        if (itemHeldMaterial != null)
        {
            itemHeldMaterial.SetTexture("_MainTexArray", heldArray);
        }

        for (int i = 0; i < CACHE_SIZE; i++)
        {
            Graphics.CopyTexture(placeholderTex, 0, 0, heldArray, i, 0);
            lruList.Add(i);
        }
    }

    public event Action<int> OnHeldIconLoaded;

    public int GetSlotIndex(int itemId)
    {
        if (itemId < 0) return -1;
        
        if (itemToSlot.TryGetValue(itemId, out int slot))
        {
            lruList.Remove(slot);
            lruList.Add(slot);
            return slot;
        }

        RequestHeldLoad(itemId);
        return -1; 
    }

    private void RequestHeldLoad(int itemId)
    {
        if (loadingHandles.ContainsKey(itemId)) return;

        ItemData data = ItemDataManager.Instance.GetItem(itemId);
        if (data == null) return;

        string address = data.hasHeldSprite ? $"ItemHeld_{itemId:D5}" : $"ItemIcon_{itemId:D5}";
        var handle = Addressables.LoadAssetAsync<Texture2D>(address);
        loadingHandles[itemId] = handle;

        handle.Completed += (op) =>
        {
            if (op.Status == AsyncOperationStatus.Succeeded)
            {
                int slot = AllocateSlot(itemId);
                Texture2D source = op.Result;

                if (data.hasHeldSprite)
                {
                    // 전용 이미지는 그대로 복사 (최대 64x64)
                    Graphics.CopyTexture(source, 0, 0, 0, 0, source.width, source.height, heldArray, slot, 0, 0, 0);
                }
                else
                {
                    // 아이콘(48x48)은 64x64 캔버스의 (0,0) 위치에 복사
                    // 먼저 해당 슬롯을 투명하게 초기화 (이전 잔상 제거)
                    Graphics.CopyTexture(placeholderTex, 0, 0, heldArray, slot, 0);
                    // (0,0) 위치에 48x48 복사
                    Graphics.CopyTexture(source, 0, 0, 0, 0, source.width, source.height, heldArray, slot, 0, 0, 0);
                }

                OnHeldIconLoaded?.Invoke(itemId);
            }
            loadingHandles.Remove(itemId);
        };
    }

    private int AllocateSlot(int itemId)
    {
        int slot = lruList[0];
        lruList.RemoveAt(0);
        lruList.Add(slot);

        if (slotToItem.TryGetValue(slot, out int oldItemId))
        {
            itemToSlot.Remove(oldItemId);
        }

        itemToSlot[itemId] = slot;
        slotToItem[slot] = itemId;
        
        return slot;
    }

    private void OnDestroy()
    {
        if (heldArray != null)
        {
            if (Application.isPlaying) Destroy(heldArray);
            else DestroyImmediate(heldArray);
        }
        if (placeholderTex != null)
        {
            if (Application.isPlaying) Destroy(placeholderTex);
            else DestroyImmediate(placeholderTex);
        }
    }
}
