# Feature Request: Base Expression (기본 표정) API

## 요약

캐릭터별 고유한 기본 표정(미소, 눈매, 입꼬리 등)을 **모든 상태(idle, 대화, 제스처)에서 항상 유지**할 수 있는 API를 요청합니다.

---

## 문제 상황

### 현재 구조

현재 FluentT TalkMotion SDK의 표정 제어 흐름:

```
TMAnimationComponent 레이어 순서:
  -3: Base face expression (Update)
  -2: Face animation / lip-sync (LateUpdate)
  -1: Head animation (LateUpdate)
   0+: External layers - eye blink 등 (LateUpdate)
```

### 문제

캐릭터별 기본 표정을 Unity AnimationClip으로 만들어 idle 애니메이션에 적용하고 있습니다.
그런데 **대화(TalkMotion), 제스처(emotion trigger), one-shot 모션** 재생 시 SDK가 표정을 직접 제어하면서 기본 표정이 사라집니다.

| 상태 | 기본 표정 유지 여부 |
|------|-------------------|
| Idle | O (idle 애니메이션에 포함) |
| 대화 중 (lip-sync) | X (SDK가 표정 덮어쓰기) |
| 제스처/감정 모션 | X (Animator override) |
| 대화 종료 후 idle 복귀 | O (idle 복귀 시 다시 적용) |

### 왜 외부에서 해결하기 어려운가

시도한 접근과 한계:

1. **Animator additive layer**: SDK가 `SetBlendShapeWeight()`로 직접 제어하므로 Animator 이후에 덮어씌워져 무효
2. **TMAnimationComponent 외부 레이어(0+)에 additive clip 삽입**: 
   - `AnimationClip.SampleAnimation()`으로 값을 추출하는 우회적 방법 필요
   - mesh blendshape name ↔ ARKit name 매핑을 외부에서 알 수 없음
   - SDK 내부 레이어 시스템에 대한 높은 결합도
3. **OnLateUpdateCompleted 콜백에서 수동 적용**:
   - SDK 제어 흐름 외부에서 blendshape를 직접 수정하는 것은 불안정

---

## 제안하는 API

### 옵션 A: 단순 API (권장)

```csharp
// AnimationClip 기반 - 기존 Unity workflow와 호환
fluentTAvatar.SetBaseExpression(AnimationClip clip);

// 해제
fluentTAvatar.ClearBaseExpression();
```

- SDK 내부에서 clip의 blendshape 커브를 파싱
- 내부 레이어(-3 이전 또는 -3과 병합)에서 additive로 적용
- 대화, 제스처 등 모든 상태에서 기본값으로 유지

### 옵션 B: TMAnimationClip 기반

```csharp
// TMAnimationClip 직접 전달
fluentTAvatar.SetBaseExpression(TMAnimationClip clip, TMAnimationLayer.BlendMode mode);
```

### 옵션 C: Dictionary 기반 (가장 단순)

```csharp
// ARKit blendshape name → value 직접 전달
fluentTAvatar.SetBaseExpression(Dictionary<string, float> blendShapeValues);

// 예시
fluentTAvatar.SetBaseExpression(new Dictionary<string, float>
{
    { "mouthSmileLeft", 15f },
    { "mouthSmileRight", 15f },
    { "eyeSquintLeft", 8f },
    { "eyeSquintRight", 8f },
});
```

---

## 유스케이스

### 1. 캐릭터 개성 표현
각 캐릭터마다 고유한 기본 표정이 있음:
- 캐릭터 A: 약간 미소 (mouthSmileLeft/Right ~15)
- 캐릭터 B: 진지한 표정 (browDownLeft/Right ~10)
- 캐릭터 C: 밝은 표정 (cheekSquintLeft/Right ~12, mouthSmileLeft/Right ~20)

### 2. 런타임 아바타 교체
`ChangeAvatar()` 호출 시 새 아바타의 기본 표정을 즉시 적용해야 함.

### 3. 다중 캐릭터 프로젝트
10개 이상의 캐릭터 프리팹이 있고, 각각 다른 기본 표정을 가짐.
모든 애니메이션에 캐릭터별 표정을 bake하는 것은 비현실적 (N캐릭터 x M애니메이션 조합).

---

## 기대하는 동작

```
적용 순서 (제안):

  [Base Expression] ← 신규: 캐릭터 기본 표정 (additive, 항상 적용)
       ↓
  [-3] Base face (SDK 내부)
       ↓
  [-2] Lip-sync / face animation
       ↓
  [-1] Head animation
       ↓
  [0+] External layers (eye blink 등)
```

- Base Expression은 **항상 additive로 적용**
- 대화 중 lip-sync와 자연스럽게 합성 (미소 + 입모양)
- 제스처 중에도 기본 표정 유지
- `ClearBaseExpression()` 호출 시 즉시 해제
- 아바타 교체 시 자동 초기화 (또는 수동 재설정)

---

## 프로젝트 컨텍스트

- **프로젝트**: WebGL 기반 TalkMotion 아바타 (iframe 임베딩)
- **SDK 버전**: com.fluentt.talkmotion@2b94862b2c7d
- **컨트롤러**: FluentTAvatarControllerFloatingHead
- **아바타 수**: 10개 (확장 예정)
- **표정 데이터 형식**: Unity AnimationClip (.anim), ARKit 52 blendshape 기준

---

## 참고: 현재 TMAnimationComponent 레이어 구조

```
SortedDictionary<int, TMAnimationLayer>

내부 레이어 (음수 인덱스):
  -3: Base face expression    → Update phase
  -2: Face animation          → LateUpdate phase
  -1: Head animation          → LateUpdate phase

외부 레이어 (0+ 인덱스):
   0: Eye blink (sample)      → LateUpdate phase
   1+: 사용자 확장             → LateUpdate phase

처리 순서: 오름차순 (-3 → -2 → -1 → 0 → 1 → ...)
블렌드 모드: Override, Additive, SoftMax2D, Max
```

### 관련 SDK 파일

| 파일 | 역할 |
|------|------|
| `TMAnimationComponent.cs` | 레이어 관리, Play/Stop API |
| `TMAnimationClipInst.cs` | blendshape 적용 (`SetBlendShapeWeight`) |
| `TMAnimationLayer.cs` | BlendMode, UpdatePhase 정의 |
| `TMAnimationClip.cs` | 클립 데이터 구조 |
| `FluentTAvatar.cs` | Update/LateUpdate 흐름 관리 |
