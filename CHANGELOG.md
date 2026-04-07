# Changelog

## [0.6.0] - 2026-04-07

### Added
- **One-Shot Motion 브릿지 연동**: Flutter 테스트 앱에서 Listening/Thinking 모션을 트리거할 수 있는 전체 파이프라인 구현
  - `PlayOneShotMotion(json)` — 단일 모션(`motionId`) 또는 그룹 루프(`groupId`) 지원
  - `StopOneShotMotion()` — 현재 재생 중인 원샷 모션 중지
  - `GetOneShotMotionList()` — 등록된 모션/그룹 ID 목록 조회
  - jslib 콜백: `FH_OnOneShotMotionStarted`, `FH_OnOneShotMotionEnded`, `FH_OnOneShotMotionList`
  - Dart Bridge: stream controllers + `playOneShotMotionGroup()`, `stopOneShotMotion()` 등
  - Flutter UI: "One-Shot Motion" 섹션에 Listening / Thinking / Stop 버튼 추가
- 아바타 프리팹에 One-Shot Motion Group 설정 (Listening, Thinking 그룹)

### Changed
- SDK 패키지 의존성에서 특정 브랜치 참조 제거
- AvatarTestScene 씬 업데이트 (One-Shot Motion Group 구성 반영)
- WebGL 빌드 파일 업데이트

### Removed
- 미사용 애니메이션 파일 정리 (Emotion, ProcessedAnimations, ProcessedAnimations2, TriggerMotion, added motion 폴더)
  - 약 400개 파일 삭제로 저장소 용량 절감

---

## [0.5.2] - 2026-04-06

### Changed
- SDK 브랜치 전환, 에디터 도구 추가, 애니메이션/씬 업데이트

## [0.5.1] - 2026-04-04

### Changed
- 애니메이션 커브 정리, 에디터 도구 추가, 아바타/씬 업데이트
