# 貼入 VS Code GitHub Copilot（Agent mode）

```
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
```
do