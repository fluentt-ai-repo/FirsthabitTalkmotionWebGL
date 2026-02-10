# Firsthabit TalkMotion - 플랫폼별 전달 계획

## 전달 순서

```
Phase 1: WebGL 데모      →  Flutter 웹앱으로 브릿지 검증 및 API 확정
Phase 2: Android 네이티브  →  프로덕션 품질 Android 빌드 전달
Phase 3: iOS 네이티브      →  프로덕션 품질 iOS 빌드 전달
```

---

## Phase 1: WebGL 데모 (현재)

### 목적
- Flutter 개발자에게 **브릿지 API 설계 검증용 데모** 제공
- 콜백 구조, 메서드 시그니처, 플로우를 웹에서 먼저 확인
- 모바일 개발 전 API 스펙 확정

### 전달물
```
flutter_test_app/
├── web/
│   ├── index.html                  # Flutter 웹 컨테이너 + JS 브릿지
│   ├── unity_wrapper.html          # Unity WebGL iframe 래퍼
│   └── unity_webgl/
│       └── Build/                  # Unity WebGL 빌드 파일
│           ├── unity_webgl.loader.js
│           ├── unity_webgl.framework.js
│           ├── unity_webgl.wasm
│           └── unity_webgl.data
├── lib/
│   ├── main.dart                   # 테스트 UI
│   └── unity_bridge.dart           # 브릿지 API (웹 버전)
└── pubspec.yaml
```

### 실행 방법
```bash
cd flutter_test_app
flutter pub get
flutter run -d chrome
```

### 브릿지 API (공통 스펙)

#### Flutter → Unity 메서드
| 메서드 | 파라미터 | 설명 |
|--------|---------|------|
| `prepareAudio` | `base64Audio`, `format` | 오디오 파일을 Unity에 전달하여 모션 준비 |
| `play` | `cacheId`, `playAudio` | 준비된 캐시 재생 |
| `stop` | - | 현재 재생 중지 |
| `setVolume` | `volume` (0.0~1.0) | 음량 조절 |
| `clearAllCache` | - | 모든 캐시 제거 |
| `changeAvatar` | `key` | 아바타 변경 (예정) |

#### Unity → Flutter 콜백
| 콜백 | 데이터 | 설명 |
|------|-------|------|
| `onBridgeReady` | - | Unity 브릿지 초기화 완료 |
| `onPrepared` | `cacheId` | 모션 준비 완료 |
| `onPrepareFailed` | `cacheId`, `error` | 준비 실패 |
| `onPlaybackStarted` | `cacheId` | 재생 시작 |
| `onPlaybackCompleted` | `cacheId` | 재생 완료 |
| `onSentenceStarted` | `text` | 문장 구간 시작 |
| `onSentenceEnded` | `text` | 문장 구간 종료 |
| `onSubtitleStarted` | `text` | 자막 구간 시작 |
| `onSubtitleEnded` | `text` | 자막 구간 종료 |
| `onRequestSent` | `id` | TalkMotion 서버 요청 전송 |
| `onResponseReceived` | `id` | TalkMotion 서버 응답 수신 |
| `onVolumeChanged` | `volume` | 음량 변경됨 |
| `onError` | `method`, `message` | 에러 발생 |

### 제한사항
- 웹 전용 (모바일 브라우저에서는 성능 저하)
- API 검증 및 플로우 확인 목적
- 프로덕션 사용 불가

---

## Phase 2: Android 네이티브

### 목적
- 프로덕션 품질의 Android 빌드 제공
- 네이티브 GPU 렌더링 (Vulkan/OpenGL ES)
- IL2CPP 네이티브 컴파일로 풀 성능

### Unity 빌드 설정
- **빌드 타겟**: Android (ARM64)
- **Scripting Backend**: IL2CPP
- **렌더링**: URP + Vulkan (fallback OpenGL ES 3.0)
- **Burst Compiler**: 활성화 (MagicaCloth2 성능)

