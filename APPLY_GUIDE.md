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

## 套用
1. **覆蓋**：`Shared/Revit/RevitActionQueue.cs`、`Shared/Routing/PipeSizeCatalog.cs`、`UI/MainWindow.xaml.cs`、`UI/ViewModels/MainViewModel.Constraints.cs`、`UI/ViewModels/MainViewModel.Route.cs`
2. **貼 snippet**：`AutoRoutingCommand.cs`、`MainViewModel.cs`
3. Build → 關 Revit → Deploy → 開 Revit

## 測試
- [ ] 開 Unified Routing：唔會 crash
- [ ] 閂 window 再開 3 次：唔會 crash，OUTPUT 冇重複 log
- [ ] 轉 Pipe Type：size list 更新
- [ ] 撳 Route：OUTPUT 逐行出（見 v4.1 APPLY_GUIDE）
- [ ] 打開 `%AppData%\MEPAutoRouting\error.log`：將內容貼返上嚟（入面會有開 window 時第一個失敗嘅 action 同完整 stack）

> 如果 Revit 仍然 crash，將新嘅 journal（`%LocalAppData%\Autodesk\Revit\Autodesk Revit 2025\Journals\` 最新嗰個）同 `error.log` 一齊上載。
