# MEP Auto Routing - Daily Handover Report

| Item | Value |
|---|---|
| Date | 2026-10-09 |
| Generated | 2026-10-09 10:39 HKT |
| Version | 4.8.3 |
| Branch | `main` |
| Latest tag | (no tag) |
| Latest commit | 66fd97f | handover-bot | 2026-10-09 02:28 | [handover] Daily report 2026-10-09 [skip ci] |
| Build | ❌ FAIL (Errors: - / Warnings: -) |
| Activity (last 168 h) | 12 commits, 110 files, +10148 / -7354 lines |

## 1. Status
In Development - v4.8.3 build stable (0 errors / 0 warnings)

## 2. Environment
- Revit 2025 (API: .NET 8, net8.0-windows)
- Visual Studio 2022 / VS Code
- Add-in manifest: MEPAutoRouting.addin
- Main DLL: MEPAutoRouting.dll
- Publisher: Cundall HK / Matthew Kwok

## 3. Commits (last 168 h)
| Hash | Author | Time | Message |
|---|---|---|---|
| `f2f4a11` | Matthew | 10-09 10:23 | Ignore build.log, bypass policy in tasks |
| `fd682ac` | Matthew | 10-09 10:23 | Routing/slope updates |
| `e046fcf` | Matthew | 10-09 10:21 | Add daily handover automation |
| `ae73027` | Matthew | 10-07 18:13 | 7/10 |
| `d582a5c` | Matthew | 10-07 17:30 | v4.8 (1.6.0): slope lead fix, elbow rollback, gravity validation, untrack bin/obj |
| `11d480e` | Matthew | 10-07 10:25 | v4.6 baseline + technical audit reports |
| `07e24a9` | Matthew | 10-05 18:29 | 5/10 |
| `ea55195` | Matthew | 10-05 18:23 | Remove unused code files, outdated docs, snippets and conflict backups |
| `e409e14` | Matthew | 10-05 18:20 | Before cleanup |
| `36eea5f` | Matthew | 10-05 10:34 | Merge branch 'main' of https://github.com/maczchung/RevitMEP-AutoRouting |
| `60553a3` | Matthew | 10-02 10:44 | 10/2 |
| `6dd4d98` | Matthew | 10-02 10:44 | 10/2 |

## 4. Files Changed
**Added (14)**
- `.github/workflows/daily-handover.yml`
- `.vscode/tasks.json`
- `VERSION`
- `docs/HANDOVER_NOTES.md`
- `scripts/Generate-Handover.ps1`
- `Tools/Resolve-MergeConflicts.ps1`
- `Shared/AppInfo.cs`
- `Shared/BoundaryPicker.cs`
- `Shared/Geom.cs`
- `Shared/IRoutingBoundary.cs`
- `Shared/MassVolume.cs`
- `Shared/Revit/RouteFailureCollector.cs`
- `Shared/SolidVolume.cs`
- `Tools/Check-Project.ps1`

**Modified (35)**
- `.gitignore`
- `MEPAutoRouting.csproj`
- `Routing/RouteService.cs`
- `Routing/RoutingEventHandler.cs`
- `Shared/ConnectorSelectionFilter.cs`
- `Shared/ConnectorUtils.cs`
- `Shared/PipeSizeCatalog.cs`
- `Shared/SlopeApplier.cs`
- `Shared/SlopeSettings.cs`
- `CHANGELOG.md`
- `Routing/RouteModels.cs`
- `UI/ViewModels/MainViewModel.Constraints.cs`
- `UI/ViewModels/MainViewModel.cs`
- `Core/AStarPathfinder.cs`
- `Core/GeometryExtractor.cs`
- `Deploy-Revit2025.ps1`
- `MEPAutoRouting.addin`
- `Routing/RouteBuilder.cs`
- `Shared/RoutingOptions.cs`
- `UI/MainWindow.xaml`
- `UI/ViewModels/UserSettings.cs`
- `Shared/ConnectorEndpoint.cs`
- `Shared/RouteProblem.cs`
- `UI/UnifiedRoutingCommand_Integration.cs.snippet`
- `UI/ViewModels/MainViewModel.Route.cs`
- `.vscode/settings.json`
- `Core/AutoRoutingCommand.cs`
- `Shared/Revit/RevitActionQueue.cs`
- `UI/MainWindow.xaml.cs`
- `README_BUILD.txt`
- `Shared/BoundingBoxUtils.cs`
- `Shared/PathUtils.cs`
- `Shared/RoutingConstraints.cs`
- `Shared/SpaceVolume.cs`
- `Shared/WallObstacle.cs`