### 전달물
```
android_unity_library/
├── unityLibrary.aar          # Unity Android 라이브러리
└── integration_guide.md      # 통합 가이드
```

또는 소스 형태:
```
android/unityLibrary/         # Unity Export된 Android 프로젝트
├── src/main/
│   ├── java/                 # Unity 네이티브 코드
│   └── jniLibs/              # .so 네이티브 라이브러리
├── build.gradle
└── libs/
    └── unity-classes.jar
```

### Flutter 통합 방식

#### 1. gradle 의존성 추가
```gradle
// android/settings.gradle
include ':unityLibrary'

// android/app/build.gradle
dependencies {
    implementation project(':unityLibrary')
}
```

#### 2. MethodChannel 브릿지 (Kotlin)
```kotlin
// Flutter ↔ Unity 통신
class TalkmotionPlugin : FlutterPlugin, MethodCallHandler {
    override fun onMethodCall(call: MethodCall, result: Result) {
        when (call.method) {
            "prepareAudio" -> {
                UnityPlayer.UnitySendMessage("FirsthabitBridge", "PrepareAudio", json)
            }
            "play" -> { ... }
            "stop" -> { ... }
        }
    }

    // Unity → Flutter 콜백
    companion object {
        fun onPrepared(id: String) {
            channel.invokeMethod("onPrepared", id)
        }
    }
}
```

#### 3. Dart 브릿지 (MethodChannel 버전)
```dart
class UnityBridgeMobile {
    static const _channel = MethodChannel('com.firsthabit.talkmotion');

    UnityBridgeMobile() {
        _channel.setMethodCallHandler((call) async {
            switch (call.method) {
                case 'onPrepared':
                    _onPrepared.add(call.arguments as String);
                // ... 동일한 콜백 구조
            }
        });
    }

    Future<void> prepareAudio(String base64, String format) {
        return _channel.invokeMethod('prepareAudio', {
            'base64': base64, 'format': format,
        });
    }
}
```

### WebGL → Android 변경점
| 항목 | WebGL | Android |
|------|-------|---------|
| Unity 빌드 | `.wasm` + `.js` | `unityLibrary.aar` |
| 통신 | JS interop + iframe | MethodChannel + UnitySendMessage |
| Dart 패키지 | `package:web` | `package:flutter/services.dart` |
| 콜백 전달 | jslib → window.parent | AndroidJavaClass → MethodChannel |
| 렌더링 | WebGL 2.0 | Vulkan / OpenGL ES |

### API 호환성
**Flutter 개발자 관점에서 API는 동일합니다.**

```dart
// WebGL 버전
final bridge = UnityBridge();        // JS interop 기반

// Android 버전
final bridge = UnityBridgeMobile();  // MethodChannel 기반

// 둘 다 동일한 인터페이스:
bridge.prepareAudio(base64, format);
bridge.play(cacheId);
bridge.onPrepared.listen((id) => ...);
bridge.onPlaybackCompleted.listen((id) => ...);
```

---

## Phase 3: iOS 네이티브

### 목적
- 프로덕션 품질의 iOS 빌드 제공
- Metal 렌더링으로 최적 성능

### Unity 빌드 설정
- **빌드 타겟**: iOS (ARM64)
- **Scripting Backend**: IL2CPP
- **렌더링**: URP + Metal
- **Burst Compiler**: 활성화

### 전달물
```
ios_unity_framework/
├── UnityFramework.framework     # Unity iOS 프레임워크
├── Data/                        # Unity 에셋 데이터
└── integration_guide.md         # 통합 가이드
```

### Flutter 통합 방식

#### 1. Xcode 프로젝트에 Unity 프레임워크 추가
```
ios/
├── Runner/
├── UnityFramework.framework     # Unity 빌드 결과물
└── Podfile
```

