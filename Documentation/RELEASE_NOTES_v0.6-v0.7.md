# Firsthabit TalkMotion WebGL — 업데이트 안내 (v0.6.0 → v0.7.0)

> **대상**: 외부 협력사 개발팀
> **기간**: 2026-04-07 ~ 2026-04-08
> **현재 버전**: v0.7.0

---

## 1. 아바타 이름 변경

아바타 ID가 전면 변경되었습니다. 기존 ID는 더 이상 사용할 수 없습니다.

| 이전 ID | 새 ID |
|---------|-------|
| sangjun | **ethan** |
| seokhee | **leo** |
| taerin | **marie** |
| new01 | **max** |
| new02 | **sophia** |
| new03 | **ella** |
| new04 | **nora** |
| new05 | **kai** |
| new06 | **lina** |
| new07 | **owen** |

`ChangeAvatar` 호출 시 새 ID를 사용해주세요.

---

## 2. One-Shot 모션 기능 추가

아바타에 사전 등록된 모션을 외부에서 트리거할 수 있습니다. 재생 중 TalkMotion(음성 재생)이 시작되면 자동으로 중단됩니다.

### 사용 가능한 모션 그룹
- `Listening` — 듣기 모션 (랜덤 가중치 루프)
- `Listening2` — 듣기 모션 단순 버전
- `Thinking` — 생각 모션 (랜덤 가중치 루프)

### API

**Flutter → Unity**

| 메서드 | 파라미터 | 설명 |
|--------|---------|------|
| `PlayOneShotMotion` | `{"groupId":"Listening"}` | 모션 그룹 루프 재생 |
| `PlayOneShotMotion` | `{"motionId":"wave"}` | 단일 모션 1회 재생 |
| `StopOneShotMotion` | (없음) | 현재 모션 중지 |
| `GetOneShotMotionList` | (없음) | 등록된 모션/그룹 목록 조회 |

**Unity → Flutter 콜백**

| 콜백 | 데이터 | 설명 |
|------|-------|------|
| `onOneShotMotionStarted` | `motionId` | 모션 재생 시작 |
| `onOneShotMotionEnded` | `motionId` | 모션 재생 종료 |
| `onOneShotMotionList` | `{"motionIds":[...], "groupIds":[...]}` | 목록 응답 |

### 사용 예시 (Dart)
```dart
// 듣기 모션 시작
bridge.playOneShotMotionGroup('Listening');

// 중지
bridge.stopOneShotMotion();

// 콜백 수신
bridge.onOneShotMotionStarted.listen((id) => print('Started: $id'));
bridge.onOneShotMotionEnded.listen((id) => print('Ended: $id'));
```

---

## 3. 클로스 시뮬레이션 WebGL 지원

이전 버전에서는 MagicaCloth2를 사용했으나 WebGL에서 동작하지 않았습니다 (멀티스레딩 미지원). **Dynamic Bone**으로 교체하여 WebGL에서도 머리카락/옷 물리가 정상 동작합니다.

- 별도 설정 불필요 (프리팹에 적용 완료)
- 네이티브(Android/iOS) 빌드에서도 동일하게 동작

---

## 4. 에러 처리 개선

아바타를 소환하지 않은 상태에서 API를 호출하면 명확한 에러 메시지가 반환됩니다.

**이전**: 내부 예외 메시지 (예: `Delegate to an instance method cannot have null 'this'.`)
**현재**: `"No avatar loaded. Call ChangeAvatar first."`

모든 아바타 의존 메서드에 적용됩니다:
PrepareAudio, Play, Stop, SetVolume, GetCacheInfo, ClearAllCache, Chat, Speak, PlayOneShotMotion, StopOneShotMotion, GetOneShotMotionList

---

## 5. WebGL 빌드 최적화

Wasm2023 + Code Stripping 적용으로 빌드 용량이 감소했습니다.

| 항목 | 이전 | 현재 |
|------|------|------|
| `.wasm` (코드) | 54 MB | 41 MB |
| `.data` (에셋) | 37 MB | 35 MB |
| **합계** | **91 MB** | **76 MB** |

---

## 6. HTML Wrapper 변경사항

Unity WebGL을 임베딩하는 HTML에 다음 사항이 추가되었습니다. 자체 HTML Wrapper를 사용하는 경우 반영해주세요.

### sendToUnity 메시지 큐
Unity 인스턴스 준비 전에 호출된 명령을 큐에 저장하고, 준비 완료 후 자동 전송합니다.

```javascript
var pendingMessages = [];

window.sendToUnity = function(gameObject, method, param) {
  if (unityInstance) {
    unityInstance.SendMessage(gameObject, method, param || '');
  } else {
    pendingMessages.push({gameObject: gameObject, method: method, param: param || ''});
  }
};

// unityInstance 연결 후 flush
while (pendingMessages.length > 0) {
  var msg = pendingMessages.shift();
  unityInstance.SendMessage(msg.gameObject, msg.method, msg.param);
}
```

### 추가할 콜백 (window.FirsthabitBridge)
```javascript
onOneShotMotionStarted: function(motionId) {
  window.flutter_inappwebview.callHandler('onOneShotMotionStarted', motionId);
},
onOneShotMotionEnded: function(motionId) {
  window.flutter_inappwebview.callHandler('onOneShotMotionEnded', motionId);
},
onOneShotMotionList: function(json) {
  window.flutter_inappwebview.callHandler('onOneShotMotionList', json);
}
```

---

## 전체 API 요약 (v0.7.0 기준)

### Flutter → Unity (15개 메서드)

| 카테고리 | 메서드 | 설명 |
|----------|--------|------|
| 오디오 | `PrepareAudio` | 오디오 + 텍스트로 모션 준비 |
| | `Play` | 캐시 재생 |
| | `Stop` | 재생 중지 |
| | `SetVolume` | 음량 조절 |
| 캐시 | `GetCacheInfo` | 캐시 정보 조회 |
| | `ClearAllCache` | 전체 캐시 삭제 |
| 아바타 | `ChangeAvatar` | 아바타 교체 |
| | `GetAvatarList` | 아바타 목록 조회 |
| 대화 | `Chat` | LLM → TTS → 모션 |
| | `Speak` | TTS → 모션 |
| 배경 | `SetBackgroundColor` | 배경색/투명 설정 |
| 모션 | `PlayOneShotMotion` | 모션 그룹/단일 재생 |
| | `StopOneShotMotion` | 모션 중지 |
| | `GetOneShotMotionList` | 모션 목록 조회 |

### Unity → Flutter (19개 콜백)

| 카테고리 | 콜백 | 데이터 |
|----------|------|-------|
| 초기화 | `onBridgeReady` | — |
| 캐시 | `onPrepared` | cacheId |
| | `onPrepareFailed` | cacheId, error |
| 재생 | `onPlaybackStarted` | cacheId |
| | `onPlaybackCompleted` | cacheId |
| 문장 | `onSentenceStarted` | text |
| | `onSentenceEnded` | text |
| 자막 | `onSubtitleStarted` | text |
| | `onSubtitleEnded` | text |
| 서버 | `onRequestSent` | id |
| | `onResponseReceived` | id |
| 상태 | `onVolumeChanged` | volume |
| | `onError` | method, message |
| | `onCacheInfo` | JSON |
| 아바타 | `onAvatarChanged` | JSON |
| | `onAvatarList` | JSON |
| 모션 | `onOneShotMotionStarted` | motionId |
| | `onOneShotMotionEnded` | motionId |
| | `onOneShotMotionList` | JSON |
