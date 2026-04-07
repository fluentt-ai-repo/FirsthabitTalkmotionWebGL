# Eye Control / Eye Blink Override 복원 실패 버그 리포트

## 대상 패키지
`com.fluentt.avatar-controller-sample-floating-head`

## 요약
`overrideEyeControl` / `overrideEyeBlink` 기능이 `enableEyeControl` / `enableEyeBlink` 필드를 직접 변경하는 Suspend/Restore 패턴을 사용합니다. 여러 시스템(Idle, OneShot, Gesture)이 동시에 이 필드를 수정할 때, 예상치 못한 중단/전환 상황에서 원래 설정값으로 올바르게 복구되지 않는 문제가 있습니다.

---

## 현재 구조의 문제점

### 1. EyeBlink — 공유 Suspend 상태 충돌

**관련 파일:** `EyeBlink.cs` (L218-245), `IdleAnimation.cs` (L179), `OneShotMotion.cs` (L82)

**문제:** `isEyeBlinkSuspended` / `eyeBlinkValueBeforeSuspend` 가 Idle과 OneShot 사이에 공유됩니다.

```
상황 재현:
1. Idle A (overrideEyeBlink=true) 재생
   → SuspendEyeBlink(): saves enableEyeBlink=true, sets enableEyeBlink=false

2. OneShot (overrideEyeBlink=true) 재생
   → SuspendEyeBlink(): isEyeBlinkSuspended=true이므로 save 생략, enableEyeBlink=false 유지

3. OneShot 완료
   → RestoreEyeBlinkIfSuspended(): enableEyeBlink=true 복원, isEyeBlinkSuspended=false

4. ★ BUG: Idle A는 여전히 재생 중 (overrideEyeBlink=true)인데,
   EyeBlink이 true로 복원되어 깜빡임이 재생됨
```

### 2. EyeControl — 저장값 오염

**관련 파일:** `OneShotMotion.cs` (L195-212), `IdleAnimation.cs` (L184-200)

**문제:** Idle과 OneShot이 별도 플래그를 사용하지만, 둘 다 `enableEyeControl`을 직접 수정합니다.

```
상황 재현:
1. Idle (overrideEyeControl=true) 재생
   → SuspendEyeControlByIdle(): saves enableEyeControl=true, sets enableEyeControl=false

2. OneShot (overrideEyeControl=true) 재생
   → SuspendEyeControl(): saves enableEyeControl=false (Idle이 이미 꺼놓은 값!)

3. OneShot 완료
   → RestoreEyeControlIfSuspended(): enableEyeControl=false (잘못된 값으로 복원)

4. ★ Idle이 나중에 복원할 때까지 eye control이 죽어있는 구간 발생
   (Idle 복원 시점에서는 결과적으로 true가 되지만, 중간 타이밍이 잘못됨)
```

### 3. Gesture가 OneShot 채널을 재사용

**관련 파일:** `ServerMotionTagging.cs` (L109-118)

**문제:** `PlayMotionClip()`이 `SuspendEyeControl()` / `RestoreEyeControlIfSuspended()`를 호출하는데, 이는 OneShot의 suspend 상태(`isEyeControlSuspendedByOneShot`)를 사용합니다.

```
상황 재현:
1. TalkMotion 중 Gesture A (overrideEyeControl=true) 재생
   → SuspendEyeControl(): OneShot의 suspend 플래그에 저장

2. OneShot motion 재생 시도 (TalkMotion 우선이라 거부됨)

3. TalkMotion 종료 → ResetEmotionState()
   → RestoreEyeControlIfSuspended(): OneShot 플래그 사용하여 복원

문제: Gesture와 OneShot이 같은 플래그를 사용하므로,
동시 발생 시 서로의 상태를 덮어씀
```

---

## 제안 해결법: Suppression Flag 패턴

### 핵심 원칙
> `enableEyeControl` / `enableEyeBlink`을 **절대 직접 수정하지 않는다.**
> 대신 각 시스템이 독립적인 suppression 플래그를 설정하고,
> 실제 동작 시 "사용자 설정 AND 모든 플래그가 false"인 경우에만 활성화한다.

