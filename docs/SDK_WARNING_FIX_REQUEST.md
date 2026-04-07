# SDK 경고 수정 요청

## 개요
아바타를 `Instantiate()`로 런타임 생성하는 환경(WebGL 빌드)에서 Look Target 관련 경고가 발생하며, 시선 추적 기능이 정상 동작하지 않습니다.

핵심 문제는 **런타임 Instantiate 시 VirtualTargets가 자동 생성되지 않는 것**이며, 현재 VirtualTargets 생성은 에디터 전용 도구에만 의존하고 있습니다.

---

## 핵심 요청: 런타임 VirtualTargets 자동 생성

### 현재 동작 (문제)

```
[LookTargetController] marie_VirtualTargets not found! Please enable Look Target in Editor to auto-create.
```

**발생 경로**: `LookTargetController.cs` → `FindVirtualTargets()` → `GameObject.Find("VirtualTargets")` → 아바타별 자식 탐색 실패

**현재 흐름**:
1. 프리팹에 `headVirtualTargetRef` 등 serialized 참조가 **null**
2. → `InitializeLookTarget()`에서 `useFindMethod: true`로 fallback (`FluentTAvatarControllerFloatingHead.LookTarget.cs:114, 130`)
3. → `LookTargetController.FindVirtualTargets()` 실행
4. → `GameObject.Find("VirtualTargets")` 컨테이너 탐색
5. → `{avatarName}_VirtualTargets` 자식 오브젝트 탐색 → **씬에 없음** → 경고 + 시선 추적 미작동

**근본 원인**: VirtualTargets 오브젝트는 에디터에서 Look Target을 활성화할 때만 생성됩니다. `Instantiate()`로 런타임 생성된 아바타에 대해서는 VirtualTargets가 만들어지지 않습니다.

### 요청사항

`Instantiate()`로 생성된 아바타가 `Awake()`/`InitializeLookTarget()` 과정에서 **VirtualTargets를 자동으로 생성**하도록 런타임 코드 수정 요청:

1. **VirtualTargets 컨테이너 자동 생성**: 씬에 `VirtualTargets` GameObject가 없으면 런타임에서 자동 생성
2. **아바타별 VirtualTarget 그룹 자동 생성**: `{avatarName}_VirtualTargets` 및 하위 오브젝트(`HeadVirtualTarget`, `EyeVirtualTarget` 또는 `LeftEyeVirtualTarget`/`RightEyeVirtualTarget`) 자동 생성
3. **serialized 참조 자동 할당**: 생성된 VirtualTarget 오브젝트를 `headVirtualTargetRef`, `eyeVirtualTargetRef` 등에 자동 할당하여 `GameObject.Find` fallback을 사용하지 않도록 처리

**이상적인 동작**:
```
Instantiate(avatarPrefab)
→ Awake() / InitializeLookTarget()
→ VirtualTargets 없으면 자동 생성
→ serialized 참조 자동 설정
→ useFindMethod: false 경로 사용
→ 경고 없이 시선 추적 정상 작동
```

**또는** `SetLookTarget(Transform target)` 호출 한 번으로 VirtualTargets 생성 + 참조 설정 + 초기화가 모두 완료되는 API 제공.

### 우선순위: **최상 (기능 버그)**

시선 추적은 아바타 품질에 직접적인 영향을 주는 기능이며, 런타임 Instantiate는 WebGL 배포의 표준 사용 패턴입니다.

---

## 버그: PlayPrepared 재생 완료 후 isTalkMotionActive가 false로 복구되지 않음

```
[FluentTAvatarControllerFloatingHead] TalkMotion is currently playing. One-shot group 'Listening' ignored.
```

**발생 조건**: `PrepareAudio` → `Play(cacheId)` → 재생 완료 → 아바타 멈춤 (idle 미복귀) → OneShot 시도 → 거부됨

**원인 분석**:

`isTalkMotionActive` 플래그의 생명주기:
1. `OnSentenceStarted_OneShotMotion()` (line 318) → `isTalkMotionActive = true`
2. `OnSentenceEnded_OneShotMotion(isLastSentence: true)` (line 339) → `isTalkMotionActive = false`

`PrepareAudio` → `PlayPrepared()` 경로에서 재생 완료 후 `OnSentenceEnded`의 `isLastSentence`가 `true`로 전달되지 않거나, `OnSentenceEnded` 자체가 호출되지 않아 `isTalkMotionActive`가 `true`로 남아있는 것으로 추정됩니다.

**결과**:
- 아바타가 idle 모션으로 복귀하지 않고 멈춤
- `isTalkMotionActive = true`가 유지되어 모든 OneShot 모션 호출이 거부됨
- `StopTalkMotion()`을 호출해도 이 플래그를 리셋하지 않으면 OneShot이 영구적으로 차단됨

**요청사항**:
- `PlayPrepared()` 경로에서 재생 완료 시 `isLastSentence = true`가 정상 전달되도록 수정
- 또는 `StopTalkMotion()` 호출 시 `isTalkMotionActive = false`로 강제 리셋
- 추가 안전장치: 일정 시간 이상 TalkMotion 이벤트가 없으면 자동 리셋 (timeout fallback)

