using System.Collections.Generic;
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

    [Header("### Properties (Polymorphic)")]
    [SerializeReference]
    public List<IItemProperty> properties = new List<IItemProperty>();

    // --- Runtime Caching ---
    private IUsable _leftUsable;
    private IUsable _rightUsable;
    private bool _isCached = false;

    public IUsable LeftAction => GetUsable(0);
    public IUsable RightAction => GetUsable(1);

    private IUsable GetUsable(int buttonIndex)
    {
        if (!_isCached) CacheUsables();
        return buttonIndex == 0 ? _leftUsable : _rightUsable;
    }

    private void CacheUsables()
    {
        _leftUsable = new NullUsable();
        _rightUsable = new NullUsable();

        if (properties != null)
        {
            foreach (var prop in properties)
            {
                if (prop is IUsable usable)
                {
                    if (usable.TargetButton == 0) _leftUsable = usable;
                    else if (usable.TargetButton == 1) _rightUsable = usable;
                    else if (usable.TargetButton == 2) { _leftUsable = usable; _rightUsable = usable; }
                }
            }
        }
        _isCached = true;
    }

    public void OnValidate() { _isCached = false; } // 인스펙터 수정 시 캐시 초기화
}