### 구현

#### 1. 메인 클래스에 Suppression 필드 추가 (`FluentTAvatarControllerFloatingHead.cs`)

```csharp
// ── Suppression Flags ──
// 각 시스템이 독립적으로 억제 플래그를 관리.
// enableEyeControl / enableEyeBlink 사용자 설정은 절대 변경하지 않음.
private bool _eyeControlSuppressedByIdle;
private bool _eyeControlSuppressedByOneShot;
private bool _eyeControlSuppressedByGesture;

private bool _eyeBlinkSuppressedByIdle;
private bool _eyeBlinkSuppressedByOneShot;
private bool _eyeBlinkSuppressedByGesture;

/// <summary>사용자 설정 AND 모든 억제 플래그가 해제된 상태</summary>
private bool IsEyeControlEffectivelyEnabled =>
    enableEyeControl
    && !_eyeControlSuppressedByIdle
    && !_eyeControlSuppressedByOneShot
    && !_eyeControlSuppressedByGesture;

/// <summary>사용자 설정 AND 모든 억제 플래그가 해제된 상태</summary>
private bool IsEyeBlinkEffectivelyEnabled =>
    enableEyeBlink
    && !_eyeBlinkSuppressedByIdle
    && !_eyeBlinkSuppressedByOneShot
    && !_eyeBlinkSuppressedByGesture;

/// <summary>
/// Safety net: OneShot/Gesture 억제 플래그 일괄 해제.
/// 예상치 못한 상황에서의 복구용.
/// Idle 억제는 Idle 시스템이 자체 관리하므로 여기서 건드리지 않음.
/// </summary>
private void ClearAllSuppressionFlags()
{
    _eyeControlSuppressedByOneShot = false;
    _eyeControlSuppressedByGesture = false;
    _eyeBlinkSuppressedByOneShot = false;
    _eyeBlinkSuppressedByGesture = false;
}
```

#### 2. LookTarget에서 effective 상태 사용 (`LookTarget.cs`)

```diff
 // UpdateLookTarget() 내부
- lookTargetController.enableEyeControl = enableEyeControl;
+ lookTargetController.enableEyeControl = IsEyeControlEffectivelyEnabled;
```

#### 3. EyeBlink 루틴에서 effective 상태 체크 (`EyeBlink.cs`)

```diff
 private IEnumerator BlinkRoutine()
 {
     while (true)
     {
         float variance = Random.Range(-blinkIntervalVariance, blinkIntervalVariance);
         float delay = Mathf.Max(BLINK_MIN_DELAY, blinkInterval + variance);
         yield return new WaitForSeconds(delay);

-        PlayBlinkAnimation();
+        if (IsEyeBlinkEffectivelyEnabled)
+        {
+            PlayBlinkAnimation();
+        }
     }
 }
```

**SuspendEyeBlink() / RestoreEyeBlinkIfSuspended() 전체 삭제:**
- `isEyeBlinkSuspended`, `eyeBlinkValueBeforeSuspend` 필드 삭제
- `SetEyeBlinkEnabled()`은 사용자 API로만 유지 (내부 override 시스템에서 호출 안 함)

#### 4. OneShotMotion 수정 (`OneShotMotion.cs`)

**삭제할 필드/메서드:**
- `isEyeControlSuspendedByOneShot`, `eyeControlValueBeforeOneShot`
- `SuspendEyeControl()`, `RestoreEyeControlIfSuspended()`

**대체:**
```csharp
// 새 helper
private void ClearOneShotSuppressionFlags()
{
    _eyeControlSuppressedByOneShot = false;
    _eyeBlinkSuppressedByOneShot = false;
}
```

**변경 위치:**

