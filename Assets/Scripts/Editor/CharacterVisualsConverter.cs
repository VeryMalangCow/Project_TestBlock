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

    [MenuItem("Tools/Project/Converter/Character Visuals to Addressable")]
    public static void Convert()
    {
        var settings = AddressableAssetSettingsDefaultObject.Settings;
        if (settings == null)
        {
            Debug.LogError("[VisualsConverter] Addressable settings not found.");
            return;
        }

        int count = 0;

        // 1. Bodies -> CharacterBody 그룹
        count += ProcessDirectory(settings, "CharacterBody", BODIES_DIR, "Body");

        // 2. Armors -> 부위별 개별 그룹
        count += ProcessArmors(settings, ARMORS_DIR);

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        Debug.Log($"[VisualsConverter] Successfully registered {count} assets into specialized groups.");
    }

    private static int ProcessArmors(AddressableAssetSettings settings, string rootDir)
    {
        if (!Directory.Exists(rootDir)) return 0;

        int totalCount = 0;
        string[] subDirs = Directory.GetDirectories(rootDir);

        foreach (string dir in subDirs)
        {
            string folderName = Path.GetFileName(dir);
            string groupName = "";

            // 폴더명에 따른 그룹 매핑
            if (folderName.Contains("Chestplate")) groupName = "CharacterChest";
            else if (folderName.Equals("Helmet")) groupName = "CharacterHelmet";
            else if (folderName.Equals("Leggings")) groupName = "CharacterLeggings";
            else if (folderName.Equals("Boots")) groupName = "CharacterBoots";
            else if (folderName.Equals("Jetbag")) groupName = "CharacterJetbag";
            else groupName = "CharacterOther"; // 예외 케이스

            totalCount += ProcessDirectory(settings, groupName, dir, "Armor");
        }

        return totalCount;
    }

    private static int ProcessDirectory(AddressableAssetSettings settings, string groupName, string dirPath, string prefix)
    {
        var group = GetOrCreateGroup(settings, groupName);
        int count = 0;
        string[] allFiles = Directory.GetFiles(dirPath, "*.png", SearchOption.AllDirectories);

        foreach (string filePath in allFiles)
        {
            string unityPath = filePath.Replace("\\", "/");
            string fileName = Path.GetFileNameWithoutExtension(unityPath);
            
            // 주소 형식: Body_Head_000 또는 Armor_Chestplate_000
            // 그룹이 달라도 주소는 유니크해야 하므로 prefix와 파일명을 조합합니다.
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
            // 실제 유저가 원하는대로 세분화된 그룹 생성
            group = settings.CreateGroup(groupName, false, false, true, null);
        }
        return group;
    }
}