**Deleted (61)**
- `bin/Debug/net8.0-windows/MEPAutoRouting.deps.json`
- `bin/Debug/net8.0-windows/MEPAutoRouting.dll`
- `bin/Debug/net8.0-windows/MEPAutoRouting.pdb`
- `gitignore`
- `obj/Debug/net8.0-windows/.NETCoreApp,Version=v8.0.AssemblyAttributes.cs`
- `obj/Debug/net8.0-windows/MEPAutoRouting.AssemblyInfo.cs`
- `obj/Debug/net8.0-windows/MEPAutoRouting.AssemblyInfoInputs.cache`
- `obj/Debug/net8.0-windows/MEPAutoRouting.GeneratedMSBuildEditorConfig.editorconfig`
- `obj/Debug/net8.0-windows/MEPAutoRouting.assets.cache`
- `obj/Debug/net8.0-windows/MEPAutoRouting.csproj.AssemblyReference.cache`
- `obj/Debug/net8.0-windows/MEPAutoRouting.csproj.CoreCompileInputs.cache`
- `obj/Debug/net8.0-windows/MEPAutoRouting.csproj.FileListAbsolute.txt`
- `obj/Debug/net8.0-windows/MEPAutoRouting.dll`
- `obj/Debug/net8.0-windows/MEPAutoRouting.g.resources`
- `obj/Debug/net8.0-windows/MEPAutoRouting.pdb`
- `obj/Debug/net8.0-windows/MEPAutoRouting.sourcelink.json`
- `obj/Debug/net8.0-windows/MEPAutoRouting_MarkupCompile.cache`
- `obj/Debug/net8.0-windows/ref/MEPAutoRouting.dll`
- `obj/Debug/net8.0-windows/refint/MEPAutoRouting.dll`
- `obj/MEPAutoRouting.csproj.nuget.dgspec.json`
- `obj/MEPAutoRouting.csproj.nuget.g.props`
- `obj/MEPAutoRouting.csproj.nuget.g.targets`
- `obj/project.assets.json`
- `obj/project.nuget.cache`
- `APPLY_FIXES.md`
- `APPLY_GUIDE.md`
- `APPLY_GUIDE.md.conflict.bak`
- `COPILOT_PROMPT.md`
- `COPILOT_PROMPT.md.conflict.bak`
- `Core/AutoRoutingCommand.cs.conflict.bak`
- `Core/RoutingUtils.cs`
- `Core/VoxelGrid.cs`
- `Electrical/ConduitCreator.cs`
- `Electrical/ConduitRoutingEngine.cs`
- `Electrical/ConduitUtils.cs`
- `Plumbing/PipeRoutingEngine.cs`
- `Reference/RouteService.reference.cs.txt`
- `Routing/RoutingMode.cs`
- `Shared/MepFittingUtils.cs`
- `Shared/PipeSizeCatalog.cs.conflict.bak`
- `Shared/Revit/RevitActionQueue.cs.conflict.bak`
- `Shared/SimplePath.cs`
- `Snippets/AutoRoutingCommand.cs.snippet`
- `Snippets/MainViewModel_RouteHooks.cs.snippet`
- `Snippets/MainViewModel_changes.cs.snippet`
- `Snippets/MainViewModel_hooks.cs.snippet`
- `Snippets/RouteService_Commit.cs.snippet`
- `Snippets/RouteService_EarlyLogs.cs.snippet`
- `Snippets/addin_publisher.snippet.xml`
- `Snippets/csproj_publisher.snippet.xml`
- `UI/MainWindow.xaml.cs.conflict.bak`
- `UI/ViewModels/MainViewModel.Constraints.cs.conflict.bak`
- `UI/ViewModels/MainViewModel.Route.cs.conflict.bak`
- `UI/ViewModels/MainViewModel.cs.conflict.bak`
- `Core/ConnectorUtils.cs`
- `Routing/UnifiedRoutingCommand.cs`
- `Routing/UnifiedRoutingUI.xaml`
- `Routing/UnifiedRoutingUI.xaml.cs`
- `Shared/SpacePicker.cs`
- `obj/Debug/net8.0-windows/Routing/UnifiedRoutingUI.baml`
- `obj/Debug/net8.0-windows/Routing/UnifiedRoutingUI.g.cs`

