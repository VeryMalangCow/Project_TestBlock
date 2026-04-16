using System;
using UnityEngine;

#region Tile Sprite

[Serializable]
public class TileSpriteSet
{
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

        for (int i = 0; i < 256; i++) maskToRuleID[i] = 0;

        // [Note] 이 부분도 나중에 어드레서블로 옮길 수 있습니다.
        TextAsset csvData = Resources.Load<TextAsset>("Sprites/Rule_TileIndex");
        if (csvData == null)
        {
            Debug.LogError("[ResourceManager] Rule_TileIndex.csv not found in Resources!");
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

            if (parts.Length > 1 && !string.IsNullOrWhiteSpace(parts[1]) && parts[1] != "0")
            {
                foreach (var o in parts[1].Trim().Split(' '))
                {
                    if (o == "2") orthoMask |= (1 << 0);
                    if (o == "4") orthoMask |= (1 << 1);
                    if (o == "6") orthoMask |= (1 << 2);
                    if (o == "8") orthoMask |= (1 << 3);
                }
            }

            if (parts.Length > 2 && !string.IsNullOrWhiteSpace(parts[2]))
            {
                foreach (var d in parts[2].Trim().Split(' '))
                {
                    if (d == "1") diagMissingMask |= (1 << 4);
                    if (d == "3") diagMissingMask |= (1 << 5);
                    if (d == "7") diagMissingMask |= (1 << 6);
                    if (d == "9") diagMissingMask |= (1 << 7);
                }
            }

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
    }

    #endregion
    
    #region Tile Access

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
