<<<<<<< HEAD
# MEPAutoRouting v4.2 crash fix

## 問題（journal.0131.txt）
開 Unified Routing 之後 Revit "unrecoverable error" 閂咗。Crash stack：

```
RevitActionQueue.Execute
 → AutoRoutingCommand lambda（queue.Log 訂閱）
 → MainViewModel.Log → LogEntries.Add
 → MainWindow CollectionChanged → OutputList.ScrollIntoView
 → InvalidOperationException: An ItemsControl is inconsistent with its items source
```
Exception 走出 `IExternalEventHandler.Execute` → Revit 終止。

## 修正
| # | 檔案 | 改動 |
|---|---|---|
| 1 | `UI/MainWindow.xaml.cs` | Auto-scroll 改用 `BeginInvoke(Background)`，合併連續 log；`InitConstraintUi` 搬去 `Loaded`；所有 log 包 try-catch |
| 2 | `Shared/Revit/RevitActionQueue.cs` | `SafeLog`：寫 log / UI handler 失敗都唔會 throw 出 Execute；錯誤寫入 `%AppData%\MEPAutoRouting\error.log`（完整 stack） |
| 3 | `UI/ViewModels/MainViewModel.Constraints.cs` | `InitConstraintUi` 只行一次；Revit thread → UI 改 `PostToUi`（BeginInvoke + try-catch） |
| 4 | `UI/ViewModels/MainViewModel.Route.cs` | 同上，全部改 `PostToUi` |
| 5 | `Shared/Routing/PipeSizeCatalog.cs` | 讀 size list 逐條 try-catch，失敗返回空 list（可自由輸入），唔會 throw |
| 6 | `Snippets/AutoRoutingCommand.cs.snippet` | queue.Log 訂閱改 BeginInvoke；閂 window 取消訂閱 |
| 7 | `Snippets/MainViewModel_changes.cs.snippet` | Constructor 刪 `InitConstraintUi()`；`Log()` 確保 UI thread；刪 VM 入面重複訂閱 |
=======
# MEPAutoRouting v4.1（完整版 = v4 + v4.1 hotfix）

## 包含
| 版本 | 內容 |
|---|---|
| v4 | Publisher（Cundall HK · Matthew Kwok）、Calculation boundary 手動 Pick、Pipe size input、Problems 英文、Slope / wall clearance VM wrapper、Flow direction |
| v4.1 | 修「撳 Route 冇 pipe」：Route / Preview 統一行 `RevitActionQueue`、每步寫 OUTPUT、捉 UI exception、捉 Revit rollback、檢查 script |

## 檔案放邊度
| Package | Project | 動作 |
|---|---|---|
| `Shared/**/*.cs` | 同名檔案 | **覆蓋**（如果 Copilot 搬咗位置，覆蓋嗰個；唔好留兩份） |
| `UI/MainWindow.xaml`、`UI/MainWindow.xaml.cs` | `UI/` | **覆蓋** |
| `UI/ViewModels/MainViewModel.Constraints.cs`、`MainViewModel.Route.cs` | `UI/ViewModels/` | 新增（partial class） |
| `Deploy-Revit2025.ps1` | root | 覆蓋 |
| `Tools/Check-Project.ps1` | `Tools/` | 新增 |
| `Snippets/*` | — | 貼入現有檔案 |
| `Reference/RouteService.reference.cs.txt` | — | **唔好覆蓋**，只 merge `[v4-x]` / `[v4.1-x]` |
>>>>>>> 6dd4d98ac757c36819d8fe104c6a91a62610d7a2

## 套用
1. **覆蓋**：`Shared/Revit/RevitActionQueue.cs`、`Shared/Routing/PipeSizeCatalog.cs`、`UI/MainWindow.xaml.cs`、`UI/ViewModels/MainViewModel.Constraints.cs`、`UI/ViewModels/MainViewModel.Route.cs`
2. **貼 snippet**：`AutoRoutingCommand.cs`、`MainViewModel.cs`
3. Build → 關 Revit → Deploy → 開 Revit

<<<<<<< HEAD
## 測試
- [ ] 開 Unified Routing：唔會 crash
- [ ] 閂 window 再開 3 次：唔會 crash，OUTPUT 冇重複 log
- [ ] 轉 Pipe Type：size list 更新
- [ ] 撳 Route：OUTPUT 逐行出（見 v4.1 APPLY_GUIDE）
- [ ] 打開 `%AppData%\MEPAutoRouting\error.log`：將內容貼返上嚟（入面會有開 window 時第一個失敗嘅 action 同完整 stack）

