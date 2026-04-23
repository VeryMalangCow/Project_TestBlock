using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.ResourceManagement.AsyncOperations;

public class ItemIconCacheManager : PermanentSingleton<ItemIconCacheManager>
{
    private const int CACHE_SIZE = 256; // 동시 표현 가능한 아이콘 수
    private const int ICON_SIZE = 48;   // 요청하신 48x48 사이즈
    
    [Header("# Cache Settings")]
    [SerializeField] private Texture2DArray iconArray;
    [SerializeField] private Material itemIconMaterial; // UI에서 공유할 머티리얼
    
    public Texture2DArray IconArray => iconArray;

    // Key: ItemID, Value: SlotIndex
    private Dictionary<int, int> itemToSlot = new Dictionary<int, int>();
    private Dictionary<int, int> slotToItem = new Dictionary<int, int>();
    private List<int> lruList = new List<int>(); 
    
    private Dictionary<int, AsyncOperationHandle<Texture2D>> loadingHandles = new Dictionary<int, AsyncOperationHandle<Texture2D>>();
    
    // 로딩 중이거나 에러 시 보여줄 기본 텍스처
    private Texture2D placeholderTex;

    public Material ItemIconMaterial => itemIconMaterial;

    protected override void Awake()
    {
        base.Awake();
        InitCache();
    }

    private void InitCache()
    {
        // UI용 Texture2DArray 생성
        iconArray = new Texture2DArray(ICON_SIZE, ICON_SIZE, CACHE_SIZE, TextureFormat.RGBA32, false);
        iconArray.filterMode = FilterMode.Point;
        iconArray.wrapMode = TextureWrapMode.Clamp;
        
        // 투명한 플레이스홀더 생성
        placeholderTex = new Texture2D(ICON_SIZE, ICON_SIZE, TextureFormat.RGBA32, false);
        Color[] pColors = new Color[ICON_SIZE * ICON_SIZE];
        for(int i=0; i<pColors.Length; i++) pColors[i] = new Color(0, 0, 0, 0); 
        placeholderTex.SetPixels(pColors);
        placeholderTex.Apply();

        // [중요] 머티리얼에 Texture2DArray 연결
        if (itemIconMaterial != null)
        {
            itemIconMaterial.SetTexture("_MainTexArray", iconArray);
        }

        // 초기화: 모든 슬롯을 플레이스홀더로 채우고 LRU 리스트 구성
        for (int i = 0; i < CACHE_SIZE; i++)
        {
            Graphics.CopyTexture(placeholderTex, 0, 0, iconArray, i, 0);
            lruList.Add(i);
        }
    }

    public event Action<int> OnIconLoaded;

    public Texture2DArray GetIconArray() => iconArray;

    /// <summary>
    /// 아이템 아이콘이 캐시에 로드되어 즉시 사용 가능한지 확인합니다.
    /// </summary>
    public bool IsIconReady(int itemId)
    {
        return itemId < 0 || itemToSlot.ContainsKey(itemId);
    }

    /// <summary>
    /// 아이템 ID를 전달하면 해당 아이콘이 위치한 Texture2DArray의 슬롯 인덱스를 반환합니다.
    /// 로딩 중이면 -1을 반환합니다.
    /// </summary>
    public int GetSlotIndex(int itemId)
    {
        if (itemId < 0) return -1;
        
        if (itemToSlot.TryGetValue(itemId, out int slot))
        {
            // LRU 갱신: 방금 썼으므로 리스트의 맨 뒤로 이동
            lruList.Remove(slot);
            lruList.Add(slot);
            return slot;
        }

        // 캐시에 없으면 로드 요청
        RequestIconLoad(itemId);
        return -1; 
    }

    private void RequestIconLoad(int itemId)
    {
        if (loadingHandles.ContainsKey(itemId)) return;

        string address = $"ItemIcon_{itemId:D5}";
        var handle = Addressables.LoadAssetAsync<Texture2D>(address);
        loadingHandles[itemId] = handle;

        handle.Completed += (op) =>
        {
            if (op.Status == AsyncOperationStatus.Succeeded)
            {
                int slot = AllocateSlot(itemId);
                Texture2D source = op.Result;

                // [Fix] Graphics.CopyTexture는 규격이 일치해야 함. RenderTexture를 통한 안전한 복사 수행.
                RenderTexture rt = RenderTexture.GetTemporary(ICON_SIZE, ICON_SIZE, 0, RenderTextureFormat.ARGB32);
                RenderTexture.active = rt;
                GL.Clear(true, true, Color.clear);
                
                Graphics.Blit(source, rt);
                Graphics.CopyTexture(rt, 0, 0, 0, 0, ICON_SIZE, ICON_SIZE, iconArray, slot, 0, 0, 0);

                RenderTexture.active = null;
                RenderTexture.ReleaseTemporary(rt);

                // [신규] 로딩 완료 이벤트 발생
                OnIconLoaded?.Invoke(itemId);
            }
            loadingHandles.Remove(itemId);
        };
    }

    private int AllocateSlot(int itemId)
    {
        // LRU: 가장 오래된(맨 앞) 슬롯 추출
        int slot = lruList[0];
        lruList.RemoveAt(0);
        lruList.Add(slot);

        // 이전 매핑 정보 제거
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
        if (iconArray != null)
        {
            if (Application.isPlaying) Destroy(iconArray);
            else DestroyImmediate(iconArray);
        }

        if (placeholderTex != null)
        {
            if (Application.isPlaying) Destroy(placeholderTex);
            else DestroyImmediate(placeholderTex);
        }
    }
}
