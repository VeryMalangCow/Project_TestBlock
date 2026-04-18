using UnityEngine;
using UnityEngine.AddressableAssets;

public enum ItemType
{
    None,
    Block,
    Sword,
    Helmet,
    Chestplate,
    Leggings,
    Boots,
    Jetbag,
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
    public ItemType type;
    public float useTime;

    [Header("### Visuals (Addressables)")]
    public AssetReferenceSprite iconReference;
    public AssetReference worldPrefabReference;
}
