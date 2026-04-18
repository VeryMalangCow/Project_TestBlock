using UnityEngine;
using UnityEngine.AddressableAssets;

public enum ItemType
{
    None,
    Block,
    Helmet,
    Chestplate,
    Leggings,
    Boots,
    Jetbag,
    Sword,
    Tool,
    Consumable
}

[CreateAssetMenu(fileName = "NewItemData", menuName = "Project_BlockTest/ItemData")]
public class ItemData : ScriptableObject
{
    [Header("### Basic Info")]
    public int id;
    public string itemName;
    [TextArea(3, 10)]
    public string description;

    [Header("### Gameplay")]
    public int maxStack = 999;
    public int typeID = -1; // [Added] For visual resource mapping
    public ItemType type;
    public float useTime;

    [Header("### Visuals (Addressables)")]
    public AssetReferenceSprite iconReference;
    public AssetReference worldPrefabReference;
}
