# MEPAutoRouting – Search Evidence

All searches executed against the workspace on 2026-10-07 (excluding bin/obj/.git/.vs unless stated).

## 1. Keyword searches requested by the audit brief

| Query | Result |
|---|---|
| `UnifiedRoutingCommand` / `UnifiedRoutingUI` | 0 hits in compiled `.cs`. Only `UI/UnifiedRoutingCommand_Integration.cs.snippet`, `UI/UnifiedRoutingUI_Constraints(.xaml).snippet` (not compiled), the stale README_BUILD.txt line 4, and the legacy deployed 2024 manifest. |
| `VoxelGrid` | 0 class definitions. Only a stale comment in [Shared/RoutingConstraints.cs](../Shared/RoutingConstraints.cs#L7) and the non-compiled `UI/VoxelGrid_ApplyConstraints.cs.snippet`. |
| `TaskDialog` | 0 `TaskDialog.Show` call sites. Single comment mention in [Core/AStarPathfinder.cs](../Core/AStarPathfinder.cs#L13). |
| `NotImplementedException` / `TODO` / `FIXME` / `HACK` | 0 hits. |
| `async void` / `Task.Run` / timers | 0 hits. |
| `static event` | 0 hits. (Only instance event `RevitActionQueue.Log`, [Shared/Revit/RevitActionQueue.cs](../Shared/Revit/RevitActionQueue.cs#L19).) |
| `new Transaction(` | 1 hit: [Routing/RouteService.cs](../Routing/RouteService.cs#L186). |
| `TransactionGroup` / `SubTransaction` | 0 hits. |
| `ExternalEvent.Create` | 1 hit: [Shared/Revit/RevitActionQueue.cs](../Shared/Revit/RevitActionQueue.cs#L31). |
| `sourceId` / `targetId` | not present as raw fields; connectors stored as `ConnectorInfo` (OwnerId + ConnectorId), [Routing/RouteModels.cs](../Routing/RouteModels.cs#L40-L45). |
| `catch` | 54 matches across 21 files – all reviewed; silent/empty catches listed in audit §15 (GeometryExtractor.cs L53/L100, AStarPathfinder.cs L378 are the only information-hiding ones). |
| CJK in string literals `"…[\u4e00-\u9fff]…"` | **0 hits** – all user-facing strings English. CJK appears in comments only (Cantonese), e.g. RevitActionQueue.cs L5-L8, RoutingConstraints.cs L7, SlopeApplier.cs L7-L13, WallObstacle.cs L8, ConnectorEndpoint.cs L6-L9, BoundaryPicker.cs L12, SpaceVolume.cs L8, SolidVolume.cs L7, MassVolume.cs L6, PathUtils.cs L7, RoutingOptions.cs L7, PipeSizeCatalog.cs L17, AppInfo.cs L5, RouteFailureCollector.cs L6, MainViewModel.Route.cs L2-L5, MainViewModel.Constraints.cs L2-L8, MainWindow.xaml.cs L27/L40/L58, MainWindow.xaml "CALCULATION BOUNDARY（手動 Pick…）" comment. |

## 2. Symbol usage evidence

| Symbol | Definition | Call sites | Verdict |
|---|---|---|---|
| `RouteService.Run` | Routing/RouteService.cs:32 | MainViewModel.Route.cs:51 only | single active route path |
| `RoutePlanner.Plan` | Routing/RoutePlanner.cs:16 | none | LEGACY/dead (only `DirectionLabel` used at MainViewModel.cs:276,411) |
| `RoutingVolumeUtils.*` | Core/RoutingVolumeUtils.cs | none | dead |
| `WarningSwallower` | Shared/WarningSwallower.cs:6 | none | dead |
| `MainViewModel.SetPreview` | MainViewModel.cs:260 | none | dead |
| `MainViewModel.BuildOptions` | MainViewModel.cs:201 | none | dead |
| `SlopeApplier.TotalFall` | Shared/SlopeApplier.cs:97 | none | dead |
| `SelectAfterRoute` | MainViewModel.cs:198 / UserSettings.cs:20 | persisted + bound; never consumed | unimplemented feature |
| `SlopeApplier.Apply` | Shared/SlopeApplier.cs:18 | RouteService.cs:167 (after Validate at L162) | live, post-validation |
| `PathValidator.Validate` | Shared/RouteProblem.cs:28 | RouteService.cs:162 | live |
| `BfsReachable` | Core/AStarPathfinder.cs:263 | AStarPathfinder.cs:135-136 | live |
| `SetVerticalLimits` | Core/AStarPathfinder.cs:58 | RouteService.cs:139 | live |
| `WallObstacleCollector.Collect` | Shared/WallObstacle.cs:37 | RouteService.cs:129 | live |
| `GeometryExtractor.GetBoundingBoxes` | Core/GeometryExtractor.cs:20 | AStarPathfinder.cs:333-336 (via AddBoxesSafe) | live |
| `RevitActionQueue.Enqueue` | RevitActionQueue.cs:35 | MainViewModel.cs:330, MainViewModel.Route.cs:43, MainViewModel.Constraints.cs:282,367 | live – sole bridge |
| `RouteFailureCollector.Attach` | RouteFailureCollector.cs:31 | RouteService.cs:189 | live |

## 3. Deployment evidence (live machine)

```
%AppData%\Autodesk\Revit\Addins\2025\MEPAutoRouting.addin   655 B  2026-09-28   MD5 == workspace manifest
%AppData%\Autodesk\Revit\Addins\2025\MEPAutoRouting.dll     206,848 B  2026-10-06 10:18   <- loaded by Revit (STALE Debug)
%AppData%\Autodesk\Revit\Addins\2025\MEPAutoRouting\MEPAutoRouting.dll  206,848 B  2026-10-06 18:16  <- never loaded (subfolder)
%AppData%\Autodesk\Revit\Addins\2024\MEPAutoRouting.addin   EXISTS   FullClassName=MEPAutoRouting.UnifiedRoutingCommand, absolute 2024 path, VendorId=MY
Workspace Release build after audit build: 180,224 B (bin\Release\net8.0-windows\MEPAutoRouting.dll)
```

## 4. Project/config evidence

- `MEPAutoRouting.csproj`: net8.0-windows L4, x64 L12-L13, REVIT2025 L17, NoWarn MSB3277 L18, Version 1.4.0 L22, RevitInstallDir default L23, Revit references L41-L50, Resource png L53-L54, DeployToRevit target L57-L66 (Debug → subfolder).
- `MEPAutoRouting.addin` L5-L13: single `AddIn Type="Command"`, relative Assembly L8, FullClassName L9. **No `AddIn Type="Application"`** anywhere.
- Build: `dotnet clean` + `dotnet build -c Release` → 0/0, 4.6 s.
- `.vscode/settings.json`: chat auto-approve entries only (no build config).
- `obj/expanded.csproj` + `*_wpftmp` files: generated build artifacts, not compiled sources.
