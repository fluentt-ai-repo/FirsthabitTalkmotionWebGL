# Firebase Hosting 배포 가이드

## 개요
Flutter WebGL 테스트 앱을 Firebase Hosting으로 배포하여 외부 접속을 제공합니다.

- **배포 URL**: https://firsthabittalkmotionwebgl.web.app
- **Firebase 프로젝트 ID**: `firsthabittalkmotionwebgl`
- **설정 파일 위치**: `flutter_test_app/firebase.json`, `flutter_test_app/.firebaserc`

## 사전 요구사항

- Node.js (v18+)
- Flutter SDK (3.x)
- Firebase CLI: `npm install -g firebase-tools`
- Firebase 로그인: `firebase login`

## 배포 명령어

### 빌드 + 배포 (전체)
```bash
cd flutter_test_app
flutter build web --release
firebase deploy --only hosting
```

### 배포만 (이미 빌드된 경우)
```bash
cd flutter_test_app
firebase deploy --only hosting
```

## 설정 파일 설명

### firebase.json
| 설정 | 값 | 이유 |
|------|-----|------|
| `public` | `build/web` | Flutter 웹 빌드 출력 경로 |
| `.wasm` MIME 타입 | `application/wasm` | Unity WebGL WASM 파일 서빙 필수 |
| `.data` MIME 타입 | `application/octet-stream` | Unity 에셋 데이터 파일 |
| COOP/COEP 헤더 | `same-origin` / `credentialless` | SharedArrayBuffer 지원 (Unity 멀티스레딩) |
| SPA rewrite | `/** → /index.html` | Flutter 클라이언트 라우팅 지원 |

### .firebaserc
Firebase 프로젝트 ID 연결 설정 (`firsthabittalkmotionwebgl`)

## Unity WebGL 빌드 업데이트 시

Unity에서 WebGL 빌드를 새로 생성한 경우:
1. 빌드 출력 파일을 `flutter_test_app/web/unity_webgl/Build/`에 복사
2. `flutter build web --release` 실행 (빌드에 포함됨)
3. `firebase deploy --only hosting` 실행

## 주의사항

- `flutter_test_app/.firebase/` 폴더는 배포 캐시이며 `.gitignore`에 포함됨
- 무료 티어: 월 10GB 전송량 / 1GB 저장소
- 커스텀 도메인: Firebase Console → Hosting → 도메인 추가에서 설정 가능
