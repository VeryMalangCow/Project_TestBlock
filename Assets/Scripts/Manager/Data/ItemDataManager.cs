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

        TextAsset csvFile = Resources.Load<TextAsset>("Data/ItemDatabase");
        if (csvFile == null)
        {
            Debug.LogError("[ItemDataManager] Failed to load CSV from Resources/Data/ItemDatabase.csv.");
            return;
        }

        string[] lines = csvFile.text.Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.RemoveEmptyEntries);
        
        for (int i = 1; i < lines.Length; i++)
        {
            string line = lines[i].Trim();
            if (string.IsNullOrWhiteSpace(line)) continue;

            string[] parts = line.Split(',');
            if (parts.Length < 5) continue; 

            try
            {
                ItemData item = new ItemData();
                item.id = int.Parse(parts[0]);
                item.itemName = parts[1];
                item.description = parts[2];
                item.maxStack = int.Parse(parts[3]);
                item.type = (ItemType)Enum.Parse(typeof(ItemType), parts[4], true);
                
                // useTime이 CSV에 있다면 (6번째 컬럼)
                if (parts.Length >= 6)
                {
                    item.useTime = float.Parse(parts[5]);
                }

                // 슬라이스된 스프라이트일 수도 있으므로 LoadAll을 시도
                string finalPath = $"Sprites/Items/Item_{item.id:D5}";
                Sprite[] allSprites = Resources.LoadAll<Sprite>(finalPath);
                
                if (allSprites != null && allSprites.Length > 0)
                {
                    // 파일 내의 첫 번째 스프라이트를 할당
                    item.icon = allSprites[0];
                }
                else
                {
                    // 가변 길이 파일명도 시도 (예: Item_1)
                    allSprites = Resources.LoadAll<Sprite>($"Sprites/Items/Item_{item.id}");
                    if (allSprites != null && allSprites.Length > 0)
                    {
                        item.icon = allSprites[0];
                    }
                }
                
                if (item.id != -1 && item.icon == null)
                {
                    Debug.LogWarning($"[ItemDataManager] Sprite not found for Item ID {item.id} at {finalPath}");
                }

                itemCache[item.id] = item;
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[ItemDataManager] Parsing error at line {i}: {e.Message}");
            }
        }

        Debug.Log($"[ItemDataManager] Successfully loaded {itemCache.Count} items.");
    }

    public ItemData GetItem(int id)
    {
        if (itemCache.TryGetValue(id, out ItemData data)) return data;
        return null;
    }

    public List<ItemData> GetAllItems() => itemCache.Values.ToList();
}
