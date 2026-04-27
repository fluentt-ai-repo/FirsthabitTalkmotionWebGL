# FirsthabitTalkmotionWebGL - Project Instructions

## 커뮤니케이션 규칙
- 모든 대화는 **한국어**로 진행
- 코드 주석, 로그 메시지는 영어로 작성
- 문서는 한국어로 작성

## 절대 규칙

### 브랜치 전략
- 모든 작업은 `dev` 브랜치에서 `feat/*`, `fix/*`, `refactor/*` 브랜치를 생성하여 진행
- **`main`, `dev` 브랜치에 직접 작업하거나 커밋 금지**
- 작업 완료 후 `dev`에 merge, 테스트 통과 후 `main`에 merge

### Git 작업
- `git commit`, `git push`, `git merge`는 **사용자의 명시적 허가** 하에만 실행
- 자동 커밋/푸시/머지 절대 금지
- merge 완료 후 작업 브랜치 삭제 여부를 사용자에게 확인

### 버전 관리
- main에 merge 후 반드시 **버전 태그** 생성
- Semantic Versioning 사용 (major.minor.patch)
  - Major: Breaking changes
  - Minor: 하위 호환 새 기능
  - Patch: 하위 호환 버그 수정

### 버전 히스토리 및 문서화 규칙
- **코드 변경과 문서 업데이트는 같은 작업 브랜치에서 함께 수행해야 합니다.** 별도 브랜치를 만들지 마세요.
  - 작업 브랜치에서: 코드 수정 → CLAUDE.md 과제 목록 업데이트 → dev에 머지
- **기능이 변경된 경우, 관련된 다른 문서(CLAUDE.md, FIREBASE_DEPLOY.md 등)도 함께 업데이트해야 합니다.**
- **main merge + 버전 태그 생성 시, Unity 프로젝트 버전도 동일하게 업데이트해야 합니다.**
  - `unity/ProjectSettings/ProjectSettings.asset` 파일의 `bundleVersion` 값을 태그 버전과 일치시킬 것
  - Unity Editor에서: Edit → Project Settings → Player → Version 에서 변경 가능

### Unity .meta 파일
- 새 파일/폴더 생성 시 반드시 `.meta` 파일 필요
- Unity Editor 밖에서 생성한 경우 Unity를 열어 .meta 자동 생성 필요
- `.meta` 파일 누락 시 GUID 충돌 발생

## Git Workflow

### 브랜치 구조
- `main` - 프로덕션 안정 브랜치 (태그 릴리즈만)
- `dev` - 개발 통합 브랜치 (모든 작업의 베이스)
- `feat/*`, `fix/*`, `refactor/*` - 작업 브랜치

### 전체 워크플로우

1. dev 전환 및 업데이트:
   ```bash
   git checkout dev
   git pull origin dev
   ```

2. 작업 브랜치 생성:
   ```bash
   git checkout -b feat/기능명
   ```

3. 작업 및 커밋:
   ```bash
   git add .
   git commit -m "타입: 설명"
   git push -u origin feat/기능명
   ```

4. dev에 merge (작업 완료 후):
   ```bash
   git checkout dev
   git pull origin dev
   git merge feat/기능명
   git push origin dev
   ```

5. **필수: 수동 테스트** (dev merge 후)
   - Flutter 앱에서 브릿지 통신 테스트
   - Prepare → Play → Stop 전체 플로우 확인
   - 콜백 이벤트 정상 수신 확인
   - 변경 기능 검증 + 기존 기능 회귀 테스트

6. main에 merge (테스트 통과 후):
   ```bash
   git checkout main
   git pull origin main
   git merge dev
   git push origin main
   ```

7. **필수: 버전 태그 생성** (main merge 후):
   ```bash
   git tag -a v0.1.0 -m "Release v0.1.0: 기능 설명"
   git push origin v0.1.0
   ```

### 커밋 메시지 컨벤션
- `feat:` - 새 기능
- `fix:` - 버그 수정
- `refactor:` - 코드 리팩토링
- `docs:` - 문서 변경
- `test:` - 테스트 추가/수정
- `chore:` - 빌드, 설정 등 잡무

