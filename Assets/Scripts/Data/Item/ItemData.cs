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
    Weapon,
    Tool,
    Consumable
}

public enum WeaponType
{
    None,
    Sword,
    Spear,
    Bow,
    Arrow,
    Staff
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
    public int typeID = -1; 
    public ItemType type;

    [Header("### Held Visual Settings")]
    public bool hasHeldSprite;
    public Vector2 heldPivot;  
    public float heldRotation; 

    [Header("### Visuals (Addressables)")]
    public AssetReferenceSprite iconReference;
    public AssetReference worldPrefabReference;

    [Header("### Weapon/Tool Stats")]
    public WeaponStats weaponStats;
}

[System.Serializable]
public class WeaponStats
{
    public int weaponID; // Matches ItemData.typeID
    public WeaponType weaponType; 
    public int damage;
    public float knockback;
    public float speed;    // Attacks per second
    public float critChance; 
    public float critDamage; 
    public float reach;
    public int manaCost;

    public float UseTime => speed > 0 ? 1f / speed : 0.2f;
}
