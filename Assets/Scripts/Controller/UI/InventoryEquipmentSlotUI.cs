using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// 장비 전용 인벤토리 슬롯입니다. 아이템 이름 표시 및 플레이스홀더 자동 토글 기능을 제공합니다.
/// </summary>
public class InventoryEquipmentSlotUI : InventorySlotUI
{
    [Header("### Equipment Specialized UI")]
    [SerializeField] private Image placeholderImage; // 아무것도 없을 때 보여줄 배경 아이콘 (투명도 조절용)
    [SerializeField] private TextMeshProUGUI itemNameText; // 장착 중인 아이템의 이름 표시

    public override void UpdateSlot(PlayerInventorySlotData slot)
    {
        // 1. 기본 슬롯 로직 실행 (아이콘 갱신 등)
        base.UpdateSlot(slot);

        // 2. 장비 전용 추가 로직
        if (slot.IsEmpty)
        {
            SetPlaceholderVisible(true);
            if (itemNameText != null) itemNameText.text = "";
        }
        else
        {
            SetPlaceholderVisible(false);
            
            // 아이템 이름 업데이트
            ItemData data = ItemDataManager.Instance.GetItem(slot.itemID);
            if (itemNameText != null && data != null)
            {
                itemNameText.text = data.itemName;
            }
        }
    }

    protected override void ClearSlotInternal()
    {
        base.ClearSlotInternal();
        
        SetPlaceholderVisible(true);
        if (itemNameText != null) itemNameText.text = "";
    }

    private void SetPlaceholderVisible(bool visible)
    {
        if (placeholderImage != null)
        {
            // 완전 비활성화보다는 투명도나 enabled를 조절하여 레이아웃을 유지합니다.
            placeholderImage.enabled = visible;
        }
    }
}
