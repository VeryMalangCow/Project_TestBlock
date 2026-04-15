using System;
using UnityEngine;

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
    Accessory
}

[Serializable]
public class ItemData
{
    public int id;
    public string itemName;
    public string description;
    public int maxStack;
    public ItemType type;
    public float useTime;
    public Sprite icon;
}
