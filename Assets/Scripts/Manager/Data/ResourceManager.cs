using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AddressableAssets;

#region Tile Sprite

[Serializable]
public class TileSpriteSet
{
    private static int[] maskToRuleID = new int[256];
    private static bool isMappingInitialized = false;

    public static int[] GetRawMappingArray()
    {
        if (!isMappingInitialized) InitializeMapping();
        return maskToRuleID;
    }

    public static void InitializeMapping()
    {
        if (isMappingInitialized) return;

        for (int i = 0; i < 256; i++) maskToRuleID[i] = 0;

        // [Addressable] 오토타일링 규칙 데이터 로드
        var handle = Addressables.LoadAssetAsync<TextAsset>("Rule_TileIndex");
        TextAsset csvData = handle.WaitForCompletion();

        if (csvData == null)
        {
            Debug.LogError("[ResourceManager] Rule_TileIndex not found via Addressables (Address: Rule_TileIndex)");
            return;
        }

        string[] lines = csvData.text.Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.RemoveEmptyEntries);
        for (int i = 1; i < lines.Length; i++)
        {
            string line = lines[i].Trim();
            if (string.IsNullOrWhiteSpace(line)) continue;
            string[] parts = line.Split(',');
            if (parts.Length < 1 || !int.TryParse(parts[0], out int ruleId)) continue;

            int orthoMask = 0;
            int diagMissingMask = 0;

            if (parts.Length > 1 && !string.IsNullOrWhiteSpace(parts[1]) && parts[1] != "0")
            {
                foreach (var o in parts[1].Trim().Split(' '))
                {
                    if (o == "2") orthoMask |= (1 << 0);
                    if (o == "4") orthoMask |= (1 << 1);
                    if (o == "6") orthoMask |= (1 << 2);
                    if (o == "8") orthoMask |= (1 << 3);
                }
            }

            if (parts.Length > 2 && !string.IsNullOrWhiteSpace(parts[2]))
            {
                foreach (var d in parts[2].Trim().Split(' '))
                {
                    if (d == "1") diagMissingMask |= (1 << 4);
                    if (d == "3") diagMissingMask |= (1 << 5);
                    if (d == "7") diagMissingMask |= (1 << 6);
                    if (d == "9") diagMissingMask |= (1 << 7);
                }
            }

            maskToRuleID[orthoMask | diagMissingMask] = ruleId;
        }
        isMappingInitialized = true;
    }

    public static int GetRuleID(int bitmask)
    {
        if (!isMappingInitialized) InitializeMapping();
        return maskToRuleID[bitmask & 0xFF];
    }
}

#endregion

public class ResourceManager : PermanentSingleton<ResourceManager>
{
    #region Variable

    // [Cache] 캐릭터 파츠 캐시 (주소 -> 12개 스프라이트 배열)
    private Dictionary<string, Sprite[]> characterVisualCache = new Dictionary<string, Sprite[]>();

    #endregion

    #region MonoBehaviour

    protected override void Awake()
    {
        base.Awake();
        Init();
    }

    private void OnDestroy()
    {
        // 시스템 종료 시 캐릭터 비주얼 캐시 정리 (참조 해제는 Addressables 시스템이 관리)
        characterVisualCache.Clear();
    }

    #endregion

    #region Init

    public void Init()
    {
        TileSpriteSet.InitializeMapping();
    }

    #endregion
    
    #region Tile Access

    public int GetTileKindCount(int tileId)
    {
        return 3; // 현재 규칙상 3개 고정
    }

    #endregion

    #region Character Visual Access (Addressable & Cache)

    /// <summary>
    /// 캐릭터 몸체 파츠(Head, Arm 등)를 즉시 로드하거나 캐시에서 가져옵니다.
    /// </summary>
    public Sprite[] GetBodyPartSprites(string partName, int id)
    {
        string cleanName = partName.Contains('/') ? partName.Substring(partName.LastIndexOf('/') + 1) : partName;
        // [Simplified] Body_Head_000
        string address = $"Body_{cleanName}_{id:D3}";
        return GetOrLoadCharacterSprites(address);
    }

    /// <summary>
    /// 갑옷 파츠를 즉시 로드하거나 캐시에서 가져옵니다. (ID 기반)
    /// </summary>
    public Sprite[] GetArmorSprites(string category, int id)
    {
        string cleanName = category.Contains('/') ? category.Substring(category.LastIndexOf('/') + 1) : category;
        // [Simplified] Armor_Chestplate_000
        string address = $"Armor_{cleanName}_{id:D3}";
        return GetOrLoadCharacterSprites(address);
    }

    /// <summary>
    /// 갑옷 파츠를 즉시 로드하거나 캐시에서 가져옵니다. (Base 등 문자열 기반)
    /// </summary>
    public Sprite[] GetArmorSprites(string category, string idOrBase)
    {
        string cleanName = category.Contains('/') ? category.Substring(category.LastIndexOf('/') + 1) : category;
        // [Simplified] Armor_Chestplate_Base
        string address = $"Armor_{cleanName}_{idOrBase}";
        return GetOrLoadCharacterSprites(address);
    }

    /// <summary>
    /// 실제 어드레서블 로딩 및 스프라이트 정렬 로직 (중앙 집중)
    /// </summary>
    private Sprite[] GetOrLoadCharacterSprites(string address)
    {
        // 1. 캐시 확인
        if (characterVisualCache.TryGetValue(address, out Sprite[] cached))
        {
            return cached;
        }

        // 2. 동기식 어드레서블 로드 (슬라이스된 스프라이트 전체)
        try
        {
            // [Fix] 키가 존재하지 않을 경우를 대비하여 핸들 상태를 먼저 확인하는 것은 어려우므로 Try-Catch로 보호
            var handle = Addressables.LoadAssetAsync<IList<Sprite>>(address);
            IList<Sprite> spriteList = handle.WaitForCompletion();

            if (spriteList == null || spriteList.Count == 0)
            {
                return null;
            }

            // 3. 이름 규칙에 따라 12개 배열로 정렬 (예: ..._0, ..._1)
            Sprite[] sorted = new Sprite[12];
            foreach (var s in spriteList)
            {
                string[] parts = s.name.Split('_');
                if (parts.Length >= 2 && int.TryParse(parts[parts.Length - 1], out int idx))
                {
                    if (idx >= 0 && idx < 12) sorted[idx] = s;
                }
            }

            characterVisualCache[address] = sorted;
            return sorted;
        }
        catch (UnityEngine.AddressableAssets.InvalidKeyException)
        {
            // [Debug] 찾지 못한 주소를 명확히 출력
            Debug.LogWarning($"[ResourceManager] InvalidKeyException: Key '{address}' not found in Addressables.");
            return null;
        }
        catch (Exception e)
        {
            Debug.LogError($"[ResourceManager] Failed to load character visual '{address}': {e.Message}");
            return null;
        }
    }

    #endregion
}
