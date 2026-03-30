using UnityEngine;
using UnityEditor;
using System.Collections.Generic;
using System.Linq;

public class TextureArrayBaker : EditorWindow
{
    private string spritePath = "Assets/Resources/Sprites/Tiles";
    private string outputPath = "Assets/Resources/Text2DArray/TilesetArray.asset";

    [MenuItem("Tools/Bake Tileset TextureArray")]
    public static void ShowWindow()
    {
        GetWindow<TextureArrayBaker>("Tileset Baker");
    }

    private void OnGUI()
    {
        GUILayout.Label("Tileset Texture2DArray Baker", EditorStyles.boldLabel);
        spritePath = EditorGUILayout.TextField("Sprite Path", spritePath);
        outputPath = EditorGUILayout.TextField("Output Path", outputPath);

        if (GUILayout.Button("Bake TextureArray"))
        {
            Bake();
        }
    }

    private void Bake()
    {
        // 1. Load all sprites
        string[] guids = AssetDatabase.FindAssets("t:Sprite", new[] { spritePath });
        List<Sprite> sprites = new List<Sprite>();
        foreach (var guid in guids)
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            sprites.AddRange(AssetDatabase.LoadAllAssetsAtPath(path).OfType<Sprite>());
        }

        if (sprites.Count == 0)
        {
            Debug.LogError("No sprites found at " + spritePath);
            return;
        }

        // 2. Group and Sort
        // Expected name format: Tile_TileID_KindID_SpriteIdx
        var grouped = new SortedDictionary<int, SortedDictionary<int, Sprite[]>>();

        foreach (var s in sprites)
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

        if (grouped.Count == 0)
        {
            Debug.LogError("No valid sprites found matching Tile_ID_Kind_Idx format.");
            return;
        }

        // 3. Determine dimensions and use fixed maxKinds
        // To keep the formula consistent, we use a fixed maxKinds (e.g., 10)
        // This must match ResourceManager.maxKinds
        int maxKinds = 10; 
        int maxTileId = grouped.Keys.Max();
        
        int totalLayers = (maxTileId + 1) * maxKinds * 16;
        Debug.Log($"Baking TextureArray: MaxTileID={maxTileId}, FixedMaxKinds={maxKinds}, TotalLayers={totalLayers}");

        // 4. Create Texture2DArray
        // Use the first sprite to get dimensions (8x8 expected)
        int width = (int)sprites[0].rect.width;
        int height = (int)sprites[0].rect.height;
        TextureFormat format = TextureFormat.RGBA32; 

        Texture2DArray texArray = new Texture2DArray(width, height, totalLayers, format, false);
        texArray.filterMode = FilterMode.Point;
        texArray.wrapMode = TextureWrapMode.Clamp;

        // 5. Fill pixels
        for (int t = 0; t <= maxTileId; t++)
        {
            if (!grouped.ContainsKey(t)) continue;

            foreach (var kindEntry in grouped[t])
            {
                int k = kindEntry.Key;
                for (int i = 0; i < 16; i++)
                {
                    Sprite s = kindEntry.Value[i];
                    if (s == null) continue;

                    int layerIdx = (t * maxKinds * 16) + (k * 16) + i;
                    
                    // Extract pixels from sprite's texture
                    Texture2D sourceTex = s.texture;
                    Rect r = s.rect;
                    Color[] pixels = sourceTex.GetPixels((int)r.x, (int)r.y, (int)r.width, (int)r.height);
                    
                    texArray.SetPixels(pixels, layerIdx);
                }
            }
        }

        texArray.Apply();

        // 6. Save Asset
        AssetDatabase.CreateAsset(texArray, outputPath);
        AssetDatabase.SaveAssets();
        
        // Save metadata to a text file or just log it for ResourceManager
        Debug.Log($"Successfully baked {totalLayers} layers to {outputPath}");
        Debug.Log($"Formula: Index = (TileID * {maxKinds} * 16) + (KindID * 16) + BitmaskIndex");
    }
}
