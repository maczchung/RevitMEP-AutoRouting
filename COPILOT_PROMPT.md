# 貼入 VS Code GitHub Copilot（Agent mode）

```
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
```
