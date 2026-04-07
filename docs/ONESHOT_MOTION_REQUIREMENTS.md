# One-Shot Motion Play 기능 요구사항

## 대상 클래스
`FluentTAvatarControllerFloatingHead` (Runtime + Editor)

## 배경

현재 `FluentTAvatarControllerFloatingHead`에는 특정 AnimationClip을 명시적으로 1회 재생(one-shot)하는 공개 API가 없다. 기존 애니메이션 재생은 모두 내부 이벤트/타이머 기반으로만 동작한다:

| 기존 기능 | 트리거 방식 | 재생 방식 |
|-----------|------------|-----------|
| Idle Animation | 자동 루프 (ExitTime transition) | AnimatorOverrideController swap-buffer |
| Gesture Animation | 서버 모션 태그 콜백 | AnimatorOverrideController + SetTrigger |
| Eye Blink | 타이머 코루틴 | TMAnimationComponent.Play() |

외부(Flutter 브릿지 등)에서 특정 모션을 즉시 1회 재생시키고, 완료 후 자동으로 Idle로 복귀하는 기능이 필요하다.

---

## 요구사항

### 1. Runtime API

#### 1-1. One-Shot Motion 등록 데이터 구조

Idle Animation의 `IdleAnimationEntry`와 유사한 패턴으로, One-Shot용 모션 목록을 Inspector에서 미리 등록할 수 있어야 한다.

```csharp
[System.Serializable]
public class OneShotMotionEntry
{
    public string motionId;          // 외부에서 호출할 고유 식별자 (예: "wave", "nod", "bow")
    public AnimationClip clip;       // 재생할 AnimationClip
    [Range(0f, 1f)]
    public float blendWeight = 1f;   // Animator 레이어 블렌드 가중치
}
```

**필드:**
- `motionId`: 외부 시스템(Flutter 등)에서 문자열로 모션을 지정할 수 있도록 하는 고유 ID
- `clip`: 재생할 Unity AnimationClip
- `blendWeight`: 재생 시 Animator 레이어의 블렌드 가중치 (0~1)

**저장:**
```csharp
[SerializeField] private List<OneShotMotionEntry> oneShotMotions = new List<OneShotMotionEntry>();
```

#### 1-2. Public API 메서드

```csharp
/// <summary>
/// Play a registered one-shot motion by its motionId.
/// The motion plays once and automatically returns to idle upon completion.
/// </summary>
/// <param name="motionId">The unique identifier of the registered motion</param>
/// <returns>true if the motion was found and playback started, false otherwise</returns>
public bool PlayOneShotMotion(string motionId)
```

**동작 명세:**
1. `oneShotMotions` 리스트에서 `motionId`와 일치하는 항목을 찾는다
2. 없으면 `Debug.LogWarning`을 출력하고 `false`를 반환한다
3. 있으면 해당 AnimationClip을 1회 재생하고 `true`를 반환한다
4. 재생 완료 후 자동으로 Idle 상태로 복귀한다
5. TalkMotion(음성 립싱크) 재생 중에는 one-shot 모션이 무시되거나, body 레이어에서만 재생되어 립싱크를 방해하지 않아야 한다

**추가 유틸리티 메서드:**

```csharp
/// <summary>
/// Get the list of registered one-shot motion IDs.
/// </summary>
public List<string> GetOneShotMotionIds()

/// <summary>
/// Check if a one-shot motion is currently playing.
/// </summary>
public bool IsOneShotMotionPlaying()

/// <summary>
/// Stop the currently playing one-shot motion immediately and return to idle.
/// </summary>
public void StopOneShotMotion()
```

#### 1-3. 애니메이션 재생 방식

기존 Gesture Animation의 재생 패턴(`ServerMotionTagging.cs`)을 참고한다.

**기존 Gesture 재생 방식 (참고용):**
- AnimatorOverrideController의 swap-buffer 패턴 사용 (`gesture_override_0`, `gesture_override_1`)
- `animator.SetTrigger("emotion0")` 또는 `"emotion1"`로 재생
- `emotionReset` 트리거로 Idle 복귀
- `LayerWeightController` StateMachineBehaviour로 레이어 가중치 제어

**One-Shot 재생 방식 선택지:**

