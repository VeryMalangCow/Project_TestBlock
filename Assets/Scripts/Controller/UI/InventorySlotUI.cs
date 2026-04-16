using UnityEngine;
using UnityEngine.UI;
using TMPro;

public class InventorySlotUI : MonoBehaviour
{
    [Header("### UI References")]
    [SerializeField] private Image iconImage;
    [SerializeField] private TextMeshProUGUI stackText;

    private InventoryUI ownerUI;
    private int slotIndex = -1;
    private int currentItemID = -2;

    public int SlotIndex => slotIndex;

    public void Init(InventoryUI ui, int index)
    {
        ownerUI = ui;
        slotIndex = index;
        currentItemID = -2;
        ClearSlot();
    }

    public void UpdateSlot(PlayerInventorySlotData slot)
    {
        int nextID = (slot == null || slot.IsEmpty) ? -1 : slot.itemID;

        // 1. 수량은 항상 업데이트 (이미지는 ID가 바뀔 때만)
        if (slot != null && !slot.IsEmpty)
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

        // [중앙 캐시 호출] 이제 비동기 핸들을 직접 관리하지 않습니다.
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

    private void ClearSlotInternal()
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
