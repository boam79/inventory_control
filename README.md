# 스프링의원 재고관리

Windows 설치형 의료소모품·시술재료 재고 프로그램입니다. 단일 PC, SQLite, 오프라인 핵심 업무.

- 재고는 의원 단일 창고입니다. 사용 시에만 부서를 기록합니다.
- 초기재고를 비우면 0이 아니라 **미설정**입니다.
- 거래는 물리 삭제하지 않고 취소(반대 처리)합니다.
- 환자 식별정보는 저장하지 않습니다.
- 예측·자동업데이트 실패는 입고·출고를 막지 않습니다. 자동 발주 확정은 없습니다.

## 요구 사항

- **의원 PC / UI / 설치본:** Windows 10/11 64비트. 설치본은 .NET SDK 불필요 (self-contained)
- **개발 (Windows·macOS 공통):** .NET 10 SDK. 로컬 도구 `vpk` 1.2.0은 Windows에서 Setup.exe를 만들 때만 필요

같은 저장소를 양쪽에서 씁니다. `dotnet test` 는 OS를 가리지 않습니다. WPF 화면과 Setup.exe만 Windows입니다.

절차·제한·SDK 설치는 [DOC/08-맥-윈도우-개발환경.md](DOC/08-맥-윈도우-개발환경.md)를 따릅니다.

| | Windows | macOS |
|--|---------|-------|
| 테스트·도메인 | `powershell -File scripts/dev.ps1` | `./scripts/dev.sh` |
| 앱 화면 | `dotnet run --project Inventory.App` | 불가 (App은 스텁으로만 빌드) |
| Setup.exe | `scripts/pack-installer.ps1` | 불가 |
| CI | GitHub Actions `windows-latest` | GitHub Actions `macos-latest` |

Windows SDK: https://aka.ms/dotnet/download

## 설치 프로그램 (더블클릭)

의원 PC에서는 아래 Setup.exe를 더블클릭합니다. 시작 메뉴와 바탕화면 바로가기가 생깁니다.

```text
powershell -ExecutionPolicy Bypass -File scripts/pack-installer.ps1
```

생성된 파일:

- `dist\*Setup.exe` — **이것을 더블클릭**
- `dist\*.sha256` — 해시 검증용 (코드 서명 인증서 없음 → SmartScreen 경고가 날 수 있음)

DB는 설치 폴더가 아니라 `%LOCALAPPDATA%\SpringClinicInventory\inventory.db` 입니다.

업데이트 피드: https://github.com/boam79/inventory_control/releases  
설치본에서 상단 **업데이트** 또는 환경설정의 **업데이트**를 누르면 GitHub Releases의 새 패키지를 받아 SHA256(있으면)을 검사한 뒤 적용하고 앱을 다시 시작합니다. `dotnet run`은 설치본이 아니라 자동 적용하지 않습니다. 실패해도 재고 업무는 계속됩니다.

`gh`로 릴리스에 올리려면 (로그인 후):

```text
gh release create v1.0.3 dist/*Setup.exe dist/*.sha256 --repo boam79/inventory_control --title "v1.0.3" --notes "업데이트 버튼: 다운로드·SHA256·적용·재시작"
```

다운로드: https://github.com/boam79/inventory_control/releases/download/v1.0.3/SpringClinic.Inventory-win-Setup.exe

큰 exe는 git에 넣지 않습니다. 빌드 스크립트만 커밋합니다.

zip만 있을 때 바로가기: `scripts/install-shortcut.ps1`

## 솔루션

- `Inventory.App` — WPF (`net10.0-windows`). macOS/Linux에서는 UI 없이 스텁 라이브러리로만 로드됩니다.
- `Inventory.Core` — 제품 식별·역할·비밀번호 해시
- `Inventory.Infrastructure` — SQLite/EF Core, 재고 서비스, Excel, 백업, 예측, 업데이트 확인
- `Inventory.Tests` — xUnit

```text
dotnet test
```

Windows에서 화면:

```text
dotnet run --project Inventory.App
```

한 번에 복원·빌드·테스트:

```text
./scripts/dev.sh
powershell -File scripts/dev.ps1
```

