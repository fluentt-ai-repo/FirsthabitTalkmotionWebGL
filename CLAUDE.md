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

---

## 남은 과제 목록

### 성능/UX
- [ ] **Play 버튼 렉 해결** - Play 누를 때마다 프레임 드롭 발생. PlayPrepared 호출 시 메인 스레드 블로킹 원인 조사 필요
- [ ] **Stop 부드럽게 끝내기** - StopTalkMotion() 호출 시 즉시 끊기지 않고 부드럽게 전환되도록 개선 (페이드아웃 또는 idle 블렌딩)
- [ ] **FPS 성능 최적화** - 불필요하게 리소스 차지하는 부분 확인. 렌더링 해상도, Update 루프, 불필요한 컴포넌트 등 점검

### 기능
- [ ] **Queue 모드 조정** - 현재 queue 모드가 기본값. Flutter에서 queue/interrupt 모드를 선택할 수 있도록 브릿지에 설정 메서드 추가
- [ ] **Idle 모션 다양화** - idle 모션이 단일 반복이 아닌 여러 idle 애니메이션이 랜덤/순차 재생되도록 개선
- [ ] **투명 배경 / 컬러 조정** - WebGL 카메라 배경을 투명(alpha=0)으로 설정하거나 Flutter에서 배경색을 동적으로 변경할 수 있도록 지원

### 검증/확인
- [ ] **콜백 등록 확인** - Flutter 개발자가 콜백을 자유롭게 편집/추가/제거할 수 있는 구조인지 검토
- [ ] **WebGL 빌드파일 호환성 확인** - Flutter 프로젝트에서 WebGL 빌드 사용 시 문제 없는지 확인 (압축, 경로, CORS, MIME 타입 등)
- [ ] **WebGL 빌드 용량 체크** - 빌드 결과물 크기 점검. 불필요한 에셋, 미사용 패키지 등 용량 줄일 수 있는 부분 확인
- [ ] **렌더링/빛 셋팅 일치 확인** - 퍼스트해빗 편집툴의 렌더링 및 라이팅 설정과 완전히 일치하는지 비교 검증