> 如果 Revit 仍然 crash，將新嘅 journal（`%LocalAppData%\Autodesk\Revit\Autodesk Revit 2025\Journals\` 最新嗰個）同 `error.log` 一齊上載。
=======
## Step 1：行檢查 script（先做）
```powershell
powershell -ExecutionPolicy Bypass -File .\Tools\Check-Project.ps1
```
- `[DUP]`：同一個 class 有兩份 → 刪舊嗰份
- `[OLD] SpacePicker`：刪除
- 中文字串：改英文（你上次 OUTPUT 出嘅 `Slope % 要喺 0 – 20 之間` 就係舊 `SlopeSettings.cs`）
- `RoutingEventHandler` / `.Raise()`：記低位置，Step 3 用

## Step 2：覆蓋 / 新增檔案
照上面檔案表。

## Step 3：MainViewModel
跟 `Snippets/MainViewModel_hooks.cs.snippet`：
1. class 加 `partial`
2. 實作 6 個 hook：`Notify`、`GetSelectedTypeId`、`IsConduitDiscipline`、`GetSelectedPipingSystemType`、`BuildRouteRequest`、`ApplyRouteResult`
3. Constructor 最尾：`ActionQueue = queue; InitConstraintUi();`
4. `RouteCommand` / `PreviewCommand` 改做 `ExecuteRoute(true / false)`
5. **唔好再 Raise 舊 `RoutingEventHandler`**
6. `LeadMm` 寫入 `ConstraintOptions.LeadLengthMm`
7. 刪除舊有 `SlopeText`、`BoundaryText`、`OnSystemTypeChanged`、Boundary 相關 member

## Step 4：RouteService
跟 `Reference/RouteService.reference.cs.txt` merge 所有 `[v4-x]` 同 `[v4.1-x]`。重點：
- Commit 段用 `RouteFailureCollector` + 檢查 `TransactionStatus.Committed`
- 每一步寫 log
- 每個 early `return res;` 之前都要有 `RouteProblem.Error(...)`

## Step 5：Strategy
Routing Options 仲有 "Horizontal X → Y"（舊 L 形）。確認揀任何 Strategy 都會行 `AStarPathfinder.FindPath`。

## Step 6：Publisher
- `.addin`：`Snippets/addin_publisher.snippet.xml`
- `.csproj`：`Snippets/csproj_publisher.snippet.xml`

> Revit "Unsigned Add-in" 對話框嘅 Publisher 讀 code signing 證書，唔係 `.addin`。要顯示 Cundall HK 就要搵 IT 攞證書簽 DLL。

## Step 7：Build + Deploy
```powershell
dotnet build -c Release
powershell -ExecutionPolicy Bypass -File .\Deploy-Revit2025.ps1
```

---

## 撳 Route 之後應該見到嘅 OUTPUT
```
[info] ▶ Route clicked
[info] Route queued…
[info] Route: Revit handler started.
[info] Start · route Ø40 mm · lead 150 mm
[info] Region … mm (Space 1 - Space 1)
[info] Grid … cells @ 50 mm
[info] Grid ready · … cells blocked by constraints · … walls
[info] A* finished · … points
[info] Path … points · slope 1:100 (1 %) · fall … mm
[info] Creating … segments…
[info] … segments created, adding elbows…
[done] Created … segments and … elbows (… warning(s)).
```
斷咗喺邊一行，就係嗰一步有問題。將 OUTPUT、PROBLEMS 同 Check script 結果貼返上嚟。

## 你截圖見到嘅其他位
- **Space 1 高 1278 mm**：扣埋管半徑同牆間距，可行空間好窄；出 "No path found" 就加高 Space 嘅 Upper Limit / Limit Offset
- **Slope 1 %，Target（Pump）比 Source 高**：Flow = Source → Target 會出 "rises in the flow direction" warning，正常

---

## 測試清單
- [ ] Check-Project.ps1：冇 `[DUP]`、冇中文字串、`TransactionStatus.Committed` OK
- [ ] 撳 Route：OUTPUT 逐行出到 `Created … segments`
- [ ] Revit Undo list 見到 "MEP Auto Route"
- [ ] 故意揀錯 System Type：PROBLEMS 出 `Revit: …` error，唔會靜靜雞
- [ ] About pane / status bar 見到 Cundall HK · Matthew Kwok
- [ ] Pick / Pick linked boundary：卡片資料正確；Esc 出 "Boundary pick cancelled."
- [ ] Size 打 110（list 冇）：自動改 100 並出 warning
- [ ] 揀 Sanitary：Apply slope 即時剔上；1:40 轉 % 變 2.5
- [ ] PROBLEMS：Error 紅、Warning 黃，全部英文
>>>>>>> 6dd4d98ac757c36819d8fe104c6a91a62610d7a2