### 금지 사항
- main에 직접 작업/커밋 금지
- 테스트 없이 dev → main merge 금지
- main merge 후 버전 태그 미생성 금지
- dev 브랜치 우회 금지

### 브랜치 정리
```bash
git branch -d feat/기능명                # 로컬 삭제
git push origin --delete feat/기능명     # 원격 삭제
```

## 코딩 표준

### C# (Unity)
- 클래스/메서드: PascalCase
- private 필드: camelCase
- 파일명: 클래스명과 일치 (PascalCase)
- 네임스페이스: `Firsthabit.WebGL`
- 로그 형식: `Debug.Log("[FirsthabitBridge] Message")`
- Public API: XML 문서 주석 사용

### Dart (Flutter)
- 클래스: PascalCase, 파일/변수: snake_case
- private: `_` prefix
- Stream 기반 콜백 API

## 프로젝트 구조
- `unity/` - Unity WebGL 프로젝트
- `flutter_test_app/` - Flutter 웹 테스트 앱 (브릿지 테스트용)
- Unity WebGL은 iframe으로 Flutter에 임베딩
- **중요**: jslib에서 `window.parent` 사용 (Unity는 iframe 안에서 실행)

## 로컬 실행
- Flutter 테스트 앱 로컬 실행: `cd flutter_test_app && flutter run -d chrome`
- Unity WebGL 빌드 파일이 `flutter_test_app/web/unity_webgl/`에 있어야 함
- Unity 재빌드 후에는 `flutter clean` 필요 (빌드 캐시가 이전 파일을 재사용하므로)

## 배포
- Firebase Hosting 사용 → 상세 가이드: [FIREBASE_DEPLOY.md](./FIREBASE_DEPLOY.md)
- ~~**배포 URL**: https://firsthabittalkmotionwebgl.web.app~~ (수행사 측 호스팅은 2026-04-27 셧다운 처리됨. 본 저장소 인수자는 자체 Firebase 프로젝트로 신규 배포 필요)
- 빠른 배포 (인수자 자체 인프라 기준): `cd flutter_test_app && flutter build web --release && firebase deploy --only hosting`

---

## 남은 과제 목록

### 인수자(퍼스트해빗) 측 후속 최적화 권장
- [ ] **Addressable 에셋 전환** — 웹 배포 시 로딩 속도/파일 용량 최적화를 위해 아바타 에셋을 Addressable로 전환. 본 인수인계 시점에는 적용되지 않은 상태이며, 인수자가 자체 운영 환경에서 진행하면 효과를 얻을 수 있음.

### 본 인수인계 시점(v0.7.3) 처리 완료 항목
- [x] Play 버튼 렉 해결 (성능/UX)
- [x] Stop 부드럽게 종료 (페이드아웃/idle 블렌딩)
- [x] FPS 성능 최적화 (렌더링 해상도, Update 루프 점검)
- [x] Queue 모드 조정 (브릿지 설정 메서드)
- [x] Idle 모션 다양화 (다종 idle 클립 사용)
- [x] 모션 트리거 기능 (One-Shot Motion 브릿지 — v0.6.0)
- [x] 투명 배경 / 컬러 조정 (`SetBackgroundColor` — v0.2.0)
- [x] 아바타 런타임 교체 (`ChangeAvatar` / `GetAvatarList` — v0.3.0)
- [x] 자막 텍스트 전달 (`PrepareAudio` text 파라미터 — v0.3.0)
- [x] 콜백 등록 확인 (16종 콜백 정합성)
- [x] WebGL 빌드파일 호환성 검증 (Flutter 통합 시 압축/경로/CORS/MIME 점검)
- [x] WebGL 빌드 용량 체크 (Brotli + High Stripping + Wasm2023 적용)
- [x] 렌더링/빛 셋팅 일치 (GlobalVolume Profile — Editor·WebGL 일치)
