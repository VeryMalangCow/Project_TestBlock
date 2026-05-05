using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.AddressableAssets;
using UnityEditor.AddressableAssets.Settings;
using UnityEngine;
using UnityEngine.AddressableAssets;

public class ItemConverterFromCSVToSO : EditorWindow
{
    private struct WeaponStats
    {
        public int weaponID;
        public WeaponType weaponType;
        public int damage;
        public float knockback;
        public float speed;
        public float critChance;
        public float critDamage;
        public float reach;
        public int manaCost;
    }

    private const string CSV_PATH = "Assets/Datas/ItemDatabase.csv";
    private const string WEAPON_CSV_PATH = "Assets/Datas/WeaponDatabase.csv";
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

        EnsureFolders();

        // 1. 무기 데이터 로드 (TypeID 기반)
        Dictionary<int, WeaponStats> weaponStatsMap = LoadWeaponStats();

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

            itemData.id = id;
            itemData.typeID = typeID;
            itemData.itemName = name;
            itemData.description = description.Replace("\\n", "\n");
            itemData.maxStack = maxStack;
            
            if (typeStr.Equals("Sword", System.StringComparison.OrdinalIgnoreCase)) typeStr = "Weapon";
            if (System.Enum.TryParse(typeStr, out ItemType parsedType)) itemData.type = parsedType;

            // --- New Property Mapping Logic ---
            itemData.properties.Clear();

            switch (itemData.type)
            {
                case ItemType.Weapon:
                    if (weaponStatsMap.TryGetValue(itemData.typeID, out WeaponStats wStats))
                    {
                        WeaponProperty weaponProp = new WeaponProperty
                        {
                            weaponType = wStats.weaponType,
                            damage = wStats.damage,
                            speed = wStats.speed,
                            reach = wStats.reach
                        };
                        itemData.properties.Add(weaponProp);
                    }
                    break;

                case ItemType.Tool:
                    // 현재 CSV에 Tool 전용 데이터가 없으므로 기본값 할당
                    ToolProperty toolProp = new ToolProperty
                    {
                        minePower = 10,
                        mineSpeed = 0.2f
                    };
                    itemData.properties.Add(toolProp);
                    break;

                case ItemType.Block:
                    // 블록 설치 속성 추가
                    BlockProperty blockProp = new BlockProperty();
                    itemData.properties.Add(blockProp);
                    break;
            }

            string spritePath = $"{SPRITE_DIR}/Item_{id:D5}.png";
            ProcessAndRegisterSprite(spritePath, $"ItemIcon_{id:D5}", 64, iconGroup, settings, itemData, true);

            string heldSpritePath = $"{SPRITE_HELD_DIR}/Item_{id:D5}.png";
            itemData.hasHeldSprite = File.Exists(heldSpritePath);
            if (itemData.hasHeldSprite)
            {
                ProcessAndRegisterSprite(heldSpritePath, $"ItemHeld_{id:D5}", 64, iconGroup, settings, itemData, false);
            }

            EditorUtility.SetDirty(itemData);
            createdItems.Add(itemData);
        }

        UpdateDatabase(createdItems, dataGroup, settings);

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        Debug.Log($"[Converter] Successfully converted {createdItems.Count} items. (Weapon stats mapped by TypeID: {weaponStatsMap.Count})");
    }

    private static void EnsureFolders()
    {
        if (!AssetDatabase.IsValidFolder("Assets/Datas")) AssetDatabase.CreateFolder("Assets", "Datas");
        if (!AssetDatabase.IsValidFolder(SO_DIR)) AssetDatabase.CreateFolder("Assets/Datas", "Items");
        if (!AssetDatabase.IsValidFolder("Assets/Sprites")) AssetDatabase.CreateFolder("Assets", "Sprites");
        if (!AssetDatabase.IsValidFolder(SPRITE_HELD_DIR)) AssetDatabase.CreateFolder("Assets/Sprites", "Items_Held");
    }

    private static Dictionary<int, WeaponStats> LoadWeaponStats()
    {
        Dictionary<int, WeaponStats> map = new Dictionary<int, WeaponStats>();
        if (!File.Exists(WEAPON_CSV_PATH))
        {
            Debug.LogWarning($"[Converter] Weapon CSV not found at {WEAPON_CSV_PATH}. Skipping weapon stats.");
            return map;
        }

        try
        {
            // [Fix] FileShare.ReadWrite를 사용하여 다른 프로그램(엑셀 등)이 열고 있어도 읽기 시도
            using (var stream = new FileStream(WEAPON_CSV_PATH, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
            using (var reader = new StreamReader(stream))
            {
                string header = reader.ReadLine(); // Skip header
                while (!reader.EndOfStream)
                {
                    string line = reader.ReadLine()?.Trim();
                    if (string.IsNullOrEmpty(line)) continue;

                    string[] v = line.Split(',');
                    if (v.Length < 8) continue;

                    try
                    {
                        WeaponType wType = WeaponType.None;
                        System.Enum.TryParse(v[1].Trim(), true, out wType);

                        WeaponStats stats = new WeaponStats
                        {
                            weaponID = int.Parse(v[0].Trim()),
                            weaponType = wType,
                            damage = int.Parse(v[2].Trim()),
                            knockback = float.Parse(v[3].Trim()),
                            speed = float.Parse(v[4].Trim()),
                            critChance = float.Parse(v[5].Trim()),
                            critDamage = float.Parse(v[6].Trim()),
                            reach = float.Parse(v[7].Trim()),
                            manaCost = (v.Length >= 9) ? int.Parse(v[8].Trim()) : 0
                        };

                        map[stats.weaponID] = stats;
                    }
                    catch (System.Exception e)
                    {
                        Debug.LogError($"[Converter] Error parsing weapon CSV line: {e.Message}");
                    }
                }
            }
        }
        catch (IOException e)
        {
            Debug.LogError($"[Converter] Failed to read weapon CSV due to sharing violation: {e.Message}. Please close the file in Excel.");
        }
        
        return map;
    }

    private static void ProcessAndRegisterSprite(string path, string address, int maxSize, AddressableAssetGroup group, AddressableAssetSettings settings, ItemData itemData, bool isIcon)
    {
        if (!File.Exists(path)) return;

        TextureImporter importer = AssetImporter.GetAtPath(path) as TextureImporter;
        if (importer != null)
        {
            bool needsReimport = false;
            if (importer.maxTextureSize != maxSize || importer.filterMode != FilterMode.Point || 
                !importer.isReadable || importer.mipmapEnabled)
            {
                importer.textureType = TextureImporterType.Sprite;
                importer.maxTextureSize = maxSize;
                importer.filterMode = FilterMode.Point;
                importer.isReadable = true;
                importer.mipmapEnabled = false;
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
