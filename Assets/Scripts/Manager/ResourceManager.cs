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
        10, // 1: U -> Maps to D sprite
        8,  // 2: D -> Maps to U sprite
        15, // 3: UD
        11, // 4: L
        6,  // 5: UL -> Maps to DL sprite
        7,  // 6: DL -> Maps to UL sprite
        3,  // 7: UDL
        9,  // 8: R
        5,  // 9: UR -> Maps to DR sprite
        4,  // 10: DR -> Maps to UR sprite
        1,  // 11: UDR
        14, // 12: LR
        2,  // 13: ULR -> Maps to DLR sprite
        0,  // 14: DLR -> Maps to ULR sprite
        13  // 15: UDLR
    };

    public TileSpriteSet(Sprite[] sprites)
    {
        tileImages = sprites;
    }

    public Sprite GetSprite(bool u, bool d, bool l, bool r)
    {
        if (tileImages == null || tileImages.Length < 16) return null;

        int mask = 0;
        if (u) mask |= 1;
        if (d) mask |= 2;
        if (l) mask |= 4;
        if (r) mask |= 8;

        int index = maskToIndex[mask];
        return (index >= 0 && index < tileImages.Length) ? tileImages[index] : null;
    }
}

[Serializable]
public class RandomTileSpriteSet
{
    [SerializeField] private List<TileSpriteSet> tileSpriteSets;

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

    [SerializeField] private List<RandomTileSpriteSet> allTileSpriteSets;

    #endregion

    #region Init

    public void Init()
    {
        allTileSpriteSets = InitTileSprites("Sprites/Tiles");
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
