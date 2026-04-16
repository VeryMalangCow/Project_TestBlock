using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.AddressableAssets;
using UnityEditor.AddressableAssets.Settings;
using UnityEngine;

public class CharacterVisualsConverter : EditorWindow
{
    private const string BODIES_DIR = "Assets/Sprites/Bodies";
    private const string ARMORS_DIR = "Assets/Sprites/Armors";
    private const string GROUP_NAME = "CharacterVisuals";

    [MenuItem("Tools/Project/Converter/Character Visuals to Addressable")]
    public static void Convert()
    {
        var settings = AddressableAssetSettingsDefaultObject.Settings;
        if (settings == null)
        {
            Debug.LogError("[VisualsConverter] Addressable settings not found.");
            return;
        }

        var group = GetOrCreateGroup(settings, GROUP_NAME);
        int count = 0;

        // 1. Bodies 처리
        count += ProcessDirectory(settings, group, BODIES_DIR, "Body");

        // 2. Armors 처리
        count += ProcessDirectory(settings, group, ARMORS_DIR, "Armor");

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        Debug.Log($"[VisualsConverter] Successfully registered {count} character visual assets to '{GROUP_NAME}' group.");
    }

    private static int ProcessDirectory(AddressableAssetSettings settings, AddressableAssetGroup group, string rootDir, string prefix)
    {
        if (!Directory.Exists(rootDir)) return 0;

        int count = 0;
        string[] allFiles = Directory.GetFiles(rootDir, "*.png", SearchOption.AllDirectories);

        foreach (string filePath in allFiles)
        {
            string unityPath = filePath.Replace("\\", "/");
            string fileName = Path.GetFileNameWithoutExtension(unityPath);
            
            // 주소 생성 규칙: Body_Head_000 또는 Armor_Helmet_000
            // 파일명 자체가 Head_000 형식이므로 prefix만 붙여줌
            string address = $"{prefix}_{fileName}";

            string guid = AssetDatabase.AssetPathToGUID(unityPath);
            var entry = settings.CreateOrMoveEntry(guid, group);
            entry.address = address;
            
            count++;
        }

        return count;
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
