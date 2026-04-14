using System;

[Serializable]
public class PlayerEquipmentData
{
    public int helmetIndex = -1;
    public int chestplateIndex = -1;
    public int leggingsIndex = -1;

    public PlayerEquipmentData()
    {
        helmetIndex = -1;
        chestplateIndex = -1;
        leggingsIndex = -1;
    }
}
