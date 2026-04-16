using UnityEngine;
using UnityEditor;
using UnityEditor.AddressableAssets;
using UnityEditor.AddressableAssets.Settings;
using System.Collections.Generic;
using System.Linq;
using System.IO;

public class BakerForTextureArray : EditorWindow
{
    private string spritePath = "Assets/Sprites/Tiles";
    private string outputPath = "Assets/Datas/Tileset/TilesetArray.asset";
    private const string ADDRESSABLE_GROUP_NAME = "GlobalDatas";
    private const string ASSET_ADDRESS = "TilesetArray";

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
        // 0. Addressable Settings 확인
        var settings = AddressableAssetSettingsDefaultObject.Settings;
        if (settings == null)
        {
            Debug.LogError("[Baker] Addressable settings not found.");
            return;
        }

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

        // 2. Determine dimensions and total layers
        int rulesCount = 47;
        int variations = 3;
        int spritesPerTile = rulesCount * variations;
        int maxTileId = tileTextures.Keys.Max();
        int totalLayers = (maxTileId + 1) * spritesPerTile;
        int spriteSize = 16;
        
        Debug.Log($"Baking TextureArray: MaxTileID={maxTileId}, SpritesPerTile={spritesPerTile}, TotalLayers={totalLayers}");

        Texture2DArray texArray = new Texture2DArray(spriteSize, spriteSize, totalLayers, TextureFormat.RGBA32, false);
        texArray.filterMode = FilterMode.Point;
        texArray.wrapMode = TextureWrapMode.Clamp;

        // 3. Fill pixels
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

        // 5. [Addressable] 자동으로 등록 (이름 충돌 방지를 위해 assetGuid, addressableEntry 사용)
        string assetGuid = AssetDatabase.AssetPathToGUID(outputPath);
        var group = settings.FindGroup(ADDRESSABLE_GROUP_NAME);
        if (group == null) group = settings.CreateGroup(ADDRESSABLE_GROUP_NAME, false, false, true, null);
        
        var addressableEntry = settings.CreateOrMoveEntry(assetGuid, group);
        addressableEntry.address = ASSET_ADDRESS;
        
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        
        Debug.Log($"Successfully baked to {outputPath} and registered to Addressables as '{ASSET_ADDRESS}'");
    }
}
