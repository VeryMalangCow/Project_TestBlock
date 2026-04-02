using UnityEngine;
using UnityEditor;
using System.Collections.Generic;
using System.Linq;
using System.IO;

public class BakerForTextureArray : EditorWindow
{
    private string spritePath = "Assets/Resources/Sprites/Tiles";
    private string outputPath = "Assets/Resources/Text2DArray/TilesetArray.asset";

    [MenuItem("Tools/Project/Texture2D Baker/Tile Texture2DArray")]
    public static void ShowWindow()
    {
        GetWindow<BakerForTextureArray>("Tileset Baker");
    }

    private void OnGUI()
    {
        GUILayout.Label("Tileset Texture2DArray Baker (47 Rules x 3 Variations)", EditorStyles.boldLabel);
        spritePath = EditorGUILayout.TextField("Sprite Path", spritePath);
        outputPath = EditorGUILayout.TextField("Output Path", outputPath);

        if (GUILayout.Button("Bake TextureArray"))
        {
            Bake();
        }
    }

    private void Bake()
    {
        // 1. Find all textures matching Tile_ID format
        string[] guids = AssetDatabase.FindAssets("t:Texture2D", new[] { spritePath });
        SortedDictionary<int, string> tileTextures = new SortedDictionary<int, string>();

        foreach (var guid in guids)
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            string name = Path.GetFileNameWithoutExtension(path);
            if (name.StartsWith("Tile_") && int.TryParse(name.Substring(5), out int tileId))
            {
                tileTextures[tileId] = path;
            }
        }

        if (tileTextures.Count == 0)
        {
            Debug.LogError("No textures matching Tile_ID format found at " + spritePath);
            return;
        }

        // 2. Determine dimensions and total layers
        // Each tile has 47 rules * 3 variations = 141 sprites
        int rulesCount = 47;
        int variations = 3;
        int spritesPerTile = rulesCount * variations;
        int maxTileId = tileTextures.Keys.Max();
        int totalLayers = (maxTileId + 1) * spritesPerTile;

        // Use the first texture to get dimensions
        string firstPath = tileTextures.Values.First();
        TextureImporter firstImporter = AssetImporter.GetAtPath(firstPath) as TextureImporter;
        firstImporter.GetSourceTextureWidthAndHeight(out int width, out int height);
        // We expect individual sprites to be 16x16 as per rule
        int spriteSize = 16;
        
        Debug.Log($"Baking TextureArray: MaxTileID={maxTileId}, SpritesPerTile={spritesPerTile}, TotalLayers={totalLayers}");

        Texture2DArray texArray = new Texture2DArray(spriteSize, spriteSize, totalLayers, TextureFormat.RGBA32, false);
        texArray.filterMode = FilterMode.Point;
        texArray.wrapMode = TextureWrapMode.Clamp;

        // 3. Fill pixels
        foreach (var entry in tileTextures)
        {
            int tileId = entry.Key;
            string path = entry.Value;
            
            // Load all sprites from this texture, sorted numerically by their suffix (e.g., _000, _001)
            Sprite[] sprites = AssetDatabase.LoadAllAssetsAtPath(path)
                .OfType<Sprite>()
                .OrderBy(s => {
                    string[] parts = s.name.Split('_');
                    if (parts.Length >= 3 && int.TryParse(parts[2], out int idx)) return idx;
                    return 999;
                })
                .ToArray();
            
            if (sprites.Length < spritesPerTile)
            {
                Debug.LogWarning($"Tile_{tileId:D4} at {path} has only {sprites.Length} sprites, expected {spritesPerTile}. Filling remaining with empty.");
            }

            for (int i = 0; i < spritesPerTile; i++)
            {
                int layerIdx = (tileId * spritesPerTile) + i;
                
                if (i < sprites.Length)
                {
                    Sprite s = sprites[i];
                    Texture2D sourceTex = s.texture;
                    Rect r = s.rect;
                    Color[] pixels = sourceTex.GetPixels((int)r.x, (int)r.y, (int)r.width, (int)r.height);
                    texArray.SetPixels(pixels, layerIdx);
                }
                else
                {
                    // Empty pixels for missing sprites
                    texArray.SetPixels(new Color[spriteSize * spriteSize], layerIdx);
                }
            }
        }

        texArray.Apply();

        // 4. Save Asset
        string dir = Path.GetDirectoryName(outputPath);
        if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
        
        AssetDatabase.CreateAsset(texArray, outputPath);
        AssetDatabase.SaveAssets();
        
        Debug.Log($"Successfully baked {totalLayers} layers to {outputPath}");
        Debug.Log($"Formula: Index = (TileID * 141) + (RuleID * 3) + VariationIdx");
    }
}