#### 2. MethodChannel 브릿지 (Swift)
```swift
class TalkmotionPlugin: NSObject, FlutterPlugin {
    static func register(with registrar: FlutterPluginRegistrar) {
        let channel = FlutterMethodChannel(
            name: "com.firsthabit.talkmotion",
            binaryMessenger: registrar.messenger()
        )
        let instance = TalkmotionPlugin()
        registrar.addMethodCallDelegate(instance, channel: channel)
    }

    func handle(_ call: FlutterMethodCall, result: @escaping FlutterResult) {
        switch call.method {
        case "prepareAudio":
            Unity.shared.sendMessage(
                "FirsthabitBridge", "PrepareAudio", json
            )
        case "play": ...
        case "stop": ...
        }
    }

    // Unity → Flutter 콜백
    static func onPrepared(_ id: String) {
        channel.invokeMethod("onPrepared", arguments: id)
    }
}
```

#### 3. Dart 브릿지
Android와 **완전히 동일한 코드** 사용 (MethodChannel은 플랫폼 무관)

### Android → iOS 변경점
| 항목 | Android | iOS |
|------|---------|-----|
| Unity 빌드 | `unityLibrary.aar` | `UnityFramework.framework` |
| 네이티브 언어 | Kotlin | Swift |
| 렌더링 | Vulkan/OpenGL ES | Metal |
| 빌드 설정 | gradle | Xcode + CocoaPods |
| Dart 코드 | **동일** | **동일** |

---

## 플랫폼별 코드 재사용 매트릭스

```
                        WebGL    Android    iOS
─────────────────────────────────────────────────
Unity C# 로직            ✅        ✅        ✅     (공통, #if 분기만 추가)
FluentT SDK 연동         ✅        ✅        ✅     (공통)
브릿지 API 설계          ✅        ✅        ✅     (공통 스펙)
Dart 콜백 Stream 패턴    ✅        ✅        ✅     (공통)
Flutter UI (main.dart)   ✅        ✅        ✅     (공통, bridge 객체만 교체)
─────────────────────────────────────────────────
Dart 브릿지 구현          Web전용   Mobile공통  Mobile공통
네이티브 플러그인         jslib    Kotlin     Swift
빌드 산출물              WASM     AAR        Framework
```

---

## Flutter 개발자 통합 요약

### Phase 1 (WebGL 데모) 받았을 때
1. `flutter_test_app/` 그대로 실행하여 API 동작 확인
2. 콜백 흐름, 파라미터 검증
3. 자체 앱에 필요한 UI/UX 설계

### Phase 2 (Android) 받았을 때
1. `unityLibrary`를 프로젝트에 추가
2. `UnityBridgeMobile` (MethodChannel 버전) 사용
3. API 시그니처 동일하므로 기존 로직 그대로 사용

### Phase 3 (iOS) 받았을 때
1. `UnityFramework`를 Xcode 프로젝트에 추가
2. Dart 코드 변경 없음 (Android와 동일한 MethodChannel)
3. Swift 네이티브 플러그인만 추가

---

## 성능 기대치 (참고용)

> **주의**: 아래 수치는 일반적인 벤치마크 및 플랫폼 특성에 기반한 **상대적 추정치**입니다.
> 실제 성능은 디바이스 사양, Unity 씬 복잡도, FluentT SDK 처리량, 네트워크 환경 등에 따라
> 크게 달라질 수 있으므로 각 Phase 전달 후 실측이 필요합니다.

|                    | WebGL (데모) | Android 네이티브 | iOS 네이티브 |
|--------------------|-------------|-----------------|-------------|
| 렌더링 FPS         | 15~30       | 45~60           | 50~60       |
| 앱 시작 시간        | 3~8초       | ~1초            | ~1초        |
| 메모리 사용         | 230~400 MB  | 130~200 MB      | 120~180 MB  |
| 천 물리 (Magica)   | 제한적       | 풀 성능          | 풀 성능      |
| 배터리 소모         | 높음        | 보통             | 보통        |
