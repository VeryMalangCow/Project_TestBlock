using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.AddressableAssets;
using UnityEditor.AddressableAssets.Settings;
using UnityEngine;
using UnityEngine.AddressableAssets;

public class ItemConverterFromCSVToSO : EditorWindow
{
    private const string CSV_PATH = "Assets/Resources/Data/ItemDatabase.csv";
    private const string SO_DIR = "Assets/Resources/Data/Items";
    private const string DATABASE_PATH = "Assets/Resources/Data/ItemDatabase.asset";
    private const string SPRITE_DIR = "Assets/Resources/Sprites/Items";
    private const string ADDRESSABLE_GROUP_NAME = "ItemIcons";

    [MenuItem("Tools/Project/Converter/Item CSV to SO")]
    public static void Convert()
    {
        if (!File.Exists(CSV_PATH))
        {
            Debug.LogError($"[Converter] CSV not found at {CSV_PATH}");
            return;
        }

        // 0. Addressable Settings 확인
        var settings = AddressableAssetSettingsDefaultObject.Settings;
        if (settings == null)
        {
            Debug.LogError("[Converter] Addressable settings not found. Please create settings first.");
            return;
        }
        var group = GetOrCreateGroup(settings, ADDRESSABLE_GROUP_NAME);

        // 1. 폴더 확인 및 생성
        if (!AssetDatabase.IsValidFolder(SO_DIR))
        {
            string parent = Path.GetDirectoryName(SO_DIR).Replace("\\", "/");
            string folder = Path.GetFileName(SO_DIR);
            AssetDatabase.CreateFolder(parent, folder);
        }

        // 2. CSV 데이터 읽기
        string[] lines = File.ReadAllLines(CSV_PATH);
        if (lines.Length <= 1) return; 

        List<ItemData> createdItems = new List<ItemData>();

        for (int i = 1; i < lines.Length; i++)
        {
            string[] values = lines[i].Split(',');
            if (values.Length < 6) continue;

            int id = int.Parse(values[0]);
            string name = values[1];
            string description = values[2];
            int maxStack = int.Parse(values[3]);
            string typeStr = values[4];
            float useTime = float.Parse(values[5]);

            string assetPath = $"{SO_DIR}/Item_{id:D5}.asset";
            ItemData itemData = AssetDatabase.LoadAssetAtPath<ItemData>(assetPath);

            if (itemData == null)
            {
                itemData = ScriptableObject.CreateInstance<ItemData>();
                AssetDatabase.CreateAsset(itemData, assetPath);
            }

            itemData.id = id;
            itemData.itemName = name;
            itemData.description = description.Replace("\\n", "\n");
            itemData.maxStack = maxStack;
            
            if (System.Enum.TryParse(typeStr, out ItemType parsedType))
                itemData.type = parsedType;
            
            itemData.useTime = useTime;

            // 4. 아이콘 자동 매칭 및 Addressable 등록
            string spritePath = $"{SPRITE_DIR}/Item_{id:D5}.png";
            Sprite icon = AssetDatabase.LoadAssetAtPath<Sprite>(spritePath);
            if (icon != null)
            {
                // [Addressable 핵심] 에셋을 어드레서블로 등록하고 주소 부여
                string guid = AssetDatabase.AssetPathToGUID(spritePath);
                var entry = settings.CreateOrMoveEntry(guid, group);
                entry.address = $"ItemIcon_{id:D5}"; // 명확한 주소 부여
                
                // SO에 Reference 저장
                itemData.iconReference = new AssetReferenceSprite(guid);
            }

            EditorUtility.SetDirty(itemData);
            createdItems.Add(itemData);
        }

        // 5. 통합 데이터베이스 업데이트
        ItemDatabase database = AssetDatabase.LoadAssetAtPath<ItemDatabase>(DATABASE_PATH);
        if (database == null)
        {
            database = ScriptableObject.CreateInstance<ItemDatabase>();
            AssetDatabase.CreateAsset(database, DATABASE_PATH);
        }

        database.items = createdItems;
        database.RefreshList();
        EditorUtility.SetDirty(database);

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        Debug.Log($"[Converter] Successfully converted {createdItems.Count} items and registered to Addressables.");
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