- **Option A (권장): Gesture와 동일한 레이어/슬롯 재사용**
  - 별도 Animator 레이어나 override 슬롯을 추가하지 않고, 기존 gesture override 슬롯을 활용
  - 장점: Animator Controller 수정 불필요, 구현 단순
  - 단점: Gesture 재생과 동시 사용 불가 (실사용상 문제 없음 - one-shot은 대화 외 상황에서 사용)

- **Option B: 전용 Animator 레이어/슬롯 추가**
  - One-Shot 전용 override 슬롯(`oneshot_override_0`, `oneshot_override_1`)과 전용 Animator 레이어 추가
  - 장점: Gesture와 완전히 독립적으로 동작
  - 단점: Animator Controller 수정 필요, 복잡도 증가

**Idle 복귀:**
- AnimationClip의 재생이 끝나면(ExitTime 또는 상태 감지) 자동으로 Idle 상태로 전환
- 전환 시 crossfade 적용 (기존 gesture → idle 전환과 동일한 0.3s)

#### 1-4. Partial 파일 구성

기존 패턴에 따라 별도 partial 파일로 분리한다:

```
Runtime/FluentTAvatarControllerFloatingHead.OneShotMotion.cs
```

기존 partial 파일 패턴:
- `FluentTAvatarControllerFloatingHead.IdleAnimation.cs`
- `FluentTAvatarControllerFloatingHead.ServerMotionTagging.cs`
- `FluentTAvatarControllerFloatingHead.EyeBlink.cs`

---

### 2. Editor Inspector

#### 2-1. 탭 추가

기존 Editor는 탭 기반 UI를 사용한다:

```csharp
// 현재 탭 목록
private string[] _tabNames = {
    "Default Animation",    // Idle 설정
    "Look Target",          // 시선 추적
    "Text Emotion Detection", // 텍스트 감정 분석
    "Gesture Animation",    // 제스처 매핑
    "Eye Blink"             // 눈 깜빡임
};
```

**새 탭 추가:** `"One-Shot Motion"` 탭을 추가한다.

#### 2-2. Inspector UI 구성

새 탭의 UI 구성:

```
[One-Shot Motion 탭]
┌─────────────────────────────────────────────────┐
│ ℹ️ Register animation clips that can be played  │
│   once on demand via PlayOneShotMotion(motionId)│
├─────────────────────────────────────────────────┤
│ One-Shot Motions                           [+]  │
│ ┌─────────────────────────────────────────────┐ │
│ │ ▶ Element 0                                 │ │
│ │   Motion ID: [wave          ]               │ │
│ │   Clip:      [● WaveHand.anim      ]       │ │
│ │   Blend Weight: [━━━━━━━●━━] 0.8           │ │
│ ├─────────────────────────────────────────────┤ │
│ │ ▶ Element 1                                 │ │
│ │   Motion ID: [nod           ]               │ │
│ │   Clip:      [● HeadNod.anim       ]       │ │
│ │   Blend Weight: [━━━━━━━━━●] 1.0           │ │
│ └─────────────────────────────────────────────┘ │
│                                                 │
│ ⚠️ Duplicate Motion ID detected: "wave"        │
│    (중복 ID가 있을 경우 경고 표시)               │
└─────────────────────────────────────────────────┘
```

**Editor 기능:**
- `oneShotMotions` 리스트를 `EditorGUILayout.PropertyField`로 표시 (ReorderableList 또는 기본 리스트)
- 각 항목: `motionId` (string), `clip` (AnimationClip), `blendWeight` (slider 0~1)
- **Validation:** 중복 `motionId` 감지 시 HelpBox 경고 표시
- **Validation:** `motionId`가 비어있거나 `clip`이 null인 항목에 대해 경고 표시

#### 2-3. Editor Partial 파일

기존 패턴에 따라 별도 partial 파일로 분리:

```
Editor/FluentTAvatarControllerFloatingHeadEditor.OneShotMotion.cs
```

기존 Editor partial 파일 패턴:
- `FluentTAvatarControllerFloatingHeadEditor.DefaultAnimation.cs`
- `FluentTAvatarControllerFloatingHeadEditor.ServerMotionTagging.cs`
- `FluentTAvatarControllerFloatingHeadEditor.EyeBlink.cs`

---

### 3. 콜백 (선택사항)

외부 시스템에서 One-Shot 모션의 시작/종료를 감지할 수 있도록 콜백 제공을 고려한다:

```csharp
/// <summary>
/// Fired when a one-shot motion starts playing.
/// Parameter: motionId
/// </summary>
public UnityEvent<string> onOneShotMotionStarted;

/// <summary>
/// Fired when a one-shot motion finishes playing and returns to idle.
/// Parameter: motionId
/// </summary>
public UnityEvent<string> onOneShotMotionEnded;
```

