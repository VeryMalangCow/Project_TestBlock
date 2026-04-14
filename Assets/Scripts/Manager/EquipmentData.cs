using System;

[Serializable]
public class EquipmentData
{
    public int helmetIndex = -1;     // -1 means no equipment
    public int chestplateIndex = -1;
    public int leggingsIndex = -1;

    public EquipmentData()
    {
        helmetIndex = -1;
        chestplateIndex = -1;
        leggingsIndex = -1;
    }
}
