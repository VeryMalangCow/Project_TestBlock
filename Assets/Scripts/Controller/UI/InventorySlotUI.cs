using UnityEngine;
using UnityEngine.UI;
using TMPro;

public class InventorySlotUI : MonoBehaviour
{
    [Header("### UI References")]
    [SerializeField] protected Image iconImage;
    [SerializeField] protected TextMeshProUGUI stackText;

    [Header("### Config")]
    [SerializeField] protected ItemType targetType = ItemType.None; // None means any item

    protected InventoryUI ownerUI;
    protected int slotIndex = -1;
    protected int currentItemID = -2;

    public int SlotIndex => slotIndex;
    public ItemType TargetType => targetType;

    public virtual void Init(InventoryUI ui, int index)
    {
        ownerUI = ui;
        slotIndex = index;
        currentItemID = -2;
        ClearSlot();
    }

    public virtual void UpdateSlot(PlayerInventorySlotData slot)
    {
        int nextID = slot.IsEmpty ? -1 : slot.itemID;

        // 1. 수량은 항상 업데이트
        if (!slot.IsEmpty)
        {
            stackText.text = slot.stackCount > 1 ? slot.stackCount.ToString() : "";
            stackText.enabled = true;
        }

        if (nextID == currentItemID) return;

        // 2. ID가 바뀌었으므로 이미지 즉시 업데이트
        currentItemID = nextID;

        if (nextID == -1)
        {
            ClearSlotInternal();
            return;
        }

        // [중앙 캐시 호출]
        Sprite icon = ItemDataManager.Instance.GetItemIcon(nextID);
        if (icon != null)
        {
            iconImage.sprite = icon;
            iconImage.enabled = true;
        }
        else
        {
            ClearSlotInternal();
        }
    }

    public void ClearSlot()
    {
        currentItemID = -2;
        ClearSlotInternal();
    }

    protected virtual void ClearSlotInternal()
    {
        if (iconImage != null)
        {
            iconImage.sprite = null;
            iconImage.enabled = false;
        }
        if (stackText != null)
        {
            stackText.text = "";
            stackText.enabled = false;
        }
    }
}
