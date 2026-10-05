# 貼入 VS Code GitHub Copilot（Agent mode）

```
<<<<<<< HEAD
套用 MEPAutoRouting v4.2 crash fix（跟 APPLY_GUIDE.md）。
Revit journal 顯示：RevitActionQueue.Execute → AutoRoutingCommand 嘅 queue.Log lambda →
MainViewModel.Log → LogEntries.Add → MainWindow CollectionChanged → OutputList.ScrollIntoView
throw "An ItemsControl is inconsistent with its items source"，exception 走出 ExternalEvent，Revit crash。

1. 用 package 檔案覆蓋：Shared/Revit/RevitActionQueue.cs、Shared/Routing/PipeSizeCatalog.cs、
   UI/MainWindow.xaml.cs、UI/ViewModels/MainViewModel.Constraints.cs、UI/ViewModels/MainViewModel.Route.cs
   （如果 project 入面位置唔同，覆蓋 project 用緊嗰份，唔好留兩份）。
2. Core/AutoRoutingCommand.cs：跟 Snippets/AutoRoutingCommand.cs.snippet 修改 queue.Log 訂閱
   （BeginInvoke + try-catch + window.Closed 取消訂閱）。
3. MainViewModel.cs：跟 Snippets/MainViewModel_changes.cs.snippet —— constructor 刪除
   InitConstraintUi()；Log() 喺非 UI thread 用 BeginInvoke；刪除 VM 入面任何 queue.Log += 訂閱。
4. 全 project 搜尋 `_uiDispatcher.Invoke(` / `Dispatcher.Invoke(`：由 ExternalEvent 返 UI 嘅地方
   改做 BeginInvoke（或者 PostToUi）。列出改咗邊度。
5. 全 project 搜尋 `ScrollIntoView`：唔可以喺 CollectionChanged 入面同步 call。

改完 dotnet build -c Release，要 0 error 0 warning，列出改動嘅 file / 行號。
=======
套用 MEPAutoRouting v4.1 完整版（跟 APPLY_GUIDE.md）。問題：撳 Route 之後 OUTPUT 只有
"▶ Route clicked"，冇 pipe、冇 error。

1. 行 Tools/Check-Project.ps1，列出結果。刪除重複 class（保留 package 版本）同舊 SpacePicker。
2. 用 package 嘅 Shared/**/*.cs 覆蓋 project 同名檔案（包括 SlopeSettings.cs 英文版），
   新增 AppInfo.cs、PipeSizeCatalog.cs、RouteFailureCollector.cs。唔好改呢啲 helper。
3. 用 UI/MainWindow.xaml 同 UI/MainWindow.xaml.cs 覆蓋；新增
   UI/ViewModels/MainViewModel.Constraints.cs 同 MainViewModel.Route.cs。
4. MainViewModel.cs：加 partial，跟 Snippets/MainViewModel_hooks.cs.snippet 實作 6 個
   partial hook（對返現有 property 名）；constructor 最尾 ActionQueue = queue; InitConstraintUi();
   RouteCommand / PreviewCommand 改做 ExecuteRoute(true/false)；LeadMm 寫入
   ConstraintOptions.LeadLengthMm；刪除重複 member；唔好再 Raise 舊 RoutingEventHandler。
5. RouteService.cs：跟 Reference/RouteService.reference.cs.txt merge 所有 [v4-x] 同 [v4.1-x]，
   保留現有 VoxelGrid / A* / GeometryExtractor 整合。每個 early return 前都要加 RouteProblem.Error。
6. 確認所有 Routing Strategy 都行 AStarPathfinder.FindPath；如果 "Horizontal X → Y"
   有獨立 path builder，列出嚟唔好刪，等我決定。
7. .addin / .csproj 加 Publisher（Snippets/addin_publisher.snippet.xml、csproj_publisher.snippet.xml）。
8. 全 project 顯示俾用戶嘅中文字串改英文；註解可以保留中文。

改完 dotnet build -c Release，要 0 error 0 warning；再行一次 Check-Project.ps1，
列出結果同改動嘅 file / 行號。
>>>>>>> 6dd4d98ac757c36819d8fe104c6a91a62610d7a2
```
do