데이터베이스 기본 경로: `%LOCALAPPDATA%\SpringClinicInventory\inventory.db`  
로그: `%LOCALAPPDATA%\SpringClinicInventory\logs`  
자동 백업: `%LOCALAPPDATA%\SpringClinicInventory\backups` (하루 첫 실행 1회)

업데이트 확인: https://github.com/boam79/inventory_control/releases  
(확인 실패해도 앱은 계속 됩니다.)

원격 저장소: https://github.com/boam79/inventory_control.git

## 설치·실행 (게시본)

다른 폴더에 풀어서 실행해도 됩니다. DB는 exe 옆이 아니라 AppData에 있습니다.

```text
dotnet publish Inventory.App -c Release -r win-x64 --self-contained true -o publish
```

`publish\Inventory.App.exe` 를 실행합니다.

1. 실행하면 **바로 메인 화면**이 열립니다. (로그인 없음 · 작업 기록은 로컬 운영자/`local` 관리자 계정)
2. 위쪽 메뉴: 입고, 출고, 재고, 통계, 대시보드, 사용자, 백업, 설정
3. 대시보드에서 품목을 선택하면 한 그래프에서 월별 출고를 비교합니다. 품목 추가는 백업의 엑셀 가져오기에서 합니다.

## 테스트 데이터 (대시보드·예측 확인용)

**거래가 0건이면 시작 시 자동으로** 품목 1,000개와 1년치 계절 거래를 넣습니다. 실제 입고·출고가 한 건이라도 있으면 자동 시드하지 않습니다.

대시보드 상단의 큰 버튼 **테스트 데이터 생성** 또는 환경설정(관리자)의 **테스트 데이터 다시 만들기**로 넣을 수 있습니다. 이미 샘플 데이터가 있을 때 대시보드의 **샘플 데이터를 다양하게 다시 만들기**(또는 환경설정의 같은 작업)를 누르면 **기존 품목·입고·출고를 모두 지우고** 정확히 1,000개 품목으로 교체합니다(예: 10,000개 → 1,000개).

운영 의원 DB를 조용히 덮어쓰지 않습니다. 빈 DB에 자동/첫 생성할 때는 거래가 이미 많으면 거부합니다. 다시 만들기는 확인 후 기존 샘플을 삭제하고 교체합니다.

개발 PC:

```text
dotnet run --project Inventory.App -- --seed-demo
```

이미 거래가 있으면 추가하지 않습니다. 기존 품목·거래를 지우고 다시 만들려면 `--force` 를 붙입니다.

규모: 품목 1,000개, 직전 12~13개월, 봄·여름·가을·겨울 사용량 배율이 다릅니다. LOT는 일부 품목만. 재고는 0 미만이 되지 않습니다.

1.0.1에서 이미 거래를 넣은 DB는 자동 시드가 돌지 않습니다. 테스트로 새 샘플 품목을 보려면 `%LOCALAPPDATA%\SpringClinicInventory\inventory.db` 를 백업 후 삭제하고 다시 실행하거나, 대시보드의 다시 만들기 버튼을 쓰세요.

Excel 가져오기는 **백업·복원** 화면의 `마스터만`(샘플 기본), `마스터+기초`, `전체이력`입니다. 빈 수식 행은 거래가 아닙니다. 기초와 이력을 같이 넣으면 이중계산 경고 후 기초는 건너뜁니다.

품목마스터 시트 열 순서: 1=품목코드, 2=품목명, 3=분류, 4=규격/단위, 5=참고단가, 6=기본사용부서(선택, 없으면 비워둠 — 이름이 새 부서면 자동으로 만들어집니다), 8=최소재고.

업데이트는 상단/환경설정의 **업데이트** 버튼입니다. GitHub Releases의 zip/exe와 `.sha256`이 있으면 내려받아 해시를 검사하고, Velopack 설치본이면 패키지를 적용한 뒤 다시 시작합니다. `dotnet run`은 설치본이 아니라 자동 적용하지 않습니다. 실패하면 **지금 실행 중인 프로그램은 그대로** 둡니다. 실행 중인 앱이 nuget을 동적으로 설치하지는 않습니다.
