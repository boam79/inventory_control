# 스프링의원 재고관리

Windows 설치형 의료소모품·시술재료 재고 프로그램입니다. 단일 PC, SQLite, 오프라인 핵심 업무.

## 요구 사항

- Windows 10/11 64비트
- .NET 10 SDK

## 솔루션

- `Inventory.App` — WPF (`net10.0-windows`)
- `Inventory.Core` — 재고 도메인
- `Inventory.Infrastructure` — 데이터 접근 (SQLite/EF Core는 이후 작업)
- `Inventory.Tests` — xUnit

```text
dotnet test
```

원격 저장소: https://github.com/boam79/inventory_control.git
