using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

public class ItemDataManager : PermanentSingleton<ItemDataManager>
{
    private Dictionary<int, ItemData> itemCache = new Dictionary<int, ItemData>();

    protected override void Awake()
    {
        base.Awake();
        LoadItemDatabase();
    }

    private void LoadItemDatabase()
    {
        itemCache.Clear();

        // 1. ScriptableObject 기반 데이터베이스 로드
        ItemDatabase database = Resources.Load<ItemDatabase>("Data/ItemDatabase");
        
        if (database == null)
        {
            Debug.LogError("[ItemDataManager] Failed to load ItemDatabase SO from Resources/Data/ItemDatabase.");
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

    public ItemData GetItem(int id)
    {
        if (itemCache.TryGetValue(id, out ItemData data)) return data;
        return null;
    }

    public List<ItemData> GetAllItems() => itemCache.Values.ToList();
}
