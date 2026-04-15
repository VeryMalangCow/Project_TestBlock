# Character Visual & Data System Specification

이 문서는 `Project_BlockTest`의 캐릭터 비주얼 시스템과 데이터 구조, 그리고 향후 개발 우선순위를 정의합니다.

## 1. 캐릭터 비주얼 시스템 (Character Visuals)

캐릭터는 여러 개의 독립적인 레이어로 구성되며, 각 레이어는 개별 `SpriteRenderer`를 가집니다. 모든 레이어는 애니메이션 프레임 인덱스에 따라 동기화되어 작동합니다.

### 레이어 구성 (Sorting Order 순)
1. **ArmBack**: 캐릭터 몸체 뒤쪽 팔 (Skin Color 적용)
2. **Leg**: 다리 (Skin Color 적용)
3. **Body**: 몸통 (Skin Color 적용)
4. **Head**: 머리 (Skin Color 적용)
5. **Eye**: 고정된 눈 흰자/윤곽 이미지
6. **Pupil**: 눈동자 (Eye Color 적용)
7. **Hair**: 머리카락 스타일 (Hair Color 적용, **Helmet 착용 시 숨김**)
8. **Chestplate**: 상의 갑옷 (Equipment)
9. **Leggings**: 하의 갑옷 (Equipment)
10. **Helmet**: 투구 (Equipment)
11. **ArmFront**: 캐릭터 몸체 앞쪽 팔 (Skin Color 적용)

### 애니메이션 프레임 규칙
- **0**: Idle (대기)
- **1 ~ 9**: Walk (걷기 루프)
- **9**: Jump / In-Air (공중 상태, 걷기 마지막 프레임 공유)
- **10**: Dash (대시 상태)

---

## 2. 데이터 구조 (Data Architecture)

플레이어의 데이터는 가독성과 확장성을 위해 계층 구조로 설계되었습니다.

### PlayerData (Root)
- **PlayerVisualData `visual`**: 외형 및 색상 데이터
- **PlayerEquipmentData `equipment`**: 장착 중인 아이템 인덱스
- **int[] `inventorySlots`**: 아이템 슬롯 데이터 (현재 50슬롯 껍데기)

### PlayerVisualData
- `skinColorHex`: 피부 색상 (Hex String)
- `eyeColorHex`: 눈동자 색상 (Hex String)
- `hairColorHex`: 머리카락 색상 (Hex String)
- `hairStyleIndex`: 머리카락 스타일 번호 (int)

### PlayerEquipmentData
- `helmetIndex`: 투구 아이템 인덱스 (-1은 미착용)
- `chestplateIndex`: 상의 아이템 인덱스 (-1은 미착용)
- `leggingsIndex`: 하의 아이템 인덱스 (-1은 미착용)

---

## 3. 네트워크 및 성능 최적화 (Networking)

### 이벤트 기반 리소스 로딩
- `NetworkVariable`을 통해 각 속성을 개별적으로 동기화합니다.
- **OnValueChanged** 이벤트를 사용하여, 데이터가 실제로 변경되었을 때만 `Resources.Load`를 수행합니다.
- 이동 중인 프레임 교체는 캐싱된 메모리 참조만 변경하므로 성능 부하가 매우 낮습니다.

### 업데이트 함수
- `UpdateAppearance(PlayerVisualData)`: 외형 데이터 일괄 변경 및 동기화
- `UpdateEquipment(PlayerEquipmentData)`: 장비 데이터 일괄 변경 및 동기화

---

## 4. 향후 개발 로드맵 (Roadmap)

데이터 모델링과 저장 시스템의 안정성을 위해 다음 순서로 개발을 진행합니다.

1. **[Priority 1] 인벤토리 구조 설계 (Inventory System)**
   - `ItemData` (ScriptableObject) 정의
   - 아이템 ID, 수량 등을 포함하는 `InventorySlot` 클래스 구체화
2. **[Priority 2] 데이터 지속성 구현 (Persistence)**
   - `JsonUtility`를 이용한 월드와 플레이어 데이터 분리 저장 시스템 구축
   - 테라리아 방식의 캐릭터/월드 개별 파일 시스템 설계
3. **[Priority 3] 게임 진입 흐름 및 UI (UX/Flow)**
   - 캐릭터 생성 UI (색상 선택, 머리 스타일 변경)
   - 월드 생성 및 선택 UI
   - 메인 메뉴에서 인게임 월드로의 데이터 전달 시스템

---
*Last Updated: 2026-04-14*
