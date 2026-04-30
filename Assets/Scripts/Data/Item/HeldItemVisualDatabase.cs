using UnityEngine;
using System.Collections.Generic;

[CreateAssetMenu(fileName = "HeldItemVisualDatabase", menuName = "Project/Database/HeldItemVisual")]
public class HeldItemVisualDatabase : ScriptableObject
{
    [System.Serializable]
    public class HeldSettings
    {
        public ItemType itemType;
        public WeaponType weaponType; // itemType이 Weapon일 때만 의미가 있음

        [Header("Idle State")]
        public Vector2 pivot = new Vector2(32, 32);
        public float rotation = 0f;

        [Header("Use State")]
        public Vector2 usePivot = new Vector2(32, 32);
        public float useRotation = 0f;
    }

    public List<HeldSettings> settingsList = new List<HeldSettings>();

    public HeldSettings GetSettings(ItemType itemType, WeaponType weaponType)
    {
        // 1. 무기(Weapon)인 경우 무기 타입까지 정밀 검색
        if (itemType == ItemType.Weapon)
        {
            var found = settingsList.Find(s => s.itemType == ItemType.Weapon && s.weaponType == weaponType);
            if (found != null) return found;
        }

        // 2. 무기가 아니거나 해당 무기 타입을 못 찾았다면 아이템 타입으로 검색
        var typeFound = settingsList.Find(s => s.itemType == itemType);
        if (typeFound != null) return typeFound;

        // 3. 기본값 반환
        return new HeldSettings { itemType = itemType, weaponType = weaponType };
    }
}
