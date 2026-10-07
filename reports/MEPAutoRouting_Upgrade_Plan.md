# MEPAutoRouting – Upgrade Plan

Scope discipline: v4.7/v4.8 contain only correctness, reliability and diagnostics work. No AI/MCP/speculative features.

## Stage 0: Protect Current Working Baseline

| # | Objective | Files | Dependencies | Risk | Test method | Definition of done |
|---|---|---|---|---|---|---|
| 0.1 | Commit current state; tag `v4.6-baseline` | – | git | none | `git tag` | baseline restorable |
| 0.2 | Redeploy current Release build to the addins root; delete stale subfolder copy and 2024 manifest | Deploy-Revit2025.ps1 (verify only) | Revit closed | low | R-20 | loaded DLL == built DLL |
| 0.3 | Snapshot %AppData%\MEPAutoRouting\settings.json content | – | – | none | manual | known-good settings recorded |

## Stage 1: Critical Correctness Fixes (v4.7)

| # | Objective | Files likely affected | Dependencies | Risk | Test method | Definition of done |
|---|---|---|---|---|---|---|
| 1.1 | Register `IExternalApplication` so the ribbon button exists (ISS-001) | MEPAutoRouting.addin | none | low | R-20 | "MEP Tools → Auto Routing" button opens the window |
| 1.2 | One deployment location for DLL + manifest (ISS-003); keep 2024-manifest removal (ISS-009) | MEPAutoRouting.csproj (DeployToRevit), Deploy-Revit2025.ps1 | 1.1 | low | R-20 | Debug and Release deploys land where the manifest points; no stale DLLs |
| 1.3 | Split settings persistence: `settings.json` (UserSettings) vs `constraints.json` (RoutingOptions) (ISS-002) | Shared/RoutingOptions.cs, UI/ViewModels/UserSettings.cs | none | low | U-tests + manual restart | Topmost/discipline/strategy survive restart; slope/size survive |
| 1.4 | Re-validate after slope: `PathValidator.Validate(allowSlope:true)` + vertical-band check after `SlopeApplier.Apply` (ISS-004) | Routing/RouteService.cs, Shared/RouteProblem.cs | none | medium (may reveal new failures) | R-08, R-11, R-13 | sloped route that hits a wall/leaves the band is blocked with a clear Problem |
| 1.5 | Input guards: identical source/target connector error (ISS-014); add "no type selected" to `RouteBlockedReason` (ISS-016) | Routing/RouteService.cs, UI/ViewModels/MainViewModel.Constraints.cs | none | low | manual | explicit messages instead of "enclosed by walls" / "CanExecute returned false" |

## Stage 2: Transaction and Rollback Reliability

| # | Objective | Files | Dependencies | Risk | Test | DoD |
|---|---|---|---|---|---|---|
| 2.1 | Decide + document elbow-failure policy: keep segments (current, ISS-026) or roll back all; if keep, auto-select created elements (fixes ISS-015) | Routing/RouteService.cs, Routing/RouteBuilder.cs, MainViewModel | Stage 1 | low | R-16 | behaviour matches doc; PROBLEMS warning accurate |
| 2.2 | Log swallowed exceptions in GeometryExtractor/AddBoxesSafe to OUTPUT at Debug level | Core/GeometryExtractor.cs, Core/AStarPathfinder.cs | none | low | manual | no silent catch without diagnostics |
| 2.3 | Verify Document.IsModifiable / read-only handling before transaction start | Routing/RouteService.cs | none | low | manual (workshared model) | clear Problem instead of exception |

## Stage 3: Routing Quality (v4.8)

| # | Objective | Files | Dependencies | Risk | Test | DoD |
|---|---|---|---|---|---|---|
| 3.1 | Fix pick-request race: capture request in the queued closure (ISS-006) | Routing/RoutingEventHandler.cs, MainViewModel.cs | none | low | manual fast double-pick | first click executes first pick |
| 3.2 | Fix duct/cable-tray custom size parameters (ISS-007) | Routing/RouteBuilder.cs | none | low | manual per discipline | duct Ø/width/height set from input |
| 3.3 | Remove or wire dead UI: Strategy, Elevation Offset, SelectAfterRoute (ISS-008, ISS-015) | UI/MainWindow.xaml, MainViewModel*, RouteModels.cs | 2.1 | low | U + manual | every visible control has an effect or is gone |
| 3.4 | Preview invalidation on boundary/slope/size/type/system change; reset TotalFall on Clear (ISS-012, ISS-013) | MainViewModel*.cs | none | low | R-19 | no stale preview/summary |
| 3.5 | Obstacle coverage: floors + MEP equipment (host+link) (ISS-005); unify GetTotalTransform (ISS-017); document rotated-link boundary limit (ISS-018) | Core/GeometryExtractor.cs, Shared/WallObstacle.cs, Shared/SpaceVolume.cs | perf guard | medium | R-05, R-07 + new equipment test | no route through equipment; wall/door cases documented |
| 3.6 | Narrow-phase solid check for columns/framing candidates (ISS-011) | Core/AStarPathfinder.cs, Shared/SolidVolume.cs | 3.5 | medium | rotated-column model | false blockages removed, no perf regression |

## Stage 4: Performance and Diagnostics

| # | Objective | Files | Dependencies | Risk | Test | DoD |
|---|---|---|---|---|---|---|
| 4.1 | CancellationToken in A*/BFS + Cancel button (ISS-010) | Core/AStarPathfinder.cs, MainViewModel.Route.cs, MainWindow.xaml | none | medium | long search + cancel | cancel returns within 1 s, no partial state |
| 4.2 | Per-stage Stopwatch logging (walls, obstacles, BFS, A*, slope, build, commit) | Routing/RouteService.cs | none | low | any route | OUTPUT shows ms per stage |
| 4.3 | Echo full route inputs (ids, type, system, size, flow, boundary id/source) at route start | Routing/RouteService.cs | none | low | any route | audit §17 checklist satisfied |
| 4.4 | Cache obstacle extraction between the two A* attempts | Routing/RouteService.cs, Core/AStarPathfinder.cs | 3.5 | low | timing log | attempt 2 reuses collection |

## Stage 5: Automated Tests (ISS-023)

| # | Objective | Files | Dependencies | Risk | Test | DoD |
|---|---|---|---|---|---|---|
| 5.1 | New `MEPAutoRouting.Tests` project (net8.0, xUnit); pure-logic tests U-01…U-10 | new tests project; possibly extract XYZ-free math | none | low | `dotnet test` | green in CI/local |
| 5.2 | XYZ-adapter or Revit-referenced test harness for grid/A* logic (U-11, U-12) | tests | 5.1 | medium | `dotnet test` | grid round-trip tests green |
| 5.3 | Wire Tools/Check-Project.ps1 into a pre-release checklist | Tools/ | none | low | run script | dupes/CJK/entry-point checks green |

## Stage 6: Future Features (backlog, not committed)

- Door/opening-aware wall obstacles (R-05 pass).
- In-canvas route preview (DirectShape/transient graphics) mirroring the point list.
- Union/transition fittings; tee support for future branch routing.
- Multi-route batch / saved scenarios.
- Cantonese → English comment translation sweep (ISS-022); dead-code deletion (ISS-020); stale README updates (ISS-021) — fold into Stage 3/4 as hygiene tasks if capacity allows.

## Explicitly out of scope for v4.7/v4.8

- AI-assisted routing, MCP servers, cloud services, automatic clash resolution beyond obstacle avoidance.
- Large architectural rewrites (the current queue/service/builder split is sound).
