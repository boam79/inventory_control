# 스프링의원 재고관리

Windows 설치형 의료소모품·시술재료 재고 프로그램입니다. 단일 PC, SQLite, 오프라인 핵심 업무.

- 재고는 의원 단일 창고입니다. 사용 시에만 부서를 기록합니다.
- 초기재고를 비우면 0이 아니라 **미설정**입니다.
- 거래는 물리 삭제하지 않고 취소(반대 처리)합니다.
- 환자 식별정보는 저장하지 않습니다.
- 예측·자동업데이트 실패는 입고·출고를 막지 않습니다. 자동 발주 확정은 없습니다.

## 요구 사항

- Windows 10/11 64비트
- **설치본:** .NET SDK 불필요 (self-contained)
- 개발: .NET 10 SDK, 로컬 도구 `vpk` 1.2.0

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

- `Inventory.App` — WPF (`net10.0-windows`)
- `Inventory.Core` — 제품 식별·역할·비밀번호 해시
- `Inventory.Infrastructure` — SQLite/EF Core, 재고 서비스, Excel, 백업, 예측, 업데이트 확인
- `Inventory.Tests` — xUnit

```text
dotnet test
dotnet run --project Inventory.App
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

1. 처음이면 **관리자 만들기**로 아이디/비밀번호를 만듭니다. (소스에 기본 비밀번호 없음)
2. 로그인 후 왼쪽 메뉴: 대시보드, 입고, 사용, 재고현황, 거래내역, LOT, 발주필요, 통계, 월마감, 기준정보, 사용자, 백업, 환경설정
3. 기준정보에서 품목 등록 → 초기재고 LOT 입력 → 확정 → 입고/사용

## 테스트 데이터 (대시보드·예측 확인용)

**거래가 0건이면 관리자 로그인 후 자동으로** 품목 약 1만 개와 1년치 계절 거래를 넣습니다. 실제 입고·사용이 한 건이라도 있으면 자동 시드하지 않습니다.

대시보드 상단의 큰 버튼 **테스트 데이터 생성** 또는 환경설정(관리자)에서도 같은 작업을 할 수 있습니다.

운영 의원 DB를 조용히 덮어쓰지 않습니다. 거래가 이미 많으면 거부하거나 한 번 더 확인합니다. 품목 1만이 이미 있으면 중복 생성하지 않습니다.

개발 PC:

```text
dotnet run --project Inventory.App -- --seed-demo
```

이미 거래가 있으면 추가하지 않습니다. 그래도 붙이려면 `--force` 를 붙입니다(기존 행은 삭제하지 않음).

규모: 품목 약 10,000개, 직전 12~13개월, 봄·여름·가을·겨울 사용량 배율이 다릅니다. LOT는 일부 품목만. 재고는 0 미만이 되지 않습니다.

1.0.1에서 이미 거래를 넣은 DB는 자동 시드가 돌지 않습니다. 테스트로 1만 품목을 보려면 `%LOCALAPPDATA%\SpringClinicInventory\inventory.db` 를 백업 후 삭제하고 다시 로그인하세요.

Excel 가져오기는 **백업·복원** 화면의 `마스터만`(샘플 기본), `마스터+기초`, `전체이력`입니다. 빈 수식 행은 거래가 아닙니다. 기초와 이력을 같이 넣으면 이중계산 경고 후 기초는 건너뜁니다.

업데이트는 상단/환경설정의 **업데이트** 버튼입니다. GitHub Releases의 zip/exe와 `.sha256`이 있으면 내려받아 해시를 검사하고, Velopack 설치본이면 패키지를 적용한 뒤 다시 시작합니다. `dotnet run`은 설치본이 아니라 자동 적용하지 않습니다. 실패하면 **지금 실행 중인 프로그램은 그대로** 둡니다. 실행 중인 앱이 nuget을 동적으로 설치하지는 않습니다.
