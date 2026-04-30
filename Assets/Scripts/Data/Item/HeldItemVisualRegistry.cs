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
        public Vector2 usePivot;    // [New] 아이템 사용 중일 때의 피봇
        public float useRotation; // [New] 아이템 사용 중일 때의 회전값

        public HeldSettings(float px, float py, float rot, float upx = -1, float upy = -1, float urot = -1)
        {
            pivot = new Vector2(px, py);
            rotation = rot;
            usePivot = (upx == -1) ? pivot : new Vector2(upx, upy);
            useRotation = (urot == -1) ? rotation : urot;
        }
    }

    public static HeldSettings GetSettings(ItemType type)
    {
        switch (type)
        {
            case ItemType.Block:
                return new HeldSettings(24f, 24f, 0f, 24f, 24f, 0f);

            case ItemType.Weapon:
                // 일반: 270도, 사용 시(Sword 등): 225도
                // 사용 시 피봇(usePivot)은 225도 회전 상태에서 손잡이가 올바른 위치에 오도록 설정해야 합니다.
                return new HeldSettings(4f, 60f, 270f, 32f, 76f, 225f);

            case ItemType.Tool:
                return new HeldSettings(32f, 32f, 0f, 32f, 32f, 0f);

            case ItemType.Consumable:
                return new HeldSettings(32f, 32f, 0f, 32f, 32f, 0f);

            case ItemType.Helmet:
            case ItemType.Chestplate:
            case ItemType.Leggings:
            case ItemType.Boots:
            case ItemType.Jetbag:
                return new HeldSettings(24f, 24f, 0f, 24f, 24f, 0f);

            default:
                return new HeldSettings(18f, 32f, 0f, 18f, 32f, 0f);
        }
    }
}
