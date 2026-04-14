using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

#region Tile Sprite

[Serializable]
public class TileSpriteSet
{
    // 256 combinations of neighbors mapped to 0~46 RuleID
    private static int[] maskToRuleID = new int[256];
    private static bool isMappingInitialized = false;

    public static int[] GetRawMappingArray()
    {
        if (!isMappingInitialized) InitializeMapping();
        return maskToRuleID;
    }

    public static void InitializeMapping()
    {
        if (isMappingInitialized) return;

        // Initialize with default (Rule 0: No neighbors)
        for (int i = 0; i < 256; i++) maskToRuleID[i] = 0;

        TextAsset csvData = Resources.Load<TextAsset>("Sprites/Rule_TileIndex");
        if (csvData == null)
        {
            Debug.LogError("[ResourceManager] Rule_TileIndex.csv not found!");
            return;
        }

        string[] lines = csvData.text.Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.RemoveEmptyEntries);
        for (int i = 1; i < lines.Length; i++)
        {
            string line = lines[i].Trim();
            if (string.IsNullOrWhiteSpace(line)) continue;
            string[] parts = line.Split(',');
            if (parts.Length < 1 || !int.TryParse(parts[0], out int ruleId)) continue;

            int orthoMask = 0;
            int diagMissingMask = 0;

            // Orthogonal (Exist: 2,4,6,8)
            if (parts.Length > 1 && !string.IsNullOrWhiteSpace(parts[1]) && parts[1] != "0")
            {
                foreach (var o in parts[1].Trim().Split(' '))
                {
                    if (o == "2") orthoMask |= (1 << 0); // Top
                    if (o == "4") orthoMask |= (1 << 1); // Left
                    if (o == "6") orthoMask |= (1 << 2); // Right
                    if (o == "8") orthoMask |= (1 << 3); // Bottom
                }
            }

            // Diagonal (Missing: 1,3,7,9)
            if (parts.Length > 2 && !string.IsNullOrWhiteSpace(parts[2]))
            {
                foreach (var d in parts[2].Trim().Split(' '))
                {
                    if (d == "1") diagMissingMask |= (1 << 4); // TL
                    if (d == "3") diagMissingMask |= (1 << 5); // TR
                    if (d == "7") diagMissingMask |= (1 << 6); // BL
                    if (d == "9") diagMissingMask |= (1 << 7); // BR
                }
            }

            // Exactly map this specific signature
            maskToRuleID[orthoMask | diagMissingMask] = ruleId;
        }
        isMappingInitialized = true;
    }

    public static int GetRuleID(int bitmask)
    {
        if (!isMappingInitialized) InitializeMapping();
        return maskToRuleID[bitmask & 0xFF];
    }
}

#endregion

public class ResourceManager : PermanentSingleton<ResourceManager>
{
    #region Variable

    [Header("### Tile")]
    [SerializeField] private Texture2DArray tilesetArray;
    
    // Variables to store all sprites for quick access
    // TileID -> Sprite[141] (47 rules * 3 variations)
    private Dictionary<int, Sprite[]> tileSpriteCache = new Dictionary<int, Sprite[]>();

    #endregion

    #region MonoBehaviour

    protected override void Awake()
    {
        base.Awake();
        Init();
    }

    #endregion

    #region Init

    public void Init()
    {
        TileSpriteSet.InitializeMapping();
        LoadAllTileSprites();

        if (tilesetArray == null)
            tilesetArray = Resources.Load<Texture2DArray>("Text2DArray/TilesetArray");
    }

    private void LoadAllTileSprites()
    {
        tileSpriteCache.Clear();
        Sprite[] allSprites = Resources.LoadAll<Sprite>("Sprites/Tiles");
        
        // Group sprites by TileID (Tile_0000 -> 0)
        var grouped = allSprites.GroupBy(s => {
            string[] parts = s.name.Split('_');
            if (parts.Length >= 2 && int.TryParse(parts[1], out int id)) return id;
            return -1;
        });

        foreach (var group in grouped)
        {
            if (group.Key == -1) continue;
            // Robust numeric sorting by the last part of the name
            tileSpriteCache[group.Key] = group.OrderBy(s => {
                string[] parts = s.name.Split('_');
                if (parts.Length >= 3 && int.TryParse(parts[2], out int idx)) return idx;
                return 999;
            }).ToArray();
        }
        
        Debug.Log($"[ResourceManager] Cached {tileSpriteCache.Count} tile sets.");
    }

    #endregion
    
    #region Tile Access

    public float GetTileArrayIndex(int tileId, int bitmask, int variation)
    {
        int ruleId = TileSpriteSet.GetRuleID(bitmask);
        return (tileId * 141) + (ruleId * 3) + (variation % 3);
    }

    public Sprite GetTileSprite(int tileId, int bitmask, int variation)
    {
        if (!tileSpriteCache.TryGetValue(tileId, out Sprite[] sprites)) return null;
        
        int ruleId = TileSpriteSet.GetRuleID(bitmask);
        int index = (ruleId * 3) + (variation % 3);
        
        return (index >= 0 && index < sprites.Length) ? sprites[index] : null;
    }

    /// <summary>
    /// Returns the number of variations for a given tile ID.
    /// Now fixed to 3 as per new rules.
    /// </summary>
    public int GetTileKindCount(int tileId)
    {
        return 3;
    }

    #endregion

    #region Armor Sprite

    public Sprite[] GetBodyPartSprites(string partName, int id = -1)
    {
        string path = $"Sprites/Bodies/{partName}";
        if (id != -1) path += $"_{id:D3}";
        
        Sprite[] sprites = Resources.LoadAll<Sprite>(path);
        if (sprites == null || sprites.Length == 0) return null;

        Sprite[] result = new Sprite[12];
        foreach (var s in sprites)
        {
            string[] parts = s.name.Split('_');
            if (parts.Length >= 2 && int.TryParse(parts[parts.Length - 1], out int idx))
            {
                if (idx >= 0 && idx < 12) result[idx] = s;
            }
        }
        return result;
    }

    public Sprite[] GetArmorSprites(string category, int id)
    {
        string filePrefix = category.Equals("Clothes", StringComparison.OrdinalIgnoreCase) ? "Cloth" : 
                           (category.EndsWith("s") ? category.Substring(0, category.Length - 1) : category);

        string path = $"Sprites/Armors/{category}/{filePrefix}_{id:D4}";
        Sprite[] sprites = Resources.LoadAll<Sprite>(path);
        if (sprites == null || sprites.Length == 0) return null;
        
        Sprite[] result = new Sprite[12];
        foreach (var s in sprites)
        {
            string[] parts = s.name.Split('_');
            if (parts.Length >= 2 && int.TryParse(parts[parts.Length - 1], out int idx))
            {
                if (idx >= 0 && idx < 12) result[idx] = s;
            }
        }
        return result;
    }

    #endregion
}
