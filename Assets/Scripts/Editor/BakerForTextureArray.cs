using UnityEngine;
using UnityEditor;
using UnityEditor.AddressableAssets;
using System.Collections.Generic;
using System.Linq;
using System.IO;

public class BakerForTextureArray : EditorWindow
{
    private string spritePath = "Assets/Sprites/Tiles";
    private string outputFolder = "Assets/Datas/Tileset/Individual";
    private const string ADDRESSABLE_GROUP_NAME = "TileAtlases";

    [MenuItem("Tools/Project/Texture2D Baker/Tile Atlas Baker")]
    public static void ShowWindow()
    {
        GetWindow<BakerForTextureArray>("Tile Atlas Baker");
    }

    private void OnGUI()
    {
        GUILayout.Label("Individual Tile Atlas Baker (128x128 per Variation)", EditorStyles.boldLabel);
        spritePath = EditorGUILayout.TextField("Sprite Path", spritePath);
        outputFolder = EditorGUILayout.TextField("Output Folder", outputFolder);

        if (GUILayout.Button("Bake Individual Atlases"))
        {
            Bake();
        }
    }

    private void Bake()
    {
        // 0. Addressable Settings 확인
        var settings = AddressableAssetSettingsDefaultObject.Settings;
        if (settings == null)
        {
            Debug.LogError("[Baker] Addressable settings not found.");
            return;
        }

        if (!Directory.Exists(outputFolder)) Directory.CreateDirectory(outputFolder);

        // 1. Find all textures matching Tile_ID format
        string[] textureGuids = AssetDatabase.FindAssets("t:Texture2D", new[] { spritePath });
        SortedDictionary<int, string> tileTextures = new SortedDictionary<int, string>();

        foreach (var guid in textureGuids)
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

        int rulesCount = 47;
        int variations = 3;
        int atlasSize = 128;
        int spriteSize = 16;
        int gridCount = 8; // 8x8 grid in 128x128 atlas

        var group = settings.FindGroup(ADDRESSABLE_GROUP_NAME);
        if (group == null) group = settings.CreateGroup(ADDRESSABLE_GROUP_NAME, false, false, true, null);

        foreach (var dictEntry in tileTextures)
        {
            int tileId = dictEntry.Key;
            string path = dictEntry.Value;
            
            Sprite[] sprites = AssetDatabase.LoadAllAssetsAtPath(path)
                .OfType<Sprite>()
                .OrderBy(s => {
                    string[] parts = s.name.Split('_');
                    if (parts.Length >= 3 && int.TryParse(parts[2], out int idx)) return idx;
                    return 999;
                })
                .ToArray();

            for (int v = 0; v < variations; v++)
            {
                Texture2D atlas = new Texture2D(atlasSize, atlasSize, TextureFormat.RGBA32, false);
                atlas.filterMode = FilterMode.Point;
                
                // Fill with transparent
                Color[] clearPixels = new Color[atlasSize * atlasSize];
                atlas.SetPixels(clearPixels);

                for (int r = 0; r < rulesCount; r++)
                {
                    int spriteIdx = (r * variations) + v;
                    if (spriteIdx < sprites.Length)
                    {
                        Sprite s = sprites[spriteIdx];
                        Rect rect = s.rect;
                        Color[] pixels = s.texture.GetPixels((int)rect.x, (int)rect.y, (int)rect.width, (int)rect.height);
                        
                        // Calculate grid position (Row starts from bottom in Unity Get/SetPixels)
                        int gridX = r % gridCount;
                        int gridY = (gridCount - 1) - (r / gridCount);
                        
                        atlas.SetPixels(gridX * spriteSize, gridY * spriteSize, spriteSize, spriteSize, pixels);
                    }
                }
                atlas.Apply();

                // Save
                string fileName = $"TileAtlas_{tileId:D4}_{v}.png";
                string fullPath = Path.Combine(outputFolder, fileName);
                byte[] bytes = atlas.EncodeToPNG();
                File.WriteAllBytes(fullPath, bytes);
                AssetDatabase.ImportAsset(fullPath);

                // Set Texture Settings
                TextureImporter importer = AssetImporter.GetAtPath(fullPath) as TextureImporter;
                if (importer != null)
                {
                    importer.textureType = TextureImporterType.Default;
                    importer.maxTextureSize = 128;
                    importer.filterMode = FilterMode.Point;
                    importer.textureCompression = TextureImporterCompression.Uncompressed;
                    importer.sRGBTexture = true;
                    importer.alphaIsTransparency = true;
                    importer.mipmapEnabled = false;
                    importer.isReadable = true; // Required for Graphics.CopyTexture
                    
                    // --- Platform Specific Overrides ---
                    // Default/Standalone (Windows, Mac, Linux)
                    TextureImporterPlatformSettings standaloneSettings = new TextureImporterPlatformSettings();
                    standaloneSettings.name = "Standalone";
                    standaloneSettings.overridden = true;
                    standaloneSettings.maxTextureSize = 128;
                    standaloneSettings.format = TextureImporterFormat.RGBA32; // Standard for Pixel Art Quality
                    standaloneSettings.textureCompression = TextureImporterCompression.Uncompressed;
                    importer.SetPlatformTextureSettings(standaloneSettings);

                    // Android
                    TextureImporterPlatformSettings androidSettings = new TextureImporterPlatformSettings();
                    androidSettings.name = "Android";
                    androidSettings.overridden = true;
                    androidSettings.maxTextureSize = 128;
                    androidSettings.format = TextureImporterFormat.RGBA32; // Keeping RGBA32 for CopyTexture compatibility
                    androidSettings.textureCompression = TextureImporterCompression.Uncompressed;
                    importer.SetPlatformTextureSettings(androidSettings);

                    // iPhone (iOS)
                    TextureImporterPlatformSettings iosSettings = new TextureImporterPlatformSettings();
                    iosSettings.name = "iPhone";
                    iosSettings.overridden = true;
                    iosSettings.maxTextureSize = 128;
                    iosSettings.format = TextureImporterFormat.RGBA32;
                    iosSettings.textureCompression = TextureImporterCompression.Uncompressed;
                    importer.SetPlatformTextureSettings(iosSettings);

                    EditorUtility.SetDirty(importer);
                    importer.SaveAndReimport();
                }

                // Register Addressable
                string assetGuid = AssetDatabase.AssetPathToGUID(fullPath);
                string address = $"TileAtlas_{tileId:D4}_{v}";
                var entry = settings.CreateOrMoveEntry(assetGuid, group);
                entry.address = address;
            }
        }

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        Debug.Log($"Successfully baked individual atlases to {outputFolder} and registered to Addressables.");
    }
}