이 콜백은 Flutter 브릿지에서 모션 재생 상태를 UI에 반영할 때 활용할 수 있다.

---

### 4. 기존 기능과의 상호작용

| 상황 | 동작 |
|------|------|
| Idle 재생 중 → PlayOneShotMotion 호출 | Idle 중단, One-Shot 재생, 완료 후 Idle 복귀 |
| One-Shot 재생 중 → PlayOneShotMotion 호출 | 현재 One-Shot 중단, 새 One-Shot 재생 |
| One-Shot 재생 중 → TalkMotion 시작 | One-Shot 중단, TalkMotion 우선 |
| TalkMotion 재생 중 → PlayOneShotMotion 호출 | 무시하고 false 반환 (TalkMotion 우선) |
| One-Shot 재생 중 → StopOneShotMotion 호출 | 즉시 중단, Idle 복귀 |

---

### 5. 사용 시나리오

```csharp
// 1. Inspector에서 미리 등록된 모션 목록 조회
List<string> motionIds = controller.GetOneShotMotionIds();
// → ["wave", "nod", "bow", "thumbsUp"]

// 2. 특정 모션 1회 재생
bool success = controller.PlayOneShotMotion("wave");

// 3. 재생 상태 확인
bool isPlaying = controller.IsOneShotMotionPlaying();

// 4. 강제 중단
controller.StopOneShotMotion();
```

**Flutter 브릿지 연동 예시 (향후):**
```
Flutter → sendToUnity("AvatarController", "PlayOneShotMotion", "wave")
Unity → PlayOneShotMotion("wave") 실행
Unity → onOneShotMotionStarted("wave") → jslib → Flutter 콜백
Unity → (재생 완료) → onOneShotMotionEnded("wave") → jslib → Flutter 콜백
```

---

## 참고: 기존 코드 구조

### Animator Override 패턴 (Gesture에서 사용 중)

```csharp
// ServerMotionTagging.cs 의 PlayMotionClip() 참고
private void PlayMotionClip(AnimationClip clip, float blendWeight)
{
    string overrideKey = currentGestureSlot == 0 ? "gesture_override_0" : "gesture_override_1";
    overrideController[overrideKey] = clip;
    animator.SetTrigger(currentGestureSlot == 0 ? "emotion0" : "emotion1");
    animator.ResetTrigger("emotionReset");
    currentGestureSlot = 1 - currentGestureSlot; // swap
}
```

### Idle Override 패턴 (IdleAnimation에서 사용 중)

```csharp
// IdleAnimation.cs 의 CheckIdleSwap() 참고
// - ExitTime transition 감지 후 다음 클립 로드
// - 2개 슬롯(idle_override_0, idle_override_1) 교대 사용
// - SelectNextIdleClip()으로 가중치 기반 랜덤 선택
```

### 파일 위치

```
패키지 루트: com.fluentt.avatar-controller-sample-floating-head@17ab93548597/

Runtime/
  FluentTAvatarControllerFloatingHead.cs              (메인 클래스, SerializeField 선언)
  FluentTAvatarControllerFloatingHead.IdleAnimation.cs (Idle 로직)
  FluentTAvatarControllerFloatingHead.ServerMotionTagging.cs (Gesture 로직)
  FluentTAvatarControllerFloatingHead.EyeBlink.cs      (Blink 로직)
  FluentTAvatarControllerFloatingHead.LookTarget.cs    (시선 추적)
  FluentTAvatarControllerFloatingHead.EmotionTagging.cs (감정 분석)
  LayerWeightController.cs                             (레이어 가중치 제어)

Editor/
  FluentTAvatarControllerFloatingHeadEditor.cs              (메인 에디터, 탭 UI)
  FluentTAvatarControllerFloatingHeadEditor.DefaultAnimation.cs (Idle 탭)
  FluentTAvatarControllerFloatingHeadEditor.ServerMotionTagging.cs (Gesture 탭)
  FluentTAvatarControllerFloatingHeadEditor.EyeBlink.cs      (Blink 탭)
  FluentTAvatarControllerFloatingHeadEditor.LookTarget.cs    (Look Target 탭)
  FluentTAvatarControllerFloatingHeadEditor.EmotionTagging.cs (Emotion 탭)
```
