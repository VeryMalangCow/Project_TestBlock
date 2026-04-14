using UnityEngine;
using UnityEditor;
using UnityEditor.U2D.Sprites;
using System.Collections.Generic;
using System.IO;

public class SpriteProcessor : Editor
{
    struct Texture2DElement
    {
        public int maxSize;
        public bool isReadable;
        public Vector2Int cellSize;
        public Vector2 pivot;

        public Texture2DElement(int _maxSize, bool _isReadable, Vector2Int _cellSize, Vector2 _pivot)
        {
            maxSize = _maxSize;
            isReadable = _isReadable;
            cellSize = _cellSize;
            pivot = _pivot;
        }
    }

    [MenuItem("Tools/Project/Sprite Processor/Tile")]
    public static void ProcessSpritesForTile()
        => ProcessTileTexture("Tiles", "Tile", new Texture2DElement(256, true, new Vector2Int(16, 16), new Vector2(8, 8)));

    [MenuItem("Tools/Project/Sprite Processor/Platform")]
    public static void ProcessSpritesForPlatform()
        => ProcessTileTexture("Platforms", "Platform", new Texture2DElement(128, false, new Vector2Int(16, 16), new Vector2(8, 8)));

    [MenuItem("Tools/Project/Sprite Processor/Torch")]
    public static void ProcessSpritesForTouch()
        => ProcessTileTexture("Torches", "Torch", new Texture2DElement(64, false, new Vector2Int(16, 16), new Vector2(8, 8)));

    [MenuItem("Tools/Project/Sprite Processor/Tree")]
    public static void ProcessSpritesForTree()
        => ProcessTileTexture("Trees", "Tree", new Texture2DElement(256, false, new Vector2Int(57, 150), new Vector2(28.5f, 9)));

    [MenuItem("Tools/Project/Sprite Processor/Body")]
    public static void ProcessSpritesForArmor()
    {
        ProcessTileTexture("Bodies", "Body", new Texture2DElement(256, false, new Vector2Int(45, 80), new Vector2(24.5f, 40)));
    }

    private static void ProcessTileTexture(string fileName, string debugName, Texture2DElement texture2DElement)
    {
        string[] guids = AssetDatabase.FindAssets("t:Texture2D", new[] { "Assets/Resources/Sprites/" + fileName });
        int count = 0;

        foreach (string guid in guids)
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            ProcessTileTexture(path, texture2DElement);
            count++;
        }

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        Debug.Log(GetNotice(debugName, count));
    }


    private static void ProcessTileTexture(string path, Texture2DElement texture2DElement)
    {
        TextureImporter importer = AssetImporter.GetAtPath(path) as TextureImporter;
        if (importer == null) return;

        // 1. Initial Setup to make it readable
        importer.textureType = TextureImporterType.Sprite;
        importer.spriteImportMode = SpriteImportMode.Multiple;
        importer.isReadable = texture2DElement.isReadable;
        importer.filterMode = FilterMode.Point;
        importer.textureCompression = TextureImporterCompression.Uncompressed;
        importer.maxTextureSize = texture2DElement.maxSize;
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

        int cellWidth = texture2DElement.cellSize.x;
        int cellHeight = texture2DElement.cellSize.y;
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
                    rect.alignment = SpriteAlignment.Custom;
                    rect.name = $"{fileName}_{index:D3}"; // Maintain index based on grid position
                    rect.pivot = new Vector2(texture2DElement.pivot.x / cellWidth, texture2DElement.pivot.y / cellHeight);
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

    private static string GetNotice(string type, int count)
    {
        return $"<color=red>[Sprite Processor]</color> <color=orange><{type}></color> Successfully processed <color=orange>\"{count}\"</color> textures.";
    }
}
