using UnityEngine;
using UnityEditor;
using UnityEditor.U2D.Sprites;
using System.Collections.Generic;
using System.IO;

public class TileSpriteProcessor : Editor
{
    private const string TileSpritePath = "Assets/Resources/Sprites/Tiles";

    [MenuItem("Tools/Project_BlockTest/Process All Tile Sprites")]
    public static void ProcessAllTileSprites()
    {
        string[] guids = AssetDatabase.FindAssets("t:Texture2D", new[] { TileSpritePath });
        int count = 0;

        foreach (string guid in guids)
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            ProcessTileTexture(path);
            count++;
        }

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        Debug.Log($"[TileSpriteProcessor] Successfully processed {count} tile textures.");
    }

    private static void ProcessTileTexture(string path)
    {
        TextureImporter importer = AssetImporter.GetAtPath(path) as TextureImporter;
        if (importer == null) return;

        // 1. Initial Setup to make it readable
        importer.textureType = TextureImporterType.Sprite;
        importer.spriteImportMode = SpriteImportMode.Multiple;
        importer.isReadable = true;
        importer.filterMode = FilterMode.Point;
        importer.textureCompression = TextureImporterCompression.Uncompressed;
        importer.maxTextureSize = 128;
        importer.alphaIsTransparency = true;
        importer.sRGBTexture = true;
        
        TextureImporterSettings settings = new TextureImporterSettings();
        importer.ReadTextureSettings(settings);
        settings.spriteMeshType = SpriteMeshType.FullRect;
        settings.spriteGenerateFallbackPhysicsShape = false;
        importer.SetTextureSettings(settings);
        
        importer.SaveAndReimport();

        // 2. Load texture to check pixels
        byte[] fileData = File.ReadAllBytes(path);
        Texture2D tex = new Texture2D(2, 2);
        tex.LoadImage(fileData);

        // 3. Slicing Settings
        var factory = new SpriteDataProviderFactories();
        factory.Init();
        var dataProvider = factory.GetSpriteEditorDataProviderFromObject(importer);
        dataProvider.InitSpriteEditorDataProvider();

        int cellWidth = 8;
        int cellHeight = 8;
        int offsetX = 0;
        int offsetY = 0;
        int paddingX = 1;
        int paddingY = 1;

        int width = tex.width;
        int height = tex.height;

        List<SpriteRect> spriteRects = new List<SpriteRect>();
        string fileName = Path.GetFileNameWithoutExtension(path);

        int index = 0;
        // Slice from Top-Left to Bottom-Right
        for (int y = height - cellHeight - offsetY; y >= 0; y -= (cellHeight + paddingY))
        {
            for (int x = offsetX; x <= width - cellWidth; x += (cellWidth + paddingX))
            {
                // Only create sprite if the 8x8 area is NOT empty
                if (!IsRectEmpty(tex, x, y, cellWidth, cellHeight))
                {
                    SpriteRect rect = new SpriteRect();
                    rect.rect = new Rect(x, y, cellWidth, cellHeight);
                    rect.alignment = SpriteAlignment.Center;
                    rect.name = $"{fileName}_{index:D3}"; // Maintain index based on grid position
                    rect.pivot = new Vector2(0.5f, 0.5f);
                    rect.spriteID = GUID.Generate();
                    spriteRects.Add(rect);
                }
                index++;
            }
        }

        dataProvider.SetSpriteRects(spriteRects.ToArray());
        dataProvider.Apply();

        importer.SaveAndReimport();
        
        // Cleanup temp texture
        DestroyImmediate(tex);
    }

    private static bool IsRectEmpty(Texture2D tex, int x, int y, int width, int height)
    {
        Color[] pixels = tex.GetPixels(x, y, width, height);
        foreach (var p in pixels)
        {
            if (p.a > 0.05f) return false; // Found a pixel!
        }
        return true; // Entirely transparent
    }
}
