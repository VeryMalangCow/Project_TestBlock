using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.ResourceManagement.AsyncOperations;

public class ItemDataManager : PermanentSingleton<ItemDataManager>
{
    private Dictionary<int, ItemData> itemCache = new Dictionary<int, ItemData>();
    
    // [Cache] 스프라이트와 핸들을 중앙에서 관리하여 중복 로드 방지 및 즉각 반환
    private Dictionary<int, AsyncOperationHandle<Sprite>> iconCache = new Dictionary<int, AsyncOperationHandle<Sprite>>();

    protected override void Awake()
    {
        base.Awake();
        LoadItemDatabase();
    }

    private void OnDestroy()
    {
        // 종료 시 모든 캐시된 에셋 일괄 해제
        foreach (var handle in iconCache.Values)
        {
            if (handle.IsValid()) Addressables.Release(handle);
        }
        iconCache.Clear();
    }

    private void LoadItemDatabase()
    {
        itemCache.Clear();

        // 1. [Addressable] 기반 데이터베이스 로드
        // 이제 Resources.Load를 쓰지 않고 주소("ItemDatabase")로 직접 불러옵니다.
        var handle = Addressables.LoadAssetAsync<ItemDatabase>("ItemDatabase");
        ItemDatabase database = handle.WaitForCompletion();
        
        if (database == null)
        {
            Debug.LogError("[ItemDataManager] Failed to load ItemDatabase SO via Addressables (Address: ItemDatabase).");
            return;
        }

        // 2. 캐시 구성 (ID 기반 빠른 검색용)
        foreach (var item in database.items)
        {
            if (item == null) continue;
            
            if (itemCache.ContainsKey(item.id))
            {
                Debug.LogWarning($"[ItemDataManager] Duplicate Item ID found: {item.id} ({item.itemName})");
                continue;
            }

            itemCache[item.id] = item;
        }

        Debug.Log($"[ItemDataManager] Successfully loaded {itemCache.Count} items from ScriptableObject.");
    }

    /// <summary>
    /// 아이템 아이콘을 즉시(동기식) 가져옵니다. 
    /// 캐시에 있다면 즉시 반환하고, 없다면 그 즉시 로드하여 저장합니다.
    /// </summary>
    public Sprite GetItemIcon(int id)
    {
        if (id < 0) return null;

        // 1. 이미 캐시에 로드된 경우 즉시 반환
        if (iconCache.TryGetValue(id, out var existingHandle))
        {
            if (existingHandle.IsValid() && existingHandle.Status == AsyncOperationStatus.Succeeded)
            {
                return existingHandle.Result;
            }
        }

        // 2. 캐시에 없거나 유효하지 않은 경우 즉시 로드 (Synchronous)
        try
        {
            string address = $"ItemIcon_{id:D5}";
            var handle = Addressables.LoadAssetAsync<Sprite>(address);
            
            // [핵심] 로드가 완료될 때까지 기다림 (0프레임 지연 실현)
            handle.WaitForCompletion();

            if (handle.Status == AsyncOperationStatus.Succeeded)
            {
                iconCache[id] = handle;
                return handle.Result;
            }
            else
            {
                Debug.LogWarning($"[ItemDataManager] Failed to sync-load icon for ID {id}");
                Addressables.Release(handle);
                return null;
            }
        }
        catch (Exception e)
        {
            Debug.LogError($"[ItemDataManager] Error loading icon {id}: {e.Message}");
            return null;
        }
    }

    public ItemData GetItem(int id)
    {
        if (itemCache.TryGetValue(id, out ItemData data)) return data;
        return null;
    }

    public List<ItemData> GetAllItems() => itemCache.Values.ToList();

    /// <summary>
    /// 특정 타입과 TypeID를 가진 첫 번째 아이템의 ID를 반환합니다.
    /// </summary>
    public int FindItemIDByType(ItemType type, int typeID)
    {
        if (typeID < 0) return -1;
        var item = itemCache.Values.FirstOrDefault(i => i.type == type && i.typeID == typeID);
        return item != null ? item.id : -1;
    }
}
