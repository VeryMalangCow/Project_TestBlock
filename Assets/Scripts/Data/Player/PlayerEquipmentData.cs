using System;

/// <summary>
/// 플레이어의 장착 아이템 데이터를 관리하는 클래스입니다.
/// </summary>
[Serializable]
public class PlayerEquipmentData
{
    public int helmetIndex = -1;
    public int chestplateIndex = -1;
    public int leggingsIndex = -1;
    public int bootsIndex = -1;
    public int jetbagIndex = -1;

    public PlayerEquipmentData()
    {
        helmetIndex = -1;
        chestplateIndex = -1;
        leggingsIndex = -1;
        bootsIndex = -1;
        jetbagIndex = -1;
    }

    public void SetEquipment(ItemType type, int typeID)
    {
        switch (type)
        {
            case ItemType.Helmet: helmetIndex = typeID; break;
            case ItemType.Chestplate: chestplateIndex = typeID; break;
            case ItemType.Leggings: leggingsIndex = typeID; break;
            case ItemType.Boots: bootsIndex = typeID; break;
            case ItemType.Jetbag: jetbagIndex = typeID; break;
        }
    }

    public int GetEquipment(ItemType type)
    {
        return type switch
        {
            ItemType.Helmet => helmetIndex,
            ItemType.Chestplate => chestplateIndex,
            ItemType.Leggings => leggingsIndex,
            ItemType.Boots => bootsIndex,
            ItemType.Jetbag => jetbagIndex,
            _ => -1
        };
    }
}
