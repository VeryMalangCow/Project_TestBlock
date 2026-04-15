using System;

[Serializable]
public class PlayerData
{
    // Appearance (Visuals)
    public PlayerVisualData visual = new PlayerVisualData();

    // Equipment
    public PlayerEquipmentData equipment = new PlayerEquipmentData();

    // Inventory
    public PlayerInventoryData inventory = new PlayerInventoryData(50); 

    public PlayerData()
    {
        visual = new PlayerVisualData();
        equipment = new PlayerEquipmentData();
        inventory = new PlayerInventoryData(50);
    }
}