## 5. Build Result
- Result: ❌ FAIL
- Errors: -
- Warnings: -

```text
MSBUILD : error MSB1009: Project file does not exist.
```

## 6. Work Done Today (manual)
- (今日做咗咩，例如：Fixed boundary picker null reference)
-

## 7. Completed Features
- Modeless UI + RevitActionQueue (ExternalEvent)
- Route button event / preview route
- Pipe size input
- Slope input
- Calculation boundary: Manual pick / Room / Space / Mass
- Problems panel in English
- Publisher info

## 8. Test Results
| ID | Item | Status |
|---|---|---|
| R-01 | Command launch | PASS |
| R-02 | Modeless window | PASS |
| R-11 | Route button event | PASS |
| R-13 | Preview route | PASS |
| R-19 | Transaction handling | PASS |

## 9. Known Issues
- (例如：Wall avoidance fails when wall is curved)

### Open GitHub Issues
_No open issues._

## 10. TODO / FIXME in Code (4 found, showing up to 40)
```text
scripts/Generate-Handover.ps1:6:    Collects Git history, changed files, build result, TODO/FIXME markers,
scripts/Generate-Handover.ps1:120:# ---------- TODO / FIXME ----------
scripts/Generate-Handover.ps1:121:$todos = Invoke-Git @('grep','-n','-I','-E','(TODO|FIXME|HACK)','--','*.cs','*.xaml','*.ps1')
scripts/Generate-Handover.ps1:224:Add "## 10. TODO / FIXME in Code ($todoTotal found, showing up to $MaxTodos)"
```

## 11. Next Priority
1. Wall avoidance enhancement
2. Auto elbow creation / fitting placement
3. Route optimisation (shortest path, fewest fittings)
4. Performance on large models

## 12. Design Decisions
- All model changes go through RevitActionQueue (no direct API calls from WPF thread)

## 13. Recovery Prompt (copy into a new Copilot chat)
```text
Continue development of MEP Auto Routing (Revit add-in).
Assume previous chat history is lost. Use this handover as the source of truth.

Current version: 4.8.3
Branch: main | Latest commit: 66fd97f | handover-bot | 2026-10-09 02:28 | [handover] Daily report 2026-10-09 [skip ci]
Build status: failure (Errors: -, Warnings: -)

Environment:
- Revit 2025 (API: .NET 8, net8.0-windows)
- Visual Studio 2022 / VS Code
- Add-in manifest: MEPAutoRouting.addin
- Main DLL: MEPAutoRouting.dll
- Publisher: Cundall HK / Matthew Kwok

Completed features:
- Modeless UI + RevitActionQueue (ExternalEvent)
- Route button event / preview route
- Pipe size input
- Slope input
- Calculation boundary: Manual pick / Room / Space / Mass
- Problems panel in English
- Publisher info

Known issues:
- (例如：Wall avoidance fails when wall is curved)

Recent commits:
- Ignore build.log, bypass policy in tasks
- Routing/slope updates
- Add daily handover automation
- 7/10
- v4.8 (1.6.0): slope lead fix, elbow rollback, gravity validation, untrack bin/obj
- v4.6 baseline + technical audit reports
- 5/10
- Remove unused code files, outdated docs, snippets and conflict backups
- Before cleanup
- Merge branch 'main' of https://github.com/maczchung/RevitMEP-AutoRouting

Next priority:
1. Wall avoidance enhancement
2. Auto elbow creation / fitting placement
3. Route optimisation (shortest path, fewest fittings)
4. Performance on large models

Continue from the latest stable state. Ask me to paste specific source files if needed.
```

---
_Auto-generated by scripts/Generate-Handover.ps1 - Cundall HK / Matthew Kwok_
