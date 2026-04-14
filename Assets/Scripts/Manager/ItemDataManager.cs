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
            if (parts.Length < 10) continue; 

            try
            {
                ItemData item = new ItemData();
                item.id = int.Parse(parts[0]);
                item.itemName = parts[1];
                item.description = parts[2];
                item.type = (ItemType)Enum.Parse(typeof(ItemType), parts[3], true);
                item.maxStack = int.Parse(parts[4]);
                item.iconPath = parts[5];
                item.value = int.Parse(parts[6]);
                item.damage = int.Parse(parts[7]);
                item.defense = int.Parse(parts[8]);
                item.useTime = float.Parse(parts[9]);

                // Load Icon Sprite
                // Default naming convention: Sprites/Items/Item_XXXXX (5 digits)
                string finalIconPath = string.IsNullOrEmpty(item.iconPath) 
                    ? $"Sprites/Items/Item_{item.id:D5}" 
                    : item.iconPath;

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

    /// <summary>
    /// Gets ItemData by its ID. Returns null if not found.
    /// </summary>
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
