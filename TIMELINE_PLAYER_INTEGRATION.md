# TalkmotionTimelinePlayer WebGL 통합 - 구현 완료

> **구현일**: 2026-02-25
> **수정 파일**: `unity/Assets/Scripts/FirsthabitWebGLBridge.cs`
> **jslib/Flutter 변경**: 없음 (기존 콜백 시스템 재활용)

---

## 개요

FluentT API에서 직접 받은 **Timeline JSON + WAV 오디오**를 Unity WebGL에 전달하여,
`TalkmotionTimelinePlayer`로 모션을 재생할 수 있습니다.

**주요 용도**: 가속 녹화 — `playAudio: false`로 모션만 빠르게 재생하고 캡처한 뒤,
별도 확보한 오디오와 FFmpeg로 합성.

---

## JS → Unity API (SendMessage)

### 1. `LoadTimeline(json: string)`

Timeline JSON 문자열을 로드합니다.

```javascript
// json: FluentT API에서 받은 TalkmotionTimeline JSON 전체
sendToUnity('LoadTimeline', timelineJsonString);
```

- 성공 시 로그 출력 (콜백 없음)
- 실패 시 `onError("LoadTimeline", message)` 콜백

---

### 2. `LoadTimelineAudio(json: string)`

Timeline에서 사용할 오디오 클립을 로드합니다.
기존 `PrepareAudio`와 동일한 base64 → blob URL → AudioClip 패턴을 사용합니다.

```javascript
// 1. 먼저 JS 측에서 base64 오디오 데이터 설정
window._pendingAudioBase64 = audioBase64Data;

// 2. 로드 요청
sendToUnity('LoadTimelineAudio', JSON.stringify({
    clipId: 'clip-001',   // Timeline JSON의 audio track에 있는 clipId와 일치해야 함
    format: 'wav'          // "wav" | "mp3" | "ogg" | "m4a"
}));
```

**콜백**:
- 성공 → `onPrepared(clipId)` — clipId를 전달하므로 어떤 오디오가 로드 완료되었는지 식별 가능
- 실패 → `onError("LoadTimelineAudio", message)`

---

### 3. `PlayTimeline(json: string)`

로드된 Timeline을 재생합니다.

```javascript
sendToUnity('PlayTimeline', JSON.stringify({
    playAudio: false,           // false: 오디오 음소거 (가속 녹화용), true: 오디오 포함 재생
    cacheId: 'timeline-001'     // 콜백에서 식별자로 사용됨
}));
```

**콜백 순서** (기존 Speak/Chat과 동일한 콜백 재활용):
```
onPlaybackStarted(cacheId)
  → onSentenceStarted(text)
  → onSubtitleStarted(text)
  → onSubtitleEnded(text)
  → onSentenceEnded(text)
  → ... (반복)
onPlaybackCompleted(cacheId)
```

---

## 전체 호출 흐름 예시

```javascript
// === Step 1: Timeline JSON 로드 ===
sendToUnity('LoadTimeline', timelineJson);

// === Step 2: 오디오 로드 (Timeline에 오디오 트랙이 있는 경우) ===
window._pendingAudioBase64 = audioBase64;
sendToUnity('LoadTimelineAudio', JSON.stringify({
    clipId: 'clip-001',
    format: 'wav'
}));
// → onPrepared('clip-001') 콜백 대기

// === Step 3: 재생 ===
sendToUnity('PlayTimeline', JSON.stringify({
    playAudio: false,
    cacheId: 'my-timeline'
}));
// → onPlaybackStarted('my-timeline')
// → ... 모션 재생 ...
// → onPlaybackCompleted('my-timeline')
```

---

## 콜백 참조

Timeline Player는 **기존 콜백을 그대로 재활용**합니다. 새로운 콜백 타입 없음.

| 콜백 | 파라미터 | 발생 시점 |
|------|----------|-----------|
| `onPrepared` | `clipId` | `LoadTimelineAudio` 오디오 로드 완료 |
| `onPlaybackStarted` | `cacheId` | `PlayTimeline` 재생 시작 |
| `onPlaybackCompleted` | `cacheId` | 재생 완료 (자동 음소거 해제 포함) |
| `onSentenceStarted` | `text` | 문장 시작 마커 도달 |
| `onSentenceEnded` | `text` | 문장 종료 마커 도달 |
| `onSubtitleStarted` | `text` | 자막 구간 시작 |
| `onSubtitleEnded` | `text` | 자막 구간 종료 |
| `onError` | `method, message` | 에러 발생 시 |

---

## 내부 구현 상세

### 초기화

- `Start()` 시 `TalkmotionTimelinePlayer` 인스턴스 생성
- `fluentTAvatar.GetComponent<AudioSource>()` + `fluentTAvatar.TMAnimationComponent`로 초기화
- 아바타 교체(`ChangeAvatar`) 시 자동으로 정리 후 재초기화

### Update 루프

`FirsthabitWebGLBridge`의 `Update()`/`LateUpdate()`에서 매 프레임 `timelinePlayer.Update(deltaTime)` / `timelinePlayer.LateUpdate(deltaTime)` 호출.

### 음소거 처리

`playAudio: false`일 때 `AudioSource.mute = true` 설정.
`OnPlaybackCompleted` 이벤트에서 자동으로 `mute = false` 복원.

---

## 주의사항

1. **LoadTimeline → LoadTimelineAudio → PlayTimeline 순서**를 지켜야 합니다.
   - Timeline 로드 없이 Play하면 `"No timeline loaded"` 에러
   - 오디오 없이 Play하면 모션만 재생됨 (오디오 트랙이 비어있으면 정상 동작)

2. **clipId 일치**: `LoadTimelineAudio`의 `clipId`는 Timeline JSON 내부의 audio track clip ID와 일치해야 합니다.

3. **기존 Speak/Chat과 독립**: Timeline Player는 FluentTAvatar의 Speak/Chat 시스템과 별도로 동작합니다. 동시 사용은 권장하지 않습니다 (같은 AudioSource와 TMAnimationComponent를 공유하므로).

4. **WebGL 빌드 필요**: 이 변경사항을 사용하려면 Unity WebGL 재빌드가 필요합니다.
