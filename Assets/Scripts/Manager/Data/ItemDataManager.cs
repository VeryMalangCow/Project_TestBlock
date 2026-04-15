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
            Debug.LogError("[ItemDataManager] Failed to load CSV from Resources/Data/ItemDatabase.csv");
            return;
        }

        string[] lines = csvFile.text.Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.RemoveEmptyEntries);
        
        // Skip header (i=0)
        for (int i = 1; i < lines.Length; i++)
        {
            string line = lines[i].Trim();
            if (string.IsNullOrWhiteSpace(line)) continue;

            string[] parts = line.Split(',');
            // CSV structure: ID(0), Name(1), Description(2), MaxStack(3), ItemType(4), UseTime(5)
            if (parts.Length < 6) continue; 

            try
            {
                ItemData item = new ItemData();
                item.id = int.Parse(parts[0]);
                item.itemName = parts[1];
                item.description = parts[2];
                item.maxStack = int.Parse(parts[3]);
                item.type = (ItemType)Enum.Parse(typeof(ItemType), parts[4], true);
                item.useTime = float.Parse(parts[5]);

                // Load Icon Sprite based on ID (Default naming convention: Sprites/Items/Item_XXXXX)
                string finalIconPath = $"Sprites/Items/Item_{item.id:D5}";
                item.icon = Resources.Load<Sprite>(finalIconPath);
                
                if (item.id != -1 && item.icon == null)
                {
                    Debug.LogWarning($"[ItemDataManager] Sprite not found at: {finalIconPath} for Item ID: {item.id}");
                }

                itemCache[item.id] = item;
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[ItemDataManager] Skipping line {i} due to parsing error: {e.Message}");
            }
        }

        Debug.Log($"[ItemDataManager] Successfully loaded {itemCache.Count} items from CSV.");
    }

    public ItemData GetItem(int id)
    {
        if (itemCache.TryGetValue(id, out ItemData data))
        {
            return data;
        }
        return null;
    }

    public List<ItemData> GetAllItems()
    {
        return itemCache.Values.ToList();
    }
}