```diff
 // PlayOneShotMotion() - 79~82행
- if (entry.overrideEyeControl) SuspendEyeControl();
- if (entry.overrideEyeBlink) SuspendEyeBlink();
+ _eyeControlSuppressedByOneShot = entry.overrideEyeControl;
+ _eyeBlinkSuppressedByOneShot = entry.overrideEyeBlink;

 // StopOneShotMotionInternal() - 222~223행
- RestoreEyeControlIfSuspended();
- RestoreEyeBlinkIfSuspended();
+ ClearOneShotSuppressionFlags();

 // PlayNextGroupEntry() - 293~301행
- if (entry.overrideEyeControl) SuspendEyeControl();
- else RestoreEyeControlIfSuspended();
- if (entry.overrideEyeBlink) SuspendEyeBlink();
- else RestoreEyeBlinkIfSuspended();
+ _eyeControlSuppressedByOneShot = entry.overrideEyeControl;
+ _eyeBlinkSuppressedByOneShot = entry.overrideEyeBlink;

 // WaitForOneShotCompletion() - group invalid 분기 (260행)
- RestoreEyeControlIfSuspended();
+ ClearOneShotSuppressionFlags();

 // WaitForOneShotCompletion() - single play 완료 (269행)
- RestoreEyeControlIfSuspended();
+ ClearOneShotSuppressionFlags();
```

#### 5. IdleAnimation 수정 (`IdleAnimation.cs`)

**삭제할 필드/메서드:**
- `isEyeControlSuspendedByIdle`, `eyeControlValueBeforeIdle`
- `SuspendEyeControlByIdle()`, `RestoreEyeControlFromIdle()`

**대체:**
```diff
 // ApplyIdleOverrides()
- if (entry.overrideEyeControl) SuspendEyeControlByIdle();
- else RestoreEyeControlFromIdle();
- if (entry.overrideEyeBlink) SuspendEyeBlink();
- else RestoreEyeBlinkIfSuspended();
+ _eyeControlSuppressedByIdle = entry.overrideEyeControl;
+ _eyeBlinkSuppressedByIdle = entry.overrideEyeBlink;

 // OnSentenceStarted_IdleAnimation()
- RestoreEyeControlFromIdle();
- RestoreEyeBlinkIfSuspended();
+ _eyeControlSuppressedByIdle = false;
+ _eyeBlinkSuppressedByIdle = false;
```

#### 6. ServerMotionTagging 수정 (`ServerMotionTagging.cs`)

```diff
 // PlayMotionClip()
- if (entry.overrideEyeControl) SuspendEyeControl();
- else RestoreEyeControlIfSuspended();
- if (entry.overrideEyeBlink) SuspendEyeBlink();
- else RestoreEyeBlinkIfSuspended();
+ _eyeControlSuppressedByGesture = entry.overrideEyeControl;
+ _eyeBlinkSuppressedByGesture = entry.overrideEyeBlink;

 // ResetEmotionState()
- RestoreEyeControlIfSuspended();
- RestoreEyeBlinkIfSuspended();
+ ClearAllSuppressionFlags();
```

---

## 변경 전후 비교

### Before (문제 있는 구조)
```
enableEyeControl = true  (사용자 설정)
    ↓ Idle이 false로 변경
    ↓ OneShot이 또 false로 변경 (이미 false인 값을 저장)
    ↓ OneShot 복원 → false로 복원 (잘못됨!)
    ↓ Idle 복원 → true로 복원 (맞지만 타이밍 문제)
```

### After (제안 구조)
```
enableEyeControl = true  (사용자 설정, 절대 변경 안 됨)

Effective = enableEyeControl && !Idle억제 && !OneShot억제 && !Gesture억제
         = true && !true && !true && !false = false  (두 시스템이 동시 억제 가능)

OneShot 완료 → _eyeControlSuppressedByOneShot = false
Effective = true && !true && !false && !false = false  (Idle이 아직 억제 중이므로 여전히 off)

Idle 전환 → _eyeControlSuppressedByIdle = false
Effective = true && !false && !false && !false = true  (모두 해제, 정상 복귀)
```

### 장점
1. **사용자 설정값 불변** — save/restore 자체가 필요 없으므로 복구 실패 원천 차단
2. **시스템 간 독립** — Idle/OneShot/Gesture가 서로의 상태를 오염시킬 수 없음
3. **안전망** — `ClearAllSuppressionFlags()`로 예상치 못한 상황에서도 강제 복구 가능
4. **코드 단순화** — Suspend/Restore 메서드 6개 삭제, 플래그 단순 할당으로 대체
