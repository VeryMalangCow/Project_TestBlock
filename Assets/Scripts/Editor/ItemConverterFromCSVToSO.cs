using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.AddressableAssets;
using UnityEditor.AddressableAssets.Settings;
using UnityEngine;
using UnityEngine.AddressableAssets;

public class ItemConverterFromCSVToSO : EditorWindow
{
    private const string CSV_PATH = "Assets/Datas/ItemDatabase.csv";
    private const string SO_DIR = "Assets/Datas/Items";
    private const string DATABASE_PATH = "Assets/Datas/ItemDatabase.asset";
    private const string SPRITE_DIR = "Assets/Sprites/Items";
    private const string SPRITE_HELD_DIR = "Assets/Sprites/Items_Held";
    private const string ADDRESSABLE_GROUP_NAME = "ItemIcons";
    private const string DATA_GROUP_NAME = "GlobalDatas";
    private const string DATABASE_ADDRESS = "ItemDatabase";

    [MenuItem("Tools/Project/Converter/Item CSV to SO")]
    public static void Convert()
    {
        if (!File.Exists(CSV_PATH))
        {
            Debug.LogError($"[Converter] CSV not found at {CSV_PATH}.");
            return;
        }

        var settings = AddressableAssetSettingsDefaultObject.Settings;
        if (settings == null)
        {
            Debug.LogError("[Converter] Addressable settings not found.");
            return;
        }
        var iconGroup = GetOrCreateGroup(settings, ADDRESSABLE_GROUP_NAME);
        var dataGroup = GetOrCreateGroup(settings, DATA_GROUP_NAME);

        // 폴더 자동 생성
        EnsureFolders();

        string[] lines = File.ReadAllLines(CSV_PATH);
        if (lines.Length <= 1) return;

        List<ItemData> createdItems = new List<ItemData>();

        for (int i = 1; i < lines.Length; i++)
        {
            string[] values = lines[i].Split(',');
            if (values.Length < 6) continue;

            int id = int.Parse(values[0]);
            int typeID = int.Parse(values[1]);
            string name = values[2];
            string description = values[3];
            int maxStack = int.Parse(values[4]);
            string typeStr = values[5];

            string assetPath = $"{SO_DIR}/Item_{id:D5}.asset";
            ItemData itemData = AssetDatabase.LoadAssetAtPath<ItemData>(assetPath);

            if (itemData == null)
            {
                itemData = ScriptableObject.CreateInstance<ItemData>();
                AssetDatabase.CreateAsset(itemData, assetPath);
            }

            // 기본 데이터 할당
            itemData.id = id;
            itemData.typeID = typeID;
            itemData.itemName = name;
            itemData.description = description.Replace("\\n", "\n");
            itemData.maxStack = maxStack;
            if (System.Enum.TryParse(typeStr, out ItemType parsedType)) itemData.type = parsedType;

            // 1. 일반 아이콘 처리 (48x48)
            string spritePath = $"{SPRITE_DIR}/Item_{id:D5}.png";
            ProcessAndRegisterSprite(spritePath, $"ItemIcon_{id:D5}", 48, iconGroup, settings, itemData, true);

            // 2. 들고 있는 전용 이미지 처리 (64x64)
            string heldSpritePath = $"{SPRITE_HELD_DIR}/Item_{id:D5}.png";
            itemData.hasHeldSprite = File.Exists(heldSpritePath);
            if (itemData.hasHeldSprite)
            {
                ProcessAndRegisterSprite(heldSpritePath, $"ItemHeld_{id:D5}", 64, iconGroup, settings, itemData, false);
            }

            EditorUtility.SetDirty(itemData);
            createdItems.Add(itemData);
        }

        // 통합 데이터베이스 갱신
        UpdateDatabase(createdItems, dataGroup, settings);

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        Debug.Log($"[Converter] Successfully converted {createdItems.Count} items.");
    }

    private static void EnsureFolders()
    {
        if (!AssetDatabase.IsValidFolder("Assets/Datas")) AssetDatabase.CreateFolder("Assets", "Datas");
        if (!AssetDatabase.IsValidFolder(SO_DIR)) AssetDatabase.CreateFolder("Assets/Datas", "Items");
        if (!AssetDatabase.IsValidFolder("Assets/Sprites")) AssetDatabase.CreateFolder("Assets", "Sprites");
        if (!AssetDatabase.IsValidFolder(SPRITE_HELD_DIR)) AssetDatabase.CreateFolder("Assets/Sprites", "Items_Held");
    }

    private static void ProcessAndRegisterSprite(string path, string address, int maxSize, AddressableAssetGroup group, AddressableAssetSettings settings, ItemData itemData, bool isIcon)
    {
        if (!File.Exists(path)) return;

        TextureImporter importer = AssetImporter.GetAtPath(path) as TextureImporter;
        if (importer != null)
        {
            bool needsReimport = false;
            if (importer.maxTextureSize != maxSize || importer.filterMode != FilterMode.Point || !importer.isReadable)
            {
                importer.textureType = TextureImporterType.Default;
                importer.maxTextureSize = maxSize;
                importer.filterMode = FilterMode.Point;
                importer.isReadable = true;
                importer.textureCompression = TextureImporterCompression.Uncompressed;
                
                var platform = importer.GetDefaultPlatformTextureSettings();
                platform.format = TextureImporterFormat.RGBA32;
                platform.overridden = true;
                importer.SetPlatformTextureSettings(platform);
                
                needsReimport = true;
            }

            if (needsReimport)
            {
                EditorUtility.SetDirty(importer);
                importer.SaveAndReimport();
            }
        }

        string guid = AssetDatabase.AssetPathToGUID(path);
        var entry = settings.CreateOrMoveEntry(guid, group);
        entry.address = address;

        if (isIcon)
        {
            Sprite s = AssetDatabase.LoadAssetAtPath<Sprite>(path);
            if (s != null) itemData.iconReference = new AssetReferenceSprite(guid);
        }
    }

    private static void UpdateDatabase(List<ItemData> items, AddressableAssetGroup group, AddressableAssetSettings settings)
    {
        ItemDatabase database = AssetDatabase.LoadAssetAtPath<ItemDatabase>(DATABASE_PATH);
        if (database == null)
        {
            database = ScriptableObject.CreateInstance<ItemDatabase>();
            AssetDatabase.CreateAsset(database, DATABASE_PATH);
        }
        database.items = items;
        database.RefreshList();
        EditorUtility.SetDirty(database);

        string dbGuid = AssetDatabase.AssetPathToGUID(DATABASE_PATH);
        var dbEntry = settings.CreateOrMoveEntry(dbGuid, group); 
        dbEntry.address = DATABASE_ADDRESS;
    }

    private static AddressableAssetGroup GetOrCreateGroup(AddressableAssetSettings settings, string groupName)
    {
        var group = settings.FindGroup(groupName);
        if (group == null) group = settings.CreateGroup(groupName, false, false, true, null);
        return group;
    }
}
