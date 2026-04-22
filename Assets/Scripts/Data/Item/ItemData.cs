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

    [Header("### Held Visual Settings")]
    public bool hasHeldSprite;
    public Vector2 heldPivot;  // Pixel-based pivot (0,0 to 64,64)
    public float heldRotation; // Default rotation when held

    [Header("### Visuals (Addressables)")]
    public AssetReferenceSprite iconReference;
    public AssetReference worldPrefabReference;
}
