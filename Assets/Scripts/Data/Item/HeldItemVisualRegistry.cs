using UnityEngine;

/// <summary>
/// 아이템 타입(ItemType)에 따른 들고 있는 아이템의 시각적 설정(피벗, 회전)을 관리하는 레지스트리입니다.
/// 모든 피벗은 64x64 규격의 왼쪽 하단(0,0)을 기준으로 한 픽셀 좌표입니다.
/// </summary>
public static class HeldItemVisualRegistry
{
    public struct HeldSettings
    {
        public Vector2 pivot;
        public float rotation;

        public HeldSettings(float px, float py, float rot)
        {
            pivot = new Vector2(px, py);
            rotation = rot;
        }
    }

    public static HeldSettings GetSettings(ItemType type)
    {
        switch (type)
        {
            case ItemType.Block:
                // 블럭은 보통 16x16 아이콘이 (0,0)에 있으므로 손잡이를 (8,8) 중앙으로 잡음
                return new HeldSettings(8f, 8f, 0f);

            case ItemType.Sword:
                // 검은 왼쪽 하단(4,4) 정도를 손잡이로 잡고 45도 기울임
                return new HeldSettings(4f, 4f, -45f);

            case ItemType.Tool:
                // 곡괭이/도구는 손잡이 위치를 잡고 0도 유지 (애니메이션에서 흔듦)
                return new HeldSettings(6f, 6f, 0f);

            case ItemType.Consumable:
                // 소비템(물약 등)은 하단 중앙
                return new HeldSettings(8f, 4f, 0f);

            case ItemType.Helmet:
            case ItemType.Chestplate:
            case ItemType.Leggings:
            case ItemType.Boots:
            case ItemType.Jetbag:
                // 장비류를 손에 들었을 때의 기본값
                return new HeldSettings(8f, 8f, 0f);

            default:
                return new HeldSettings(8f, 8f, 0f);
        }
    }
}
