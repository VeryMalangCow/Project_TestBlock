using UnityEngine;

/// <summary>
/// 아이템 사용 시 필요한 정보를 담는 컨텍스트 구조체입니다.
/// </summary>
public struct UseContext
{
    public PlayerController Player;
    public Vector2 MouseWorldPos;
    public int HotbarIndex;
    public int ItemID;
    public int ButtonIndex; // 0: Left, 1: Right
}

/// <summary>
/// 아이템이 가질 수 있는 애니메이션 타입입니다.
/// </summary>
public enum ItemAnimationType
{
    None,
    Swing,   // 일반적인 휘두르기 (무기, 도구)
    Stab,    // 찌르기 (창)
    Hold,    // 들고 있기 (포션 등)
    Place    // 설치 (블록)
}

/// <summary>
/// 모든 아이템 속성의 기반 인터페이스입니다.
/// </summary>
public interface IItemProperty { }

/// <summary>
/// 사용 가능한 아이템의 동작을 정의하는 인터페이스입니다.
/// </summary>
public interface IUsable
{
    // 사용 가능한 마우스 버튼 (0: 좌, 1: 우, 2: 둘 다)
    int TargetButton { get; }

    // 클라이언트 측 실행 (애니메이션, 사운드, 이펙트 등)
    void OnUseClient(UseContext context);

    // 서버 측 실행 (데미지 판정, 블록 수정, 소모 등)
    void OnUseServer(UseContext context);

    float GetUseDelay();
    bool IsContinuous(); // 누르고 있을 때 자동 재사용 여부
    bool ShouldLockFlip(); // 아이템 사용 중 방향 전환을 잠글지 여부
    ItemAnimationType GetAnimationType();
}

/// <summary>
/// 아무 기능도 없는 아이템을 위한 기본 구현체입니다.
/// </summary>
public class NullUsable : IUsable
{
    public int TargetButton => -1;
    public void OnUseClient(UseContext context) { }
    public void OnUseServer(UseContext context) { }
    public float GetUseDelay() => 0.2f;
    public bool IsContinuous() => false;
    public bool ShouldLockFlip() => false;
    public ItemAnimationType GetAnimationType() => ItemAnimationType.None;
}
