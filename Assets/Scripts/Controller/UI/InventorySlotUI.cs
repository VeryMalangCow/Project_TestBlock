using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using TMPro;

public class InventorySlotUI : MonoBehaviour, IPointerClickHandler
{
    [Header("### UI References")]
    [SerializeField] private Image iconImage;
    [SerializeField] private TextMeshProUGUI stackText;

    private InventoryUI ownerUI;
    private int slotIndex = -1;

    public void Init(InventoryUI ui, int index)
    {
        ownerUI = ui;
        slotIndex = index;
        ClearSlot();
    }

    public void UpdateSlot(PlayerInventorySlotData slot)
    {
        if (slot == null || slot.IsEmpty)
        {
            ClearSlot();
            return;
        }

        ItemData data = ItemDataManager.Instance.GetItem(slot.itemID);
        if (data != null)
        {
            iconImage.sprite = data.icon;
            iconImage.enabled = data.icon != null;
            
            stackText.text = slot.stackCount > 1 ? slot.stackCount.ToString() : "";
            stackText.enabled = true;
        }
        else
        {
            ClearSlot();
        }
    }

    public void ClearSlot()
    {
        iconImage.sprite = null;
        iconImage.enabled = false;
        stackText.text = "";
        stackText.enabled = false;
    }

    public void OnPointerClick(PointerEventData eventData)
    {
        if (ownerUI != null && slotIndex != -1)
        {
            ownerUI.OnSlotClicked(slotIndex);
        }
    }
}
