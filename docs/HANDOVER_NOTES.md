# Handover Notes (manual input)
<!--
  每日收工前更新呢個檔，然後 commit + push。
  05:00 HKT 嘅 GitHub Action 會讀取以下 "## " 標題內容，合併入接手報告。
  ⚠ 唔好改 "## " 標題名稱（Script 用標題搵內容）。可以喺標題下面加 "### " 子標題。
-->

## Status
In Development - v4.8.3 build stable (0 errors / 0 warnings)

## Environment
- Revit 2025 (API: .NET 8, net8.0-windows)
- Visual Studio 2022 / VS Code
- Add-in manifest: MEPAutoRouting.addin
- Main DLL: MEPAutoRouting.dll
- Publisher: Cundall HK / Matthew Kwok

## Today
- (今日做咗咩，例如：Fixed boundary picker null reference)
- 

## Completed Features
- Modeless UI + RevitActionQueue (ExternalEvent)
- Route button event / preview route
- Pipe size input
- Slope input
- Calculation boundary: Manual pick / Room / Space / Mass
- Problems panel in English
- Publisher info

## Test Results
| ID | Item | Status |
|---|---|---|
| R-01 | Command launch | PASS |
| R-02 | Modeless window | PASS |
| R-11 | Route button event | PASS |
| R-13 | Preview route | PASS |
| R-19 | Transaction handling | PASS |

## Known Issues
- (例如：Wall avoidance fails when wall is curved)

## Next Priority
1. Wall avoidance enhancement
2. Auto elbow creation / fitting placement
3. Route optimisation (shortest path, fewest fittings)
4. Performance on large models

## Design Decisions
- All model changes go through RevitActionQueue (no direct API calls from WPF thread)
