MEP Auto Routing – Revit 2025 add-in (net8.0-windows)
=====================================================

BUILD
  dotnet build -c Debug        -> auto-deploys to %AppData%\Autodesk\Revit\Addins\2025
  dotnet build -c Release      -> output only (bin\Release\net8.0-windows)
  * Close Revit before building (DLL is locked while Revit is running).
  * Different Revit path:  dotnet build -p:RevitInstallDir="D:\Autodesk\Revit 2025"

DEPLOY LAYOUT
  %AppData%\Autodesk\Revit\Addins\2025\
    MEPAutoRouting.addin
    MEPAutoRouting\MEPAutoRouting.dll (+ .pdb)

RIBBON
  Tab "MEP Tools" > Panel "Auto Routing" > "Auto Routing"

PROJECT STRUCTURE
  Core\         App (ribbon), AutoRoutingCommand (opens modeless window)
  Routing\      RouteModels, RoutePlanner (Manhattan path), RouteBuilder,
                RoutingEventHandler (ExternalEvent – all API calls), ISegmentFactory
  Plumbing\     PipeSegmentFactory
  Mechanical\   DuctSegmentFactory
  Electrical\   ConduitSegmentFactory, CableTraySegmentFactory
  Shared\       ObservableObject, RelayCommand, ConnectorUtils, selection filter,
                WarningSwallower, UnitConv
  UI\           MainWindow (VS Code Dark Modern style), Themes\VSCodeDark.xaml,
                ViewModels\ (MainViewModel, LogEntry, UserSettings)
  Resources\    Ribbon icons (16/32 px)

UI (VS Code layout)
  Title bar     Command-center pill (project · document), blue Route button, pin / min / max / close
  Activity bar  Route Setup | Output | ... | About | Settings
  Side bar      CONNECTORS (Source / Target cards + swap), SYSTEM (discipline, type,
                system type, level), SUMMARY
  Editor tabs   "Routing Options" (settings-editor style) | "Path Preview" (coordinates table)
  Panel         PROBLEMS (badge) | OUTPUT  ("yyyy-MM-dd HH:mm:ss.fff [info] ..." log)
  Status bar    Revit version, problem count, discipline, segments, length, status

SHORTCUTS
  F5 Preview · Ctrl+Enter Route · Ctrl+1 / Ctrl+2 Pick source / target · Ctrl+J Toggle panel

NOTES
  * Pipe / duct / conduit / cable tray TYPES need routing preferences / fittings with
    an elbow, otherwise segments are created but elbows fail (logged as [warn]).
  * One Route = one transaction = one Undo in Revit.
  * Settings persist in %AppData%\MEPAutoRouting\settings.json
  * Icons use Segoe Fluent Icons (Win11) with Segoe MDL2 Assets fallback (Win10).
