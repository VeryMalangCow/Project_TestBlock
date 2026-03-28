using System;
using System.Collections.Generic;
using UnityEngine;

#region Tile Sprite

[Serializable]
public class TileSpriteSet
{
    [SerializeField] private Sprite[] tileImages;

    // Bitmask (U=1, D=2, L=4, R=8) to tileImages index mapping
    // Swapped U and D indices to match Unity's Y-axis orientation
    private static readonly int[] maskToIndex = new int[]
    {
        12, // 0: None
        8,  // 1: U -> Was 10
        10, // 2: D -> Was 8
        15, // 3: UD
        11, // 4: L
        7,  // 5: UL -> Was 6
        6,  // 6: DL -> Was 7
        3,  // 7: UDL
        9,  // 8: R
        4,  // 9: UR -> Was 5
        5,  // 10: DR -> Was 4
        1,  // 11: UDR
        14, // 12: LR
        0,  // 13: ULR -> Was 2
        2,  // 14: DLR -> Was 0
        13  // 15: UDLR
    };

    public TileSpriteSet(Sprite[] sprites)
    {
        tileImages = sprites;
    }

    public static int GetBitmaskIndex(bool u, bool d, bool l, bool r)
    {
        int mask = 0;
        if (u) mask |= 1;
        if (d) mask |= 2;
        if (l) mask |= 4;
        if (r) mask |= 8;

        return maskToIndex[mask];
    }

    public Sprite GetSprite(bool u, bool d, bool l, bool r)
    {
        if (tileImages == null || tileImages.Length < 16) return null;

        int index = GetBitmaskIndex(u, d, l, r);
        return (index >= 0 && index < tileImages.Length) ? tileImages[index] : null;
    }
}

[Serializable]
public class RandomTileSpriteSet
{
    [SerializeField] private List<TileSpriteSet> tileSpriteSets;
    public int Count => (tileSpriteSets != null) ? tileSpriteSets.Count : 0;

    public RandomTileSpriteSet()
    {
        tileSpriteSets = new List<TileSpriteSet>();
    }

    public void AddTileSpriteSet(TileSpriteSet tileSpriteSet)
    {
        tileSpriteSets.Add(tileSpriteSet);
    }

    public Sprite GetSprite(bool u, bool d, bool l, bool r)
    {
        if (tileSpriteSets == null || tileSpriteSets.Count == 0) return null;
        
        // Pick a random variation set
        int randomIndex = UnityEngine.Random.Range(0, tileSpriteSets.Count);
        TileSpriteSet set = tileSpriteSets[randomIndex];
        
        return (set != null) ? set.GetSprite(u, d, l, r) : null;
    }
}

#endregion

public class ResourceManager : PermanentSingleton<ResourceManager>
{
    #region Variable

    [Header("### Tile")]
    [Header("## Render")]
    [Header("# Texture2DArray")]
    [SerializeField] private Texture2DArray tilesetArray;
    [SerializeField] private int maxKinds = 10; // Match this with Baker's maxKinds

    [Header("# Sprite")]
    [SerializeField] private List<RandomTileSpriteSet> allTileSpriteSets;

    #endregion

    #region Init

    public void Init()
    {
        allTileSpriteSets = InitTileSprites("Sprites/Tiles");
        
        // Load Texture2DArray from Resources if needed
        if (tilesetArray == null)
            tilesetArray = Resources.Load<Texture2DArray>("TilesetArray");
    }

    #endregion
    
    #region Tile Array Index

    /// <summary>
    /// Calculates the layer index for Texture2DArray
    /// </summary>
    public float GetTileArrayIndex(int tileId, int kindId, int bitmaskIdx)
    {
        // Formula matching the Baker script
        return (tileId * maxKinds * 16) + (kindId * 16) + bitmaskIdx;
    }

    /// <summary>
    /// Returns the number of variations (kinds) for a given tile ID.
    /// Used by MeshManager to pick a random kind.
    /// </summary>
    public int GetTileKindCount(int tileId)
    {
        if (tileId < 0 || tileId >= allTileSpriteSets.Count || allTileSpriteSets[tileId] == null)
            return 1;
        
        return allTileSpriteSets[tileId].Count;
    }

    #endregion
    
    #region Tile Sprite

    // Init
    private List<RandomTileSpriteSet> InitTileSprites(string path)
    {
        Sprite[] allSprites = Resources.LoadAll<Sprite>(path);

        if (allSprites == null || allSprites.Length == 0) return new List<RandomTileSpriteSet>();

        // tileId -> (kindId -> sprites[16])
        SortedDictionary<int, SortedDictionary<int, Sprite[]>> grouped = new SortedDictionary<int, SortedDictionary<int, Sprite[]>>();

        foreach (var s in allSprites)
        {
            string[] parts = s.name.Split('_');
            if (parts.Length >= 4 && parts[0] == "Tile")
            {
                if (int.TryParse(parts[1], out int tileId) &&
                    int.TryParse(parts[2], out int kindId) &&
                    int.TryParse(parts[3], out int spriteIdx))
                {
                    if (!grouped.ContainsKey(tileId))
                        grouped[tileId] = new SortedDictionary<int, Sprite[]>();
                    
                    if (!grouped[tileId].ContainsKey(kindId))
                        grouped[tileId][kindId] = new Sprite[16];

                    if (spriteIdx >= 0 && spriteIdx < 16)
                    {
                        grouped[tileId][kindId][spriteIdx] = s;
                    }
                }
            }
        }

        List<RandomTileSpriteSet> result = new List<RandomTileSpriteSet>();
        if (grouped.Count == 0) return result;

        int maxTileId = 0;
        foreach (var id in grouped.Keys) if (id > maxTileId) maxTileId = id;

        for (int i = 0; i <= maxTileId; i++)
        {
            if (grouped.TryGetValue(i, out var kinds))
            {
                RandomTileSpriteSet randomSet = new RandomTileSpriteSet();
                
                int maxKindId = 0;
                foreach (var kid in kinds.Keys) if (kid > maxKindId) maxKindId = kid;

                for (int j = 0; j <= maxKindId; j++)
                {
                    if (kinds.TryGetValue(j, out var sprites))
                    {
                        randomSet.AddTileSpriteSet(new TileSpriteSet(sprites));
                    }
                    else
                    {
                        randomSet.AddTileSpriteSet(null);
                    }
                }
                result.Add(randomSet);
            }
            else
            {
                result.Add(null);
            }
        }

        return result;
    }

    // Get
    public Sprite GetTileSprite(int id, bool u, bool d, bool l, bool r)
    {
        if (id < 0 || id >= allTileSpriteSets.Count || allTileSpriteSets[id] == null) return null;
        return allTileSpriteSets[id].GetSprite(u, d, l, r);
    }

    #endregion
}
