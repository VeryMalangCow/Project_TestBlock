using UnityEngine;

[System.Serializable]
public class WeaponProperty : IItemProperty, IUsable
{
    public int TargetButton => 0; // 좌클릭
    
    [Header("Stats")]
    public WeaponType weaponType;
    public int damage;
    public float speed; // 초당 공격 횟수
    public float reach;

    public void OnUseClient(UseContext context)
    {
        // 클라이언트: 애니메이션 재생 (PlayerVisuals의 설정값 참조)
        float offset = context.Player.Visuals.SwordSwingOffset;
        float baseAngle = context.Player.SwordSwingLockedTargetAngle - 90f;
        context.Player.Visuals.StartItemUseAnimation(baseAngle, GetUseDelay(), offset); 
    }

    public void OnUseServer(UseContext context)
    {
        // 서버: 전투 전용 컴포넌트에 로직 위임
        context.Player.Combat.PerformAttack(this, context);
    }

    public float GetUseDelay() => speed > 0 ? 1f / speed : 0.4f;
    public bool IsContinuous() => true;
    public bool ShouldLockFlip() => true;
    public bool IsAimingFollowMouse() => false;
    public ItemAnimationType GetAnimationType() => ItemAnimationType.Swing;
}

[System.Serializable]
public class PickaxeProperty : IItemProperty, IUsable
{
    public int TargetButton => 0; // 좌클릭
    
    [Header("Stats")]
    public int hardness; // 파괴 가능한 블록의 최대 강도
    public int power;    // 블록에 입히는 데미지
    public float speed;  // 초당 휘두르는 횟수
    public float rangeHeight; // 상하 사거리
    public float rangeWidth;  // 좌우 사거리

    public void OnUseClient(UseContext context)
    {
        // 1. 마우스 방향에 따른 각도 계산
        Vector2 dir = (context.MouseWorldPos - (Vector2)context.Player.transform.position).normalized;
        float targetAngle = Mathf.Atan2(dir.y, dir.x) * Mathf.Rad2Deg;
        if (context.Player.IsFlipped) targetAngle = 180f - targetAngle;

        // 2. 곡괭이 휘두르기 애니메이션 재생
        float offset = context.Player.Visuals.PickaxeSwingOffset;
        context.Player.Visuals.StartItemUseAnimation(targetAngle, GetUseDelay(), offset, true);

        // 3. 채굴 상태 업데이트 (진행도 계산 시작)
        context.Player.Mining.ProcessMiningClient(this, context);
    }

    public void OnUseServer(UseContext context)
    {
        // 서버: 원자적 검증 방식을 사용하므로 여기서는 아무것도 하지 않거나
        // 필요 시 서버측 애니메이션 트리거만 수행합니다.
    }

    public float GetUseDelay() => speed > 0 ? 1f / speed : 0.4f;
    public bool IsContinuous() => true;
    public bool ShouldLockFlip() => false;
    public bool IsAimingFollowMouse() => true;
    public ItemAnimationType GetAnimationType() => ItemAnimationType.Swing;
}

[System.Serializable]
public class BlockProperty : IItemProperty, IUsable
{
    public int TargetButton => 0; // 좌클릭 (또는 설정에 따라 우클릭)

    [Header("Stats")]
    public int hardness;
    public float maxHealth;

    public void OnUseClient(UseContext context)
    {
        // 마우스 방향에 따른 각도 계산 복구
        Vector2 dir = (context.MouseWorldPos - (Vector2)context.Player.transform.position).normalized;
        float targetAngle = Mathf.Atan2(dir.y, dir.x) * Mathf.Rad2Deg;
        if (context.Player.IsFlipped) targetAngle = 180f - targetAngle;
        float finalAngle = targetAngle; // [Fix] +90 제거하여 정면을 0도로 변경 (안정성 개선)

        // 클라이언트: 설치 애니메이션 (Stroke 방식 적용)
        float offset = context.Player.Visuals.BlockSwingOffset;
        context.Player.Visuals.StartItemUseAnimation(finalAngle, GetUseDelay(), offset, true);
    }

    public void OnUseServer(UseContext context)
    {
        // 서버: 설치 전용 컴포넌트에 로직 위임
        context.Player.Building.TryPlaceBlock(this, context);
    }

    public float GetUseDelay() => 0.2f;
    public bool IsContinuous() => true;
    public bool ShouldLockFlip() => false;
    public bool IsAimingFollowMouse() => true;
    public ItemAnimationType GetAnimationType() => ItemAnimationType.Place;
}

[System.Serializable]
public class EquipmentProperty : IItemProperty, IUsable
{
    public int TargetButton => 1; // 우클릭

    public void OnUseClient(UseContext context)
    {
        // 클라이언트: 장착 효과음 등을 여기서 재생할 수 있습니다.
    }

    public void OnUseServer(UseContext context)
    {
        // 서버: 플레이어 컨트롤러의 장비 교체 로직 호출
        context.Player.PerformQuickEquip(context.HotbarIndex);
    }

    public float GetUseDelay() => 0.2f;
    public bool IsContinuous() => false;
    public bool ShouldLockFlip() => false;
    public bool IsAimingFollowMouse() => false;
    public ItemAnimationType GetAnimationType() => ItemAnimationType.None;
}
