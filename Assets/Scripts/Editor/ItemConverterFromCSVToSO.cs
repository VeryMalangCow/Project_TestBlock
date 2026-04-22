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
    private const string ADDRESSABLE_GROUP_NAME = "ItemIcons";
    private const string DATA_GROUP_NAME = "GlobalDatas";
    private const string DATABASE_ADDRESS = "ItemDatabase";

    [MenuItem("Tools/Project/Converter/Item CSV to SO")]
    public static void Convert()
    {
        if (!File.Exists(CSV_PATH))
        {
            Debug.LogError($"[Converter] CSV not found at {CSV_PATH}. Please ensure you moved the file to {CSV_PATH}");
            return;
        }

        // 0. Addressable Settings 확인
        var settings = AddressableAssetSettingsDefaultObject.Settings;
        if (settings == null)
        {
            Debug.LogError("[Converter] Addressable settings not found.");
            return;
        }
        var iconGroup = GetOrCreateGroup(settings, ADDRESSABLE_GROUP_NAME);
        var dataGroup = GetOrCreateGroup(settings, DATA_GROUP_NAME);

        // 1. 폴더 확인 및 생성
        if (!AssetDatabase.IsValidFolder("Assets/Datas"))
        {
            AssetDatabase.CreateFolder("Assets", "Datas");
        }
        if (!AssetDatabase.IsValidFolder(SO_DIR))
        {
            AssetDatabase.CreateFolder("Assets/Datas", "Items");
        }

        // 2. CSV 데이터 읽기
        string[] lines = File.ReadAllLines(CSV_PATH);
        if (lines.Length <= 1) return; 

        List<ItemData> createdItems = new List<ItemData>();

        for (int i = 1; i < lines.Length; i++)
        {
            string[] values = lines[i].Split(',');
            if (values.Length < 6) continue; // Updated to 6 columns (ID, TypeID, Name, Description, MaxStack, Type)

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

            itemData.id = id;
            itemData.typeID = typeID; 
            itemData.itemName = name;
            itemData.description = description.Replace("\\n", "\n");
            itemData.maxStack = maxStack;
            
            if (System.Enum.TryParse(typeStr, out ItemType parsedType))
                itemData.type = parsedType;

            // 4. 아이콘 자동 매칭 및 Addressable 등록 (ItemIcons 그룹)
            string spritePath = $"{SPRITE_DIR}/Item_{id:D5}.png";
            Texture2D iconTex = AssetDatabase.LoadAssetAtPath<Texture2D>(spritePath);
            
            if (iconTex != null)
            {
                // [Import Settings Enforce] 48x48, Point Filter, Readable
                TextureImporter importer = AssetImporter.GetAtPath(spritePath) as TextureImporter;
                if (importer != null)
                {
                    bool needsReimport = false;
                    if (importer.maxTextureSize != 48 || importer.filterMode != FilterMode.Point || !importer.isReadable)
                    {
                        importer.textureType = TextureImporterType.Default; // Use Default for Texture2DArray copy
                        importer.maxTextureSize = 48;
                        importer.filterMode = FilterMode.Point;
                        importer.isReadable = true;
                        importer.textureCompression = TextureImporterCompression.Uncompressed;
                        
                        // Platform specific to ensure RGBA32
                        var settings_standalone = importer.GetDefaultPlatformTextureSettings();
                        settings_standalone.format = TextureImporterFormat.RGBA32;
                        importer.SetPlatformTextureSettings(settings_standalone);
                        
                        needsReimport = true;
                    }

                    if (needsReimport)
                    {
                        EditorUtility.SetDirty(importer);
                        importer.SaveAndReimport();
                        // Re-load after import settings change
                        iconTex = AssetDatabase.LoadAssetAtPath<Texture2D>(spritePath);
                    }
                }

                string guid = AssetDatabase.AssetPathToGUID(spritePath);
                var entry = settings.CreateOrMoveEntry(guid, iconGroup);
                entry.address = $"ItemIcon_{id:D5}";
                
                // SO에는 여전히 Sprite 참조를 남길 수도 있지만, 
                // 이제 시스템은 이 Texture2D 주소를 통해 캐시를 채웁니다.
                Sprite iconSprite = AssetDatabase.LoadAssetAtPath<Sprite>(spritePath);
                if (iconSprite != null)
                    itemData.iconReference = new AssetReferenceSprite(guid);
            }

            EditorUtility.SetDirty(itemData);
            createdItems.Add(itemData);
        }

        // 5. 통합 데이터베이스 업데이트 및 Addressable 등록 (GlobalDatas 그룹)
        ItemDatabase database = AssetDatabase.LoadAssetAtPath<ItemDatabase>(DATABASE_PATH);
        if (database == null)
        {
            database = ScriptableObject.CreateInstance<ItemDatabase>();
            AssetDatabase.CreateAsset(database, DATABASE_PATH);
        }

        database.items = createdItems;
        database.RefreshList();
        EditorUtility.SetDirty(database);

        // [Addressable] 통합 데이터베이스를 GlobalDatas 그룹에 등록
        string dbGuid = AssetDatabase.AssetPathToGUID(DATABASE_PATH);
        var dbEntry = settings.CreateOrMoveEntry(dbGuid, dataGroup); 
        dbEntry.address = DATABASE_ADDRESS;

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        Debug.Log($"[Converter] Successfully converted {createdItems.Count} items. Database registered in '{DATA_GROUP_NAME}' as '{DATABASE_ADDRESS}'.");
    }


    private static AddressableAssetGroup GetOrCreateGroup(AddressableAssetSettings settings, string groupName)
    {
        var group = settings.FindGroup(groupName);
        if (group == null)
        {
            group = settings.CreateGroup(groupName, false, false, true, null);
        }
        return group;
    }
}
