using System;
using UnityEngine;

public enum ItemType
{
    None,
    Block,
    Wall,
    Weapon,
    Tool,
    Armor,
    Accessory,
    Consumable,
    Material
}

[Serializable]
public class ItemData
{
    public int id;
    public string itemName;
    public string description;
    public ItemType type;
    public int maxStack;
    public string iconPath;
    public Sprite icon;
    public int value;

    // Optional: 추가 속성들 (공격력, 방어력 등)
    public int damage;
    public int defense;
    public float useTime;
}
