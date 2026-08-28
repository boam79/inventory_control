# 스프링의원 재고관리 — 프로젝트 문서

이 폴더는 **스프링의원 재고관리** 프로그램의 현재 상황을 의원 직원과 개발자 모두가 이해할 수 있도록 정리한 문서입니다.

**최종 갱신:** 2026-08-28  
**현재 버전:** v1.0.54  
**원격 저장소:** https://github.com/boam79/inventory_control

---

## 문서 목록

| 문서 | 대상 | 내용 |
|------|------|------|
| [01-프로젝트-개요.md](./01-프로젝트-개요.md) | 전체 | 프로그램 목적, 기술 구성, 폴더 구조, 기본 사용법 |
| [02-버전-릴리스-이력.md](./02-버전-릴리스-이력.md) | 개발·운영 | 버전별 변경 사항 |
| [03-화면-기능-구조.md](./03-화면-기능-구조.md) | 의원 직원 | 메뉴 구성, 화면별 기능 |
| [04-UI-UX-변경-이력.md](./04-UI-UX-변경-이력.md) | 전체 | 화면·사용성 개선 연혁 |
| [05-샘플-데이터-및-성능.md](./05-샘플-데이터-및-성능.md) | 개발·테스트 | 테스트 데이터, 쿼리 최적화 |
| [06-업데이트-배포.md](./06-업데이트-배포.md) | 개발·운영 | 빌드, Velopack, GitHub Releases |
| [07-알려진-이슈-및-진행중.md](./07-알려진-이슈-및-진행중.md) | 전체 | 해결된 이슈, 확인 필요 항목 |
| [08-맥-윈도우-개발환경.md](./08-맥-윈도우-개발환경.md) | 개발 | 같은 저장소를 Windows·macOS에서 쓰는 방법 |
| [09-사용-알림-하트비트.md](./09-사용-알림-하트비트.md) | 개발 | 설치·사용 알림 메일(SMTP·한메일 앱 비밀번호) |
| [10-킬스위치-최소버전.md](./10-킬스위치-최소버전.md) | 개발 | GitHub 원격 킬스위치·minVersion (의원 UI 없음) |

---

## 빠른 참조

### 의원 PC에서 설치

1. [GitHub Releases](https://github.com/boam79/inventory_control/releases)에서 최신 `*Setup.exe` 다운로드
2. 더블클릭으로 설치 (시작 메뉴·바탕화면 바로가기 자동 생성)
3. 실행하면 **로그인 없이** 바로 메인 화면

### 개발 PC에서 빌드·테스트

자세한 절차·제한은 [08-맥-윈도우-개발환경.md](./08-맥-윈도우-개발환경.md)입니다.

Windows와 macOS 모두:

```text
dotnet test
```

Windows (화면·설치본):

```powershell
powershell -File scripts/dev.ps1
dotnet run --project Inventory.App
powershell -ExecutionPolicy Bypass -File scripts/pack-installer.ps1
```

macOS (도메인·테스트만):

```bash
./scripts/dev.sh
```

### 데이터 위치

| 항목 | 경로 |
|------|------|
| DB | `%LOCALAPPDATA%\SpringClinicInventory\inventory.db` |
| 로그 | `%LOCALAPPDATA%\SpringClinicInventory\logs\` |
| 백업 | `%LOCALAPPDATA%\SpringClinicInventory\backups\` |

### 작업 기록 계정

로그인 화면은 없습니다. 모든 작업은 **로컬 운영자(`local`)** 또는 DB에 등록된 **관리자** 계정 이름으로 기록됩니다.

---

## 개발 워크플로 (Planner / Executor)

이 프로젝트는 Cursor AI와 함께 작업할 때 **Planner**(계획)와 **Executor**(구현) 역할로 나누어 진행합니다.

- **Planner:** 요구사항 분석, 작업 분해, 성공 기준 정의 → `.cursor/scratchpad.md`에 기록
- **Executor:** 한 번에 하나의 작업만 구현, 테스트 후 scratchpad에 진행 상황 기록
- **완료 확인:** Executor가 완료를 보고하면 Planner(사용자)가 직접 확인 후 다음 단계 진행

내부 작업 메모는 `.cursor/scratchpad.md`에 있으며, 이 DOC 폴더는 **대외·운영용 정리본**입니다.
