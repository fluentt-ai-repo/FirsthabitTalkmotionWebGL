# Firsthabit TalkMotion WebGL - 통합 가이드

> **대상 독자**: Flutter Android 앱에서 TalkMotion 아바타를 WebView로 통합하려는 외부 개발자

## 목차

1. [개요](#1-개요)
2. [사전 준비](#2-사전-준비)
3. [Step-by-Step 통합 가이드](#3-step-by-step-통합-가이드)
4. [API Reference](#4-api-reference)
5. [전체 사용 흐름 (Prepare → Play)](#5-전체-사용-흐름-prepare--play)
6. [샘플 코드](#6-샘플-코드)
7. [Troubleshooting](#7-troubleshooting)
8. [참고 정보](#8-참고-정보)

---

## 1. 개요

### TalkMotion이란?

Firsthabit TalkMotion은 오디오 입력을 기반으로 3D 아바타가 입 모양, 표정, 제스처 등의 모션을 자동 생성하여 재생하는 솔루션입니다. Unity WebGL로 빌드되어 WebView 안에서 실행됩니다.

### 아키텍처

```
┌─────────────────────────────────────────────────────┐
│  Flutter Android App                                │
│                                                     │
│  ┌───────────────────────────────────────────────┐  │
│  │  InAppWebView                                 │  │
│  │                                               │  │
│  │  ┌─────────────────────────────────────────┐  │  │
│  │  │  HTML Wrapper                           │  │  │
│  │  │                                         │  │  │
│  │  │  window.FirsthabitBridge (콜백 객체)     │  │  │
│  │  │  window.sendToUnity()   (명령 전달)     │  │  │
│  │  │  window._pendingAudioBase64 (오디오)    │  │  │
│  │  │                                         │  │  │
│  │  │  ┌───────────────────────────────────┐  │  │  │
│  │  │  │  Unity WebGL                      │  │  │  │
│  │  │  │                                   │  │  │  │
│  │  │  │  FirsthabitWebGLBridge (C#)       │  │  │  │
│  │  │  │       ↕                           │  │  │  │
│  │  │  │  FluentT TalkMotion SDK           │  │  │  │
│  │  │  │       ↕                           │  │  │  │
│  │  │  │  FluentT Cloud Server             │  │  │  │
│  │  │  └───────────────────────────────────┘  │  │  │
│  │  └─────────────────────────────────────────┘  │  │
│  └───────────────────────────────────────────────┘  │
└─────────────────────────────────────────────────────┘
```

### 통신 흐름 요약

| 방향 | 경로 | 방식 |
|------|------|------|
| **Flutter → Unity** | Dart → `evaluateJavascript` → `sendToUnity()` → `unityInstance.SendMessage()` | JavaScript 호출 |
| **Unity → Flutter** | C# → jslib → `window.FirsthabitBridge.onXxx()` → `addJavaScriptHandler` → Dart | JavaScript 콜백 |
| **오디오 전달** | Dart → `window._pendingAudioBase64` 세팅 → Unity jslib에서 읽음 | 전역 변수 |

---

## 2. 사전 준비

### 2.1 Unity WebGL 빌드 파일

전달받는 WebGL 빌드 파일 구성:

```
unity_webgl/
├── Build/
│   ├── unity_webgl.loader.js      # Unity 로더 스크립트
│   ├── unity_webgl.framework.js   # Unity 프레임워크
│   ├── unity_webgl.wasm           # WebAssembly 바이너리
│   └── unity_webgl.data           # 에셋 데이터
└── StreamingAssets/                # 스트리밍 에셋 (있는 경우)
```

### 2.2 빌드 파일 호스팅

빌드 파일을 로드하는 방법은 두 가지입니다:

#### 방법 A: 웹 서버 호스팅 (권장)

빌드 파일을 CDN이나 웹 서버에 업로드하고 URL로 접근합니다.

- HTTPS 필수 (WebGL + AudioContext 정책)
- CORS 헤더 설정 필요: `Access-Control-Allow-Origin: *`
- MIME 타입 설정: `.wasm` → `application/wasm`, `.data` → `application/octet-stream`

#### 방법 B: 앱 내 로컬 에셋

Flutter 앱의 `assets/` 폴더에 빌드 파일을 포함합니다.

```yaml
# pubspec.yaml
flutter:
  assets:
    - assets/unity_webgl/
    - assets/unity_webgl/Build/
    - assets/talkmotion.html
```

> **참고**: 로컬 에셋 방식은 앱 크기가 증가합니다 (약 30~50MB). 서버 호스팅을 권장합니다.

### 2.3 Flutter 패키지 설치

```yaml
# pubspec.yaml
dependencies:
  flutter_inappwebview: ^6.0.0
```

Android 매니페스트에 인터넷 권한이 필요합니다:

```xml
<!-- android/app/src/main/AndroidManifest.xml -->
<uses-permission android:name="android.permission.INTERNET" />
```

---

## 3. Step-by-Step 통합 가이드

### Step 1: HTML Wrapper 작성

Unity WebGL을 로드하고 Flutter와 통신하는 HTML 파일이 필요합니다. 아래는 완전한 템플릿입니다.

> **핵심**: 이 HTML은 iframe 없이 WebView에서 직접 실행됩니다. Unity jslib의 브릿지 코드는 `window.parent.FirsthabitBridge` → `window.FirsthabitBridge` 순으로 fallback하므로, 단독 실행에서도 정상 동작합니다.

```html
<!DOCTYPE html>
<html lang="en-us">
<head>
  <meta charset="utf-8">
  <meta name="viewport" content="width=device-width, initial-scale=1.0, user-scalable=no">
  <title>TalkMotion Avatar</title>
  <style>
    html, body {
      margin: 0; padding: 0; width: 100%; height: 100%; overflow: hidden;
      background: transparent;
    }
    #unity-canvas {
      width: 100%; height: 100%; position: fixed; top: 0; left: 0;
      background: transparent;
    }
  </style>
</head>
<body>
  <canvas id="unity-canvas" tabindex="-1"></canvas>

  <script>
    // ============================================================
    // 1. Unity → Flutter 콜백 객체
    //    Unity jslib가 window.FirsthabitBridge.onXxx()를 호출합니다.
    //    각 콜백은 InAppWebView의 JavaScript 핸들러로 전달됩니다.
    // ============================================================
    window.FirsthabitBridge = {
      onBridgeReady: function() {
        window.flutter_inappwebview.callHandler('onBridgeReady');
      },
      onPrepared: function(id) {
        window.flutter_inappwebview.callHandler('onPrepared', id);
      },
      onPrepareFailed: function(id, error) {
        window.flutter_inappwebview.callHandler('onPrepareFailed', id, error);
      },
      onPlaybackStarted: function(id) {
        window.flutter_inappwebview.callHandler('onPlaybackStarted', id);
      },
      onPlaybackCompleted: function(id) {
        window.flutter_inappwebview.callHandler('onPlaybackCompleted', id);
      },
      onSentenceStarted: function(text) {
        window.flutter_inappwebview.callHandler('onSentenceStarted', text);
      },
      onSentenceEnded: function(text) {
        window.flutter_inappwebview.callHandler('onSentenceEnded', text);
      },
      onSubtitleStarted: function(text) {
        window.flutter_inappwebview.callHandler('onSubtitleStarted', text);
      },
      onSubtitleEnded: function(text) {
        window.flutter_inappwebview.callHandler('onSubtitleEnded', text);
      },
      onRequestSent: function(id) {
        window.flutter_inappwebview.callHandler('onRequestSent', id);
      },
      onResponseReceived: function(id) {
        window.flutter_inappwebview.callHandler('onResponseReceived', id);
      },
      onVolumeChanged: function(volume) {
        window.flutter_inappwebview.callHandler('onVolumeChanged', volume);
      },
      onError: function(method, message) {
        window.flutter_inappwebview.callHandler('onError', method, message);
      },
      onCacheInfo: function(json) {
        window.flutter_inappwebview.callHandler('onCacheInfo', json);
      },
      onAvatarChanged: function(json) {
        window.flutter_inappwebview.callHandler('onAvatarChanged', json);
      },
      onAvatarList: function(json) {
        window.flutter_inappwebview.callHandler('onAvatarList', json);
      }
    };

    // ============================================================
    // 2. Flutter → Unity 명령 전달 함수
    // ============================================================
    var unityInstance = null;

    window.sendToUnity = function(method, param) {
      if (unityInstance) {
        unityInstance.SendMessage('FirsthabitWebGLBridge', method, param || '');
      } else {
        console.warn('[TalkMotion] Unity instance not ready');
      }
    };

    // ============================================================
    // 3. 오디오 데이터 임시 저장 변수
    //    Flutter에서 base64 오디오를 이 변수에 세팅한 후 PrepareAudio를 호출합니다.
    //    Unity jslib가 이 변수를 읽어 Blob URL로 변환합니다.
    // ============================================================
    window._pendingAudioBase64 = "";

    // ============================================================
    // 4. Unity WebGL 로더
    //    빌드 파일 경로를 실제 호스팅 경로에 맞게 수정하세요.
    // ============================================================
    var isIOS = /iPad|iPhone|iPod/.test(navigator.userAgent);
  </script>

  <!-- Unity 빌드 파일 경로를 실제 경로로 수정하세요 -->
  <script src="Build/unity_webgl.loader.js"></script>
  <script>
    createUnityInstance(document.querySelector("#unity-canvas"), {
      arguments: [],
      dataUrl:      "Build/unity_webgl.data",
      frameworkUrl:  "Build/unity_webgl.framework.js",
      codeUrl:       "Build/unity_webgl.wasm",
      streamingAssetsUrl: "StreamingAssets",
      companyName:   "FluentT",
      productName:   "FirsthabitTalkmotionWebGL",
      productVersion: "0.2.0",        // 빌드 버전에 맞게 변경
      webglContextAttributes: {
        alpha: true,                   // 투명 배경 지원
        preserveDrawingBuffer: false,
        premultipliedAlpha: isIOS
      },
    }).then(function(instance) {
      unityInstance = instance;
      console.log('[TalkMotion] Unity instance ready');
    }).catch(function(message) {
      console.error('[TalkMotion] Unity load failed:', message);
    });
  </script>
</body>
</html>
```

### Step 2: Flutter에 InAppWebView 추가

#### 2.1 Android 설정

`android/app/src/main/AndroidManifest.xml`의 `<application>` 태그에 다음을 확인합니다:

```xml
<application
    android:usesCleartextTraffic="true"
    ...>
```

`android/app/build.gradle`에서 minSdkVersion을 확인합니다:

```gradle
android {
    defaultConfig {
        minSdkVersion 21  // InAppWebView 최소 요구사항
    }
}
```

#### 2.2 WebView 위젯 배치

```dart
import 'package:flutter_inappwebview/flutter_inappwebview.dart';

class TalkmotionWebView extends StatefulWidget {
  final String htmlUrl; // HTML wrapper URL 또는 로컬 에셋 경로

  const TalkmotionWebView({super.key, required this.htmlUrl});

  @override
  State<TalkmotionWebView> createState() => _TalkmotionWebViewState();
}

class _TalkmotionWebViewState extends State<TalkmotionWebView> {
  InAppWebViewController? _webViewController;

  @override
  Widget build(BuildContext context) {
    return InAppWebView(
      initialUrlRequest: URLRequest(url: WebUri(widget.htmlUrl)),
      initialSettings: InAppWebViewSettings(
        javaScriptEnabled: true,
        mediaPlaybackRequiresUserGesture: false, // 오디오 자동재생 허용
        allowsInlineMediaPlayback: true,
        transparentBackground: true,              // 투명 배경
      ),
      onWebViewCreated: (controller) {
        _webViewController = controller;
        _registerHandlers(controller);
      },
    );
  }

  // ... 핸들러 등록 코드는 Step 3 참조
}
```

#### 2.3 JavaScript 핸들러 등록 (Unity → Flutter)

```dart
void _registerHandlers(InAppWebViewController controller) {
  controller.addJavaScriptHandler(
    handlerName: 'onBridgeReady',
    callback: (args) {
      print('[TalkMotion] Bridge ready');
      // Unity 초기화 완료, 이제 명령을 보낼 수 있습니다.
    },
  );

  controller.addJavaScriptHandler(
    handlerName: 'onPrepared',
    callback: (args) {
      final cacheId = args[0] as String;
      print('[TalkMotion] Prepared: $cacheId');
      // cacheId를 저장하여 Play에서 사용
    },
  );

  controller.addJavaScriptHandler(
    handlerName: 'onPrepareFailed',
    callback: (args) {
      final id = args[0] as String;
      final error = args[1] as String;
      print('[TalkMotion] Prepare failed: $id - $error');
    },
  );

  controller.addJavaScriptHandler(
    handlerName: 'onPlaybackStarted',
    callback: (args) {
      final cacheId = args[0] as String;
      print('[TalkMotion] Playback started: $cacheId');
    },
  );

  controller.addJavaScriptHandler(
    handlerName: 'onPlaybackCompleted',
    callback: (args) {
      final cacheId = args[0] as String;
      print('[TalkMotion] Playback completed: $cacheId');
      // 다음 오디오를 재생하거나 UI 업데이트
    },
  );

  controller.addJavaScriptHandler(
    handlerName: 'onSentenceStarted',
    callback: (args) {
      final text = args[0] as String;
      print('[TalkMotion] Sentence started: $text');
    },
  );

  controller.addJavaScriptHandler(
    handlerName: 'onSentenceEnded',
    callback: (args) {
      final text = args[0] as String;
      print('[TalkMotion] Sentence ended: $text');
    },
  );

  controller.addJavaScriptHandler(
    handlerName: 'onSubtitleStarted',
    callback: (args) {
      final text = args[0] as String;
      // 자막 표시 시작
    },
  );

  controller.addJavaScriptHandler(
    handlerName: 'onSubtitleEnded',
    callback: (args) {
      final text = args[0] as String;
      // 자막 표시 종료
    },
  );

  controller.addJavaScriptHandler(
    handlerName: 'onError',
    callback: (args) {
      final method = args[0] as String;
      final message = args[1] as String;
      print('[TalkMotion] Error in $method: $message');
    },
  );

  controller.addJavaScriptHandler(
    handlerName: 'onVolumeChanged',
    callback: (args) {
      final volume = (args[0] as num).toDouble();
      print('[TalkMotion] Volume: $volume');
    },
  );

  controller.addJavaScriptHandler(
    handlerName: 'onCacheInfo',
    callback: (args) {
      final json = args[0] as String;
      // {"count": 3, "ids": ["id1", "id2", "id3"]}
    },
  );

  controller.addJavaScriptHandler(
    handlerName: 'onAvatarChanged',
    callback: (args) {
      final json = args[0] as String;
      // {"avatarId": "avatar_01", "success": true, "error": ""}
    },
  );

  controller.addJavaScriptHandler(
    handlerName: 'onAvatarList',
    callback: (args) {
      final json = args[0] as String;
      // {"avatarIds": ["avatar_01", ...], "currentAvatarId": "avatar_01"}
    },
  );

  controller.addJavaScriptHandler(
    handlerName: 'onRequestSent',
    callback: (args) {
      final id = args[0] as String;
      // FluentT 서버로 요청이 전송됨 (로딩 표시 등에 활용)
    },
  );

  controller.addJavaScriptHandler(
    handlerName: 'onResponseReceived',
    callback: (args) {
      final id = args[0] as String;
      // FluentT 서버에서 응답 수신됨
    },
  );
}
```

#### 2.4 Flutter → Unity 명령 전달

```dart
/// Unity에 명령을 보내는 헬퍼 메서드
Future<void> _sendToUnity(String method, [String param = '']) async {
  final escaped = param.replaceAll('\\', '\\\\').replaceAll("'", "\\'");
  await _webViewController?.evaluateJavascript(
    source: "sendToUnity('$method', '$escaped');",
  );
}
```

### Step 3: 오디오 전달 흐름

오디오를 Unity에 전달하여 모션을 생성하는 핵심 흐름입니다.

#### 3.1 기본 패턴

```dart
Future<void> playAudioMotion(Uint8List audioBytes, String format) async {
  // 1. 오디오를 base64로 인코딩
  final base64Audio = base64Encode(audioBytes);

  // 2. JavaScript 변수에 base64 데이터 세팅
  await _webViewController?.evaluateJavascript(
    source: "window._pendingAudioBase64 = '$base64Audio';",
  );

  // 3. PrepareAudio 호출 (Unity가 base64를 읽어 모션 생성 요청)
  final json = '{"format":"$format","text":""}';
  await _sendToUnity('PrepareAudio', json);

  // 4. onPrepared 콜백을 기다린 후 Play 호출
  //    (onPrepared 핸들러에서 수신한 cacheId를 사용)
}
```

#### 3.2 자막 텍스트 포함

오디오에 대응하는 자막 텍스트를 함께 전달하면, 감정 기반 제스처가 자동으로 태깅됩니다:

```dart
Future<void> playWithSubtitle(
  Uint8List audioBytes,
  String format,
  String subtitleText,
) async {
  final base64Audio = base64Encode(audioBytes);

  await _webViewController?.evaluateJavascript(
    source: "window._pendingAudioBase64 = '$base64Audio';",
  );

  // text 필드에 자막 텍스트 전달
  final json = '{"format":"$format","text":"${_escapeJson(subtitleText)}"}';
  await _sendToUnity('PrepareAudio', json);
}
```

#### 3.3 Prepare → Play 완전한 예시

```dart
String? _pendingCacheId;

// onPrepared 핸들러에서:
controller.addJavaScriptHandler(
  handlerName: 'onPrepared',
  callback: (args) {
    final cacheId = args[0] as String;
    _pendingCacheId = cacheId;

    // 즉시 재생
    final playJson = '{"cacheId":"$cacheId","playAudio":true}';
    _sendToUnity('Play', playJson);
  },
);
```

---

## 4. API Reference

### 4.1 Flutter → Unity 메서드

`sendToUnity(method, param)` 함수를 통해 Unity에 명령을 전달합니다.
내부적으로 `unityInstance.SendMessage('FirsthabitWebGLBridge', method, param)`가 호출됩니다.

| 메서드 | 파라미터 | 설명 | 관련 콜백 |
|--------|---------|------|----------|
| `PrepareAudio` | JSON: `{"format":"wav","text":""}` | 오디오 모션 준비. `_pendingAudioBase64`에 base64 데이터를 먼저 세팅해야 합니다 | `onPrepared` / `onPrepareFailed` |
| `Play` | JSON: `{"cacheId":"xxx","playAudio":true}` | 준비된 캐시 항목 재생 | `onPlaybackStarted` → `onPlaybackCompleted` |
| `Stop` | *(없음)* | 현재 재생 즉시 중지 | - |
| `SetVolume` | `"0.0"` ~ `"1.0"` (문자열) | 음량 설정 | `onVolumeChanged` |
| `GetCacheInfo` | *(무시됨)* | 현재 캐시 정보 조회 | `onCacheInfo` |
| `ClearAllCache` | *(무시됨)* | 모든 캐시 초기화 | `onCacheInfo` |
| `ChangeAvatar` | `"avatar_01"` 등 아바타 ID (문자열) | 아바타 런타임 교체 | `onAvatarChanged` |
| `GetAvatarList` | *(무시됨)* | 사용 가능한 아바타 목록 조회 | `onAvatarList` |
| `SetBackgroundColor` | `"transparent"` 또는 `"#RRGGBB"` | 배경색 설정 | - |

#### PrepareAudio JSON 스키마

```json
{
  "format": "wav",   // "wav" | "mp3" | "ogg" | "m4a"
  "text": ""         // 자막/감정 태깅용 텍스트 (선택사항, 기본값 "")
}
```

#### Play JSON 스키마

```json
{
  "cacheId": "xxx",       // onPrepared에서 수신한 캐시 ID
  "playAudio": true       // true: 오디오 + 모션, false: 모션만
}
```

### 4.2 Unity → Flutter 콜백

Unity에서 발생하는 이벤트는 `window.FirsthabitBridge.onXxx()`를 통해 Flutter로 전달됩니다.

#### 초기화

| 콜백 | 파라미터 | 설명 |
|------|---------|------|
| `onBridgeReady` | *(없음)* | Unity 브릿지 초기화 완료. 이 콜백 수신 후에 명령을 보내야 합니다. |

#### Prepare/Play 흐름

| 콜백 | 파라미터 | 설명 |
|------|---------|------|
| `onRequestSent` | `id` (string) | FluentT 서버로 TalkMotion 요청 전송됨 |
| `onResponseReceived` | `id` (string) | FluentT 서버에서 TalkMotion 응답 수신됨 |
| `onPrepared` | `cacheId` (string) | 모션 준비 완료. 이 `cacheId`로 `Play`를 호출합니다 |
| `onPrepareFailed` | `id` (string), `error` (string) | 모션 준비 실패 |
| `onPlaybackStarted` | `cacheId` (string) | 재생 시작됨 |
| `onPlaybackCompleted` | `cacheId` (string) | 재생 완료됨 |

#### 문장/자막

| 콜백 | 파라미터 | 설명 |
|------|---------|------|
| `onSentenceStarted` | `text` (string) | 문장 구간 시작 |
| `onSentenceEnded` | `text` (string) | 문장 구간 종료 |
| `onSubtitleStarted` | `text` (string) | 자막 텍스트 표시 시작 |
| `onSubtitleEnded` | `text` (string) | 자막 텍스트 표시 종료 |

#### 상태 관리

| 콜백 | 파라미터 | 설명 |
|------|---------|------|
| `onVolumeChanged` | `volume` (number) | 음량 변경됨 (0.0 ~ 1.0) |
| `onCacheInfo` | `json` (string) | 캐시 정보 JSON |
| `onAvatarChanged` | `json` (string) | 아바타 교체 결과 JSON |
| `onAvatarList` | `json` (string) | 아바타 목록 JSON |
| `onError` | `method` (string), `message` (string) | 에러 발생 |

#### 콜백 JSON 스키마

**onCacheInfo:**
```json
{
  "count": 3,
  "ids": ["cacheId_1", "cacheId_2", "cacheId_3"]
}
```

**onAvatarChanged:**
```json
{
  "avatarId": "avatar_01",
  "success": true,
  "error": ""                // 실패 시 에러 메시지
}
```

**onAvatarList:**
```json
{
  "avatarIds": ["avatar_01", "avatar_02", "avatar_03"],
  "currentAvatarId": "avatar_01"
}
```

---

## 5. 전체 사용 흐름 (Prepare → Play)

### 5.1 시퀀스 다이어그램

```
Flutter App          HTML/JS              Unity (C#)           FluentT Server
    │                   │                     │                      │
    │  ── WebView 로드 ──>                    │                      │
    │                   │  ── Unity 로드 ──────>                     │
    │                   │                     │                      │
    │                   │  <── onBridgeReady ──│                     │
    │  <── callHandler ──│                    │                      │
    │                   │                     │                      │
    │ [1] base64 세팅   │                     │                      │
    │ ─── evaluateJS ──>│                     │                      │
    │  (_pendingAudio)  │                     │                      │
    │                   │                     │                      │
    │ [2] PrepareAudio  │                     │                      │
    │ ─── evaluateJS ──>│ ── SendMessage ────>│                      │
    │                   │                     │ ── blob URL 생성      │
    │                   │                     │ ── AudioClip 로드     │
    │                   │                     │                      │
    │                   │                     │ ── 모션 요청 ────────>│
    │                   │  <── onRequestSent ──│                     │
    │  <── callHandler ──│                    │                      │
    │                   │                     │  <── 모션 데이터 ──── │
    │                   │  <─ onResponseRecv ──│                     │
    │  <── callHandler ──│                    │                      │
    │                   │                     │ ── 캐시 저장           │
    │                   │  <── onPrepared ─────│                     │
    │  <── callHandler ──│                    │                      │
    │                   │                     │                      │
    │ [3] Play          │                     │                      │
    │ ─── evaluateJS ──>│ ── SendMessage ────>│                      │
    │                   │  <─ onPlaybackStart ─│                     │
    │  <── callHandler ──│                    │                      │
    │                   │                     │ ── 모션 + 오디오 재생  │
    │                   │                     │                      │
    │                   │  <── onSubtitle* ────│                     │
    │  <── callHandler ──│                    │                      │
    │                   │                     │                      │
    │                   │  <── onSentence* ────│                     │
    │  <── callHandler ──│                    │                      │
    │                   │                     │                      │
    │                   │  <─ onPlaybackEnd ───│                     │
    │  <── callHandler ──│                    │                      │
    │                   │                     │                      │
```

### 5.2 에러 처리 패턴

```dart
// onPrepareFailed: 모션 준비 실패
controller.addJavaScriptHandler(
  handlerName: 'onPrepareFailed',
  callback: (args) {
    final id = args[0] as String;
    final error = args[1] as String;

    if (error.contains('No audio data')) {
      // base64 데이터가 세팅되지 않았음 → _pendingAudioBase64 확인
    } else if (error.contains('AudioClip')) {
      // 오디오 디코딩 실패 → 포맷 확인
    } else {
      // FluentT 서버 에러 → 네트워크/API 키 확인
    }
  },
);

// onError: 범용 에러 핸들러
controller.addJavaScriptHandler(
  handlerName: 'onError',
  callback: (args) {
    final method = args[0] as String;  // 에러가 발생한 메서드명
    final message = args[1] as String; // 에러 메시지
    print('[TalkMotion] Error in $method: $message');
  },
);
```

---

## 6. 샘플 코드

### 6.1 완전한 HTML Wrapper 템플릿

[Step 1](#step-1-html-wrapper-작성)의 HTML 코드를 그대로 사용하세요. 수정이 필요한 부분:

1. **빌드 파일 경로**: `Build/unity_webgl.loader.js` 등의 경로를 실제 호스팅 경로에 맞게 수정
2. **productVersion**: Unity 빌드 버전과 일치시킴 (캐시 무효화에 사용됨)

### 6.2 Flutter Dart 전체 샘플

```dart
import 'dart:convert';
import 'dart:typed_data';

import 'package:flutter/material.dart';
import 'package:flutter_inappwebview/flutter_inappwebview.dart';

/// TalkMotion 아바타 WebView 위젯
///
/// Unity WebGL로 구현된 TalkMotion 아바타를 InAppWebView로 표시하고,
/// 오디오 기반 모션 재생을 제어합니다.
class TalkmotionWebView extends StatefulWidget {
  /// HTML wrapper 파일의 URL
  /// 예: "https://your-server.com/talkmotion.html"
  final String htmlUrl;

  /// Unity 브릿지 초기화 완료 시 호출
  final VoidCallback? onBridgeReady;

  /// 모션 준비 완료 시 호출 (cacheId 전달)
  final void Function(String cacheId)? onPrepared;

  /// 재생 완료 시 호출
  final void Function(String cacheId)? onPlaybackCompleted;

  /// 자막 시작 시 호출
  final void Function(String text)? onSubtitleStarted;

  /// 자막 종료 시 호출
  final void Function(String text)? onSubtitleEnded;

  /// 에러 발생 시 호출
  final void Function(String method, String message)? onError;

  const TalkmotionWebView({
    super.key,
    required this.htmlUrl,
    this.onBridgeReady,
    this.onPrepared,
    this.onPlaybackCompleted,
    this.onSubtitleStarted,
    this.onSubtitleEnded,
    this.onError,
  });

  @override
  State<TalkmotionWebView> createState() => TalkmotionWebViewState();
}

class TalkmotionWebViewState extends State<TalkmotionWebView> {
  InAppWebViewController? _controller;
  bool _isReady = false;

  /// Unity 브릿지가 초기화되었는지 여부
  bool get isReady => _isReady;

  @override
  Widget build(BuildContext context) {
    return InAppWebView(
      initialUrlRequest: URLRequest(url: WebUri(widget.htmlUrl)),
      initialSettings: InAppWebViewSettings(
        javaScriptEnabled: true,
        mediaPlaybackRequiresUserGesture: false,
        allowsInlineMediaPlayback: true,
        transparentBackground: true,
      ),
      onWebViewCreated: (controller) {
        _controller = controller;
        _registerHandlers(controller);
      },
    );
  }

  // ─────────────────────────────────────────────
  // Public API: Flutter → Unity
  // ─────────────────────────────────────────────

  /// 오디오 모션을 준비합니다.
  ///
  /// [audioBytes] - 오디오 파일의 바이트 데이터
  /// [format] - 오디오 포맷: "wav", "mp3", "ogg", "m4a"
  /// [text] - 자막/감정 태깅용 텍스트 (선택사항)
  ///
  /// 준비 완료 시 [onPrepared] 콜백이 호출됩니다.
  Future<void> prepareAudio(
    Uint8List audioBytes,
    String format, {
    String text = '',
  }) async {
    if (!_isReady) return;

    final base64Audio = base64Encode(audioBytes);

    // 1. base64 오디오 데이터를 JS 변수에 세팅
    await _controller?.evaluateJavascript(
      source: "window._pendingAudioBase64 = '$base64Audio';",
    );

    // 2. PrepareAudio 호출
    final json = '{"format":"${_escapeJson(format)}","text":"${_escapeJson(text)}"}';
    await _sendToUnity('PrepareAudio', json);
  }

  /// 준비된 캐시 항목을 재생합니다.
  ///
  /// [cacheId] - onPrepared에서 수신한 캐시 ID
  /// [playAudio] - true면 오디오+모션, false면 모션만 재생
  Future<void> play(String cacheId, {bool playAudio = true}) async {
    final json = '{"cacheId":"${_escapeJson(cacheId)}","playAudio":$playAudio}';
    await _sendToUnity('Play', json);
  }

  /// 현재 재생을 중지합니다.
  Future<void> stop() async {
    await _sendToUnity('Stop');
  }

  /// 음량을 설정합니다. (0.0 ~ 1.0)
  Future<void> setVolume(double volume) async {
    await _sendToUnity('SetVolume', volume.toStringAsFixed(2));
  }

  /// 아바타를 교체합니다.
  Future<void> changeAvatar(String avatarId) async {
    await _sendToUnity('ChangeAvatar', avatarId);
  }

  /// 사용 가능한 아바타 목록을 조회합니다.
  Future<void> getAvatarList() async {
    await _sendToUnity('GetAvatarList');
  }

  /// 배경색을 설정합니다.
  /// "transparent" 또는 "#RRGGBB" 형식
  Future<void> setBackgroundColor(String color) async {
    await _sendToUnity('SetBackgroundColor', color);
  }

  /// 캐시 정보를 조회합니다.
  Future<void> getCacheInfo() async {
    await _sendToUnity('GetCacheInfo');
  }

  /// 모든 캐시를 초기화합니다.
  Future<void> clearAllCache() async {
    await _sendToUnity('ClearAllCache');
  }

  // ─────────────────────────────────────────────
  // Internal
  // ─────────────────────────────────────────────

  void _registerHandlers(InAppWebViewController controller) {
    controller.addJavaScriptHandler(
      handlerName: 'onBridgeReady',
      callback: (args) {
        _isReady = true;
        widget.onBridgeReady?.call();
      },
    );

    controller.addJavaScriptHandler(
      handlerName: 'onPrepared',
      callback: (args) {
        widget.onPrepared?.call(args[0] as String);
      },
    );

    controller.addJavaScriptHandler(
      handlerName: 'onPrepareFailed',
      callback: (args) {
        widget.onError?.call('PrepareAudio', args[1] as String);
      },
    );

    controller.addJavaScriptHandler(
      handlerName: 'onPlaybackStarted',
      callback: (args) {
        // 필요 시 콜백 추가
      },
    );

    controller.addJavaScriptHandler(
      handlerName: 'onPlaybackCompleted',
      callback: (args) {
        widget.onPlaybackCompleted?.call(args[0] as String);
      },
    );

    controller.addJavaScriptHandler(
      handlerName: 'onSentenceStarted',
      callback: (args) {},
    );

    controller.addJavaScriptHandler(
      handlerName: 'onSentenceEnded',
      callback: (args) {},
    );

    controller.addJavaScriptHandler(
      handlerName: 'onSubtitleStarted',
      callback: (args) {
        widget.onSubtitleStarted?.call(args[0] as String);
      },
    );

    controller.addJavaScriptHandler(
      handlerName: 'onSubtitleEnded',
      callback: (args) {
        widget.onSubtitleEnded?.call(args[0] as String);
      },
    );

    controller.addJavaScriptHandler(
      handlerName: 'onRequestSent',
      callback: (args) {},
    );

    controller.addJavaScriptHandler(
      handlerName: 'onResponseReceived',
      callback: (args) {},
    );

    controller.addJavaScriptHandler(
      handlerName: 'onVolumeChanged',
      callback: (args) {},
    );

    controller.addJavaScriptHandler(
      handlerName: 'onError',
      callback: (args) {
        widget.onError?.call(args[0] as String, args[1] as String);
      },
    );

    controller.addJavaScriptHandler(
      handlerName: 'onCacheInfo',
      callback: (args) {},
    );

    controller.addJavaScriptHandler(
      handlerName: 'onAvatarChanged',
      callback: (args) {},
    );

    controller.addJavaScriptHandler(
      handlerName: 'onAvatarList',
      callback: (args) {},
    );
  }

  Future<void> _sendToUnity(String method, [String param = '']) async {
    final escaped = param
        .replaceAll('\\', '\\\\')
        .replaceAll("'", "\\'")
        .replaceAll('\n', '\\n');
    await _controller?.evaluateJavascript(
      source: "sendToUnity('$method', '$escaped');",
    );
  }

  String _escapeJson(String value) {
    return value
        .replaceAll('\\', '\\\\')
        .replaceAll('"', '\\"')
        .replaceAll('\n', '\\n')
        .replaceAll('\r', '\\r')
        .replaceAll('\t', '\\t');
  }
}
```

### 6.3 위젯 사용 예시

```dart
class AvatarScreen extends StatelessWidget {
  const AvatarScreen({super.key});

  @override
  Widget build(BuildContext context) {
    final avatarKey = GlobalKey<TalkmotionWebViewState>();

    return Scaffold(
      body: Stack(
        children: [
          // 앱 메인 UI
          const Center(child: Text('메인 화면')),

          // 아바타 (화면 우하단에 배치)
          Positioned(
            right: 0,
            bottom: 0,
            width: 200,
            height: 300,
            child: TalkmotionWebView(
              key: avatarKey,
              htmlUrl: 'https://your-server.com/talkmotion.html',
              onBridgeReady: () {
                print('아바타 준비 완료!');
                // 초기 설정
                avatarKey.currentState?.setBackgroundColor('transparent');
              },
              onPrepared: (cacheId) {
                // 준비 완료 → 즉시 재생
                avatarKey.currentState?.play(cacheId);
              },
              onPlaybackCompleted: (cacheId) {
                print('재생 완료: $cacheId');
              },
              onSubtitleStarted: (text) {
                // 자막 표시
              },
              onError: (method, message) {
                print('에러: $method - $message');
              },
            ),
          ),
        ],
      ),
      floatingActionButton: FloatingActionButton(
        onPressed: () async {
          // TTS 등에서 받은 오디오 데이터
          final audioBytes = await getAudioFromTTS("안녕하세요");
          avatarKey.currentState?.prepareAudio(
            audioBytes,
            'wav',
            text: '안녕하세요',
          );
        },
        child: const Icon(Icons.play_arrow),
      ),
    );
  }

  Future<Uint8List> getAudioFromTTS(String text) async {
    // TTS API를 호출하여 오디오 바이트를 반환
    // 이 부분은 실제 TTS 서비스에 맞게 구현하세요
    throw UnimplementedError();
  }
}
```

---

## 7. Troubleshooting

### 7.1 WASM 캐시 문제

**증상**: Unity 빌드 업데이트 후에도 이전 버전이 로드되거나 크래시 발생

**원인**: Unity 로더가 브라우저 Cache API + IndexedDB에 빌드 파일을 캐싱합니다. `productVersion`이 동일하면 구 파일과 신 파일이 섞여 WASM 오류가 발생합니다.

**해결**:
- HTML의 `productVersion` 값을 빌드마다 변경
- WebView 캐시를 초기화: `InAppWebViewController.clearAllCache()`

### 7.2 CORS 이슈

**증상**: Unity 빌드 파일 로드 실패, 콘솔에 CORS 에러

**해결**: 서버에서 다음 헤더를 설정합니다:
```
Access-Control-Allow-Origin: *
Content-Type: application/wasm  (*.wasm 파일)
Content-Type: application/octet-stream  (*.data 파일)
```

### 7.3 오디오 관련

**증상**: `onPrepareFailed` 콜백에 "No audio data pending" 에러

**원인**: `_pendingAudioBase64`가 세팅되지 않은 상태에서 `PrepareAudio`를 호출

**해결**: `evaluateJavascript`로 base64 데이터를 먼저 세팅한 후 `PrepareAudio`를 호출합니다. 두 호출 사이에 await를 사용하여 순서를 보장하세요.

**증상**: "AudioClip decode failed" 에러

**원인**: 지원하지 않는 오디오 포맷이거나 데이터가 손상됨

**해결**: format 파라미터(`"wav"`, `"mp3"`, `"ogg"`, `"m4a"`)가 실제 오디오 형식과 일치하는지 확인합니다. WAV 포맷이 가장 안정적입니다.

### 7.4 WebView 설정

**증상**: 오디오가 재생되지 않음

**해결**: InAppWebViewSettings에서 다음을 확인합니다:
```dart
InAppWebViewSettings(
  javaScriptEnabled: true,
  mediaPlaybackRequiresUserGesture: false, // 필수!
  allowsInlineMediaPlayback: true,
)
```

**증상**: 배경이 투명하지 않음

**해결**:
1. `InAppWebViewSettings`에 `transparentBackground: true`
2. Unity 측에서 `SetBackgroundColor("transparent")` 호출
3. HTML의 `webglContextAttributes`에 `alpha: true` 확인

### 7.5 대용량 오디오 전달 시 문제

**증상**: 긴 오디오 파일 전달 시 WebView가 느려지거나 크래시

**원인**: base64 인코딩된 대용량 문자열을 `evaluateJavascript`로 전달하면 메모리 부족 발생 가능

**해결**: 오디오를 적절한 크기로 분할하거나, 서버에 오디오를 업로드 후 URL로 전달하는 방식을 고려합니다. 일반적으로 60초 미만의 TTS 오디오는 문제없이 전달됩니다.

---

## 8. 참고 정보

### 8.1 지원 오디오 포맷

| 포맷 | format 값 | MIME Type | 안정성 |
|------|----------|-----------|--------|
| WAV | `"wav"` | `audio/wav` | 높음 (권장) |
| MP3 | `"mp3"` | `audio/mpeg` | 높음 |
| OGG | `"ogg"` | `audio/ogg` | 보통 |
| M4A/AAC | `"m4a"` | `audio/mp4` | 보통 |

> WAV 포맷을 권장합니다. 브라우저 호환성이 가장 높고 디코딩 실패가 적습니다.

### 8.2 아바타 목록

사용 가능한 아바타 ID는 `GetAvatarList` 메서드로 조회할 수 있습니다. 응답 JSON에 `avatarIds` 배열과 `currentAvatarId`가 포함됩니다.

### 8.3 주요 제한사항

- **WebGL 성능**: 네이티브 빌드 대비 성능이 제한적입니다 (15~30 FPS). 프로덕션 환경에서는 네이티브 Android/iOS 빌드를 권장합니다.
- **메모리**: WebGL 빌드는 약 230~400MB 메모리를 사용합니다. 저사양 디바이스에서는 주의가 필요합니다.
- **네트워크**: 모션 생성을 위해 FluentT 서버와의 통신이 필요합니다. 오프라인에서는 Prepare가 실패합니다.
- **첫 로딩**: WASM 파일 다운로드 + 컴파일로 첫 로딩에 3~8초 소요됩니다. 로딩 인디케이터 표시를 권장합니다.