**우선순위: 최상 (기능 버그)** — 재생 후 아바타가 완전히 멈추며, OneShot 포함 모든 후속 기능이 차단됨

---

## 부가 경고: Microphone not supported on WebGL

```
[FluentTAvatarDebugger] Microphone not supported on WebGL
```

**발생 위치**: `com.fluentt.talkmotion` → `FluentTAvatarDebugger.cs`

**원인**: `Awake()`에서 WebGL 빌드 시 항상 이 경고를 출력

**영향**: 기능에는 영향 없음 (디버거 전용 컴포넌트)

**요청사항**: `LogWarning` → `Log`로 변경하거나, WebGL에서는 출력하지 않도록 수정

**우선순위: 낮음**

---

## 부가 경고: tagged_motion is NULL

```
[TalkMotionClient] SpeechMotion Response: tagged_motion is NULL (no emotion tagging in response)
```

**발생 위치**: `com.fluentt.talkmotion` → `TalkMotionClient` (서버 응답 수신 시)

**원인**: 서버가 `tagged_motion` 데이터를 응답에 포함하지 않을 때 매번 LogWarning 출력. 클라이언트 측 텍스트 패턴 기반 감정 태깅(`EmotionKeywordDataset`)을 사용하는 환경에서는 서버 태깅이 불필요하므로 항상 이 경고가 발생함.

**영향**: 기능에는 영향 없음 (클라이언트 태깅으로 정상 동작). 그러나 **재생할 때마다 콘솔에 출력되어 외부 협력사 환경에서 거슬림**.

**요청사항**:
- `LogWarning` → `Log`로 변경하거나, 서버 태깅을 사용하지 않는 경우 출력하지 않도록 수정
- 또는 로그 레벨 설정 옵션 제공 (verbose/quiet 모드)

**우선순위: 중간** (기능 무관하나, 협력사 제공 시 콘솔 품질 영향)

---

## 요청: SDK 로그 레벨 관리

아바타 1회 소환 시 SDK에서 약 **15줄의 Debug.Log**가 출력됩니다. 외부 협력사에 제공하는 WebGL 빌드 환경에서는 콘솔 로그 과다가 신뢰도 문제로 이어집니다.

**현재 출력되는 SDK 초기화 로그 (아바타 1회 소환 기준)**:
```
[FluentTAvatar] Unity Timeline initialized
[FluentTAvatar] Applied custom server settings: https://api.talkmotion.ai    ← 2회 중복
[FluentTAvatar] Talkmotion mode initialized for marie(Clone)                 ← 2회 중복
[FluentTAvatarDebugger] Microphone not supported on WebGL, skipping initialization
[FluentTAvatarDebugger] Response Saving DISABLED
[FluentTAvatar] Base expression set: 4 blendshape targets from 4 ARKit values.
[FluentTAvatarControllerFloatingHead] Idle slot 0 initialized with 06_v3_Take 001
[FluentTAvatarControllerFloatingHead] Idle slot 1 initialized with aa_v1_Take 001 (2)
[FluentTAvatarControllerFloatingHead] Created marie_VirtualTargets group
[FluentTAvatarControllerFloatingHead] VirtualTargets auto-created and rig rebuilt for runtime Instantiate
[FluentTAvatarControllerFloatingHead] Using virtual target references (optimized)
[FluentTAvatarControllerFloatingHead] Look target initialized
[FluentTAvatarControllerFloatingHead] Client emotion tagging initialized with 14 pattern groups compiled
[FluentTAvatarControllerFloatingHead] Server motion tagging initialized
[FluentTAvatarControllerFloatingHead] Eye blink initialized with clip: DefaultEyeBlink, blendMode: SoftMax2D
```

**문제점**:
- `Applied custom server settings`와 `Talkmotion mode initialized`가 2회 중복 출력
- 내부 초기화 상세 정보(Idle slot, emotion tagging, eye blink 등)가 모두 `Debug.Log`로 출력
- `FluentTAvatarDebugger` 관련 로그가 프로덕션 빌드에서도 출력

**요청사항**:
1. **로그 레벨 설정 옵션 제공**: `enableVerboseLogging` 같은 플래그로 상세 초기화 로그 ON/OFF
2. **중복 로그 제거**: `Applied custom server settings`, `Talkmotion mode initialized` 2회 → 1회
3. **Debug.Log → 조건부 출력**: 초기화 성공 로그는 verbose 모드에서만, 경고/에러만 기본 출력
4. **FluentTAvatarDebugger**: WebGL 빌드에서 자동 비활성화 또는 로그 억제

**우선순위: 중간** — 기능에 영향 없으나, 외부 제공 시 품질 이슈

---

## 재현 환경
- Unity 6000.0.x, WebGL (Wasm2023 + Code Stripping High)
- 아바타 10종 프리팹을 `Instantiate()`로 런타임 생성/교체
- 생성 후 `SetLookTarget(Camera.main.transform)` 호출
- SDK: `com.fluentt.avatar-controller-sample-floating-head@954eeb3b0c52`

## 참고
- 이 문서는 `FirsthabitTalkmotionWebGL` 프로젝트 v0.6.0 기준으로 작성되었습니다.
- `(Clone)` 접미사 불일치 문제는 `@954eeb3b0c52` 버전에서 수정 확인됨
