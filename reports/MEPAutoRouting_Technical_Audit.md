# MEPAutoRouting Technical Audit

Audit date: 2026-10-07 · Target: Revit 2025 / net8.0-windows · Method: full source read + Release build + deployment inspection. No production code was modified.

## 1. Executive Summary

- **Build status**: Release build **succeeds** – 0 errors, 0 warnings, ~4.6 s. Output: `bin\Release\net8.0-windows\MEPAutoRouting.dll` (see [MEPAutoRouting_Build_Log.txt](MEPAutoRouting_Build_Log.txt)).
- **Overall readiness**: The core route pipeline (modeless UI → `RevitActionQueue` → `RouteService` → A*+BFS → validation → slope → single-transaction build) is coherent and mostly implemented. The add-in is **not deployable end-to-end** as shipped: the ribbon application is not registered, the deployed DLL is stale, and user settings do not persist.
- **Genuinely works** (confirmed by source trace; runtime in Revit unverified):
  - Modeless window, single instance, owned by Revit main window ([Core/AutoRoutingCommand.cs](../Core/AutoRoutingCommand.cs#L18)).
  - All Revit API work from the UI funnels through one `ExternalEvent` queue ([Shared/Revit/RevitActionQueue.cs](../Shared/Revit/RevitActionQueue.cs#L13)).
  - Six-direction A* on a per-axis-aligned grid with BFS enclosed-source/target pre-check ([Core/AStarPathfinder.cs](../Core/AStarPathfinder.cs#L84)).
  - Wall clearance via centre-line distance (slanted/curved walls handled), host+linked walls ([Shared/WallObstacle.cs](../Shared/WallObstacle.cs#L37)).
  - Vertical level band enforced identically in BFS and A* ([Core/AStarPathfinder.cs](../Core/AStarPathfinder.cs#L430-L434)).
  - Manual boundary pick (Space/Room/Mass, host or linked) ([Shared/BoundaryPicker.cs](../Shared/BoundaryPicker.cs#L14)).
  - Slope parsing (1:N / %), gravity auto-enable, total fall, flow direction ([Shared/SlopeSettings.cs](../Shared/SlopeSettings.cs), [Shared/SlopeApplier.cs](../Shared/SlopeApplier.cs#L18)).
  - Failures return to the UI as `RouteProblem`s; **zero** `TaskDialog.Show` calls in the solution.
  - Pipe/Duct/Conduit/CableTray creation + auto elbows + end connection, one transaction, rollback on error ([Routing/RouteService.cs](../Routing/RouteService.cs#L186), [Routing/RouteBuilder.cs](../Routing/RouteBuilder.cs#L15)).
- **Unverified without Revit**: everything that touches the live document (picks, creation, elbows, linked geometry, level band against real models).
- **Top five risks**:
  1. No ribbon entry point – `App` (`IExternalApplication`) missing from the manifest (ISS-001).
  2. `UserSettings` and `RoutingOptions` share one JSON file – user settings lost every session (ISS-002).
  3. Deployed root DLL is stale; Debug deploys land in a subfolder the manifest does not reference (ISS-003).
  4. Slope is applied after validation and never collision/vertical re-checked (ISS-004).
  5. Obstacle model ignores floors, equipment and all MEP elements – routes can pass through them (ISS-005).
- **Recommended next release objective (v4.7)**: make the add-in reachable and trustworthy – register the application, fix deployment + settings persistence, re-validate after slope, add same-connector guard, wire or remove dead UI controls.

## 2. Build and Environment

| Item | Value | Evidence |
|---|---|---|
| Build command | `dotnet build .\MEPAutoRouting.csproj -c Release` | no .sln exists |
| Result | Success, 0 errors / 0 warnings, 4.6 s | build log |
| Target | net8.0-windows, x64-only, WPF, `REVIT2025` define, LangVersion latest, Nullable disable | [MEPAutoRouting.csproj](../MEPAutoRouting.csproj#L4-L14) |
| Assembly | MEPAutoRouting 1.4.0 → `bin\Release\net8.0-windows\MEPAutoRouting.dll` | csproj L7-L8, L22 |
| References | RevitAPI.dll + RevitAPIUI.dll only, `Private=false`, HintPath `$(RevitInstallDir)` (default `C:\Program Files\Autodesk\Revit 2025`, verified present) | csproj L41-L50 |
| NuGet | none | csproj |
| MSB3277 | suppressed via `<NoWarn>`; no conflict emitted in this build | csproj L18 |
| Revit 2024/2025 mixing | none in project; **legacy 2024 manifest still deployed** | ISS-009 |

## 3. Project Inventory

Full tree: [MEPAutoRouting_File_Inventory.txt](MEPAutoRouting_File_Inventory.txt). Highlights:

- 45 `.cs` files, all compiled (SDK glob); `UI\*.snippet` files are **not** compiled.
- No solution file, no test projects, no backup/duplicate compiled sources.
- Stray `gitignore` (no dot) – git never reads it.
- README_BUILD.txt is stale (names `MEPAutoRouting.UnifiedRoutingCommand`); README_CONNECTOR_ROUTING.txt is legacy (net48/2024).
- `Core/RoutingVolumeUtils.cs`, `Shared/WarningSwallower.cs`, most of `Routing/RoutePlanner.cs`, `MainViewModel.SetPreview`, `MainViewModel.BuildOptions`, `SlopeApplier.TotalFall` are **dead code**.

## 4. Add-in Entry Point

| Field | Value | Check |
|---|---|---|
| Text | "Unified Routing" | OK |
| FullClassName | `MEPAutoRouting.Core.AutoRoutingCommand` | **matches** the only `IExternalCommand` ([Core/AutoRoutingCommand.cs](../Core/AutoRoutingCommand.cs#L14)) |
| AddInId | 8D83C886-B739-4ACD-A9DB-3F85F2D1A111 | stable GUID |
| Assembly | `MEPAutoRouting.dll` (relative → addins root) | matches Deploy-Revit2025.ps1; **mismatches** csproj Debug auto-deploy subfolder (ISS-003) |
| VendorId / VendorDescription | CUNDALL.HK / Cundall HK · Matthew Kwok | consistent with `AppInfo` |
| Application entry | **MISSING** – `App` never registered → ribbon never created (ISS-001) | Critical |
| Deployed manifest | byte-identical to workspace (MD5 match) | OK |
| Revit 2024 | legacy manifest still present with `MEPAutoRouting.UnifiedRoutingCommand` | ISS-009 |

Startup trace: `.addin → AutoRoutingCommand.Execute → RevitActionQueue.Create (ExternalEvent) → MainWindow/MainViewModel → ExecuteRoute → RevitActionQueue → RouteService.Run → AStarPathfinder → RouteBuilder → Transaction`. Exactly **one** active route workflow; legacy entry points exist only in non-compiled snippets.

## 5. Actual Architecture

See [MEPAutoRouting_Architecture.md](MEPAutoRouting_Architecture.md) (component + sequence + pipeline diagrams). MVVM is respected: code-behind only handles window chrome, shortcuts, scrolling and command-commit plumbing ([UI/MainWindow.xaml.cs](../UI/MainWindow.xaml.cs#L113-L130)).

## 6. Revit API Context Safety

| Check | Result | Evidence |
|---|---|---|
| Single bridge | All UI-initiated Revit work via `RevitActionQueue.Enqueue` | [MainViewModel.Route.cs](../UI/ViewModels/MainViewModel.Route.cs#L43), [MainViewModel.Constraints.cs](../UI/ViewModels/MainViewModel.Constraints.cs#L282-L298) |
| ExternalEvent lifecycle | Created in command; disposed on window close; log unsubscribed | AutoRoutingCommand.cs L31,L52-L58 |
| `Task.Run` / `async void` / timers | none | workspace search |
| Exceptions inside event | caught per action → error.log + OUTPUT; never leave `Execute` | RevitActionQueue.cs L45-L53 |
| Reopen window | fresh queue/handler/VM per open; old one disposed | AutoRoutingCommand.cs |
| Issues | (a) `RoutingEventHandler.Request` shared-field race (ISS-006); (b) VM scalar props set on Revit thread (ISS-019); (c) duplicate submissions while busy are guarded by `IsBusy` on the UI side only | |

## 7. UI and MVVM

- DataContext set in code-behind; all bindings target `MainViewModel` properties; no binding errors expected (names verified against VM).
- **Both Route buttons** (title bar + Routing Options footer) and **Ctrl+Enter** call `RunCommand(_vm.RouteCommand)`; **F5** → Preview; `CommitFocusedInput` commits LostFocus TextBoxes first ([UI/MainWindow.xaml.cs](../UI/MainWindow.xaml.cs#L107-L130)).
- Problems badge, Problems list, Summary "Problems" and status-bar count all bind to the same `Problems` collection. Error = ⛔ red, Warning = ⚠ yellow ([UI/MainWindow.xaml](../UI/MainWindow.xaml) ProblemLine template).
- Route guard: `RouteBlockedReason` covers source/target/busy/size; **not** "no type selected" (ISS-016).
- Slope defaults not overwritten at startup (guarded `_lastSystemTypeId`) – verified (ISS-029).
- Dead controls: **Strategy** combo + **Elevation Offset** textbox affect nothing (ISS-008); **SelectAfterRoute** setting unused (ISS-015); "Include linked model walls" is always enabled (no compatible-type gating exists – informational).
- Stale state: preview survives boundary/slope/size/type changes (ISS-012); TotalFallText survives Clear (ISS-013).
- Window cleanup: queue disposed, collection handler unsubscribed; `vm.PropertyChanged` subscription is not removed (harmless, co-collected).
- Window position/size are **not** persisted.

## 8. Routing Pipeline

Step-by-step table (all steps confirmed reached in code):

| # | Step | File:Method | Line | Failure behaviour |
|---|---|---|---|---|
| 1 | Input validation | RouteService.Run | L40-L58 | Error → return, nothing created |
| 2/3 | Source/target leads | ConnectorEndpoint.From + AlignLeads | RouteService L64-L76 | Warn on unfixable micro-offset |
| 4/5 | Region + boundary | RouteService.Run | L81-L99 | Error if lead outside boundary |
| 6/7 | Geometry + obstacles | AStarPathfinder.GetFilteredObstacles | L324 | per-category try/catch (silent) |
| 8/9 | Walls (host+link) | WallObstacleCollector.Collect | L37 | links skipped if unloaded |
| 10 | Clearance expansion | RoutingConstraints.RequiredDistance | L36 | – |
| 11/12 | Grid + vertical limits | SetupAlignedGrid / SetVerticalLimits | L246 / L58 | nodes outside band blocked |
| 13 | Node conversion | IndexOf/NodeCenter | L255-L260 | – |
| 14 | Exempt corridors | RoutingConstraints.AddExemptCorridor | L33-L34 | origin→lead only |
| 15 | BFS reachability | BfsReachable | L263 | cap = inconclusive → A* decides |
| 16-18 | A* + goal + retrace | FindPath | L160-L205 | LastFailure + empty list |
| 19 | Simplify | SimplifyCollinearPoints + MergeCollinear | L466 | – |
| 20 | Slope | SlopeApplier.Apply | RouteService L167 | **no re-validation (ISS-004)** |
| 21 | Preview | same path object → UI list | ApplyRouteResult | UI-only preview |
| 22/23 | Segments + fittings | RouteBuilder.Build | L15 | per-elbow catch |
| 24 | Commit/rollback | Transaction | RouteService L186-L231 | RollBack on exception/Revit errors |
| 25 | UI report | ApplyRouteResult + LogOutcome | MainViewModel.Route.cs L87 | PROBLEMS + OUTPUT + status |

Retry: one retry with XY-expanded region; **Z stays clamped to `zLimitMin/Max`** (RouteService L117-L125) – correct.

## 9. A* and BFS

Verified correct: per-axis aligned step so leads fall on nodes ([Core/AStarPathfinder.cs](../Core/AStarPathfinder.cs#L232-L250)); string node ids `x,y,z` unique per cell; binary MinHeap with lazy duplicate handling; closed set; Manhattan heuristic with Z×3 weight (consistent with 600 ft Z-move penalty dominance... note heuristic×3 on Z is **over**-weighted vs. actual Z cost `_gz + 600` only when _gz small – heuristic is not admissible but bounded; acceptable); parent tracking; goal = exact end index; six directions; first-move/goal-entry direction rules; 400k iteration cap; failure reasons (`OutsideRoutingVolume`, `SourceEnclosed`, `TargetEnclosed`, `NoPath`, `Exception`) with diagnostics.

Gaps: **no cancellation** (ISS-010); BFS duplicates A*'s passability work (same `CheckIfObstacle`, incl. vertical limits – good) but not the first-move rule (ISS-027, informational); nodes are never reopened after closing (fine with this heuristic); BFS runs over the whole reachable grid before A* – duplicated effort (perf note).

Unit safety: all internal computations in feet; mm↔ft via `UnitUtils` in `Geom`/`UnitConv`; grid tolerance 1 mm; `GridSize` 0.5 ft ≈ 152 mm. No feet/mm mixups found.

## 10. Obstacles and Linked Models

- Obstacles = walls (curve-based, host+link via `GetTotalTransform`) + columns/structural columns/framing (AABB, host+link via `GetTransform` – inconsistent, ISS-017).
- **Not obstacles**: floors, ceilings, roofs, MEP equipment/fixtures, pipes/ducts/conduits/cable trays/fittings, generic models, openings/doors in walls (door openings are **not** carved out of wall obstacles – a wall with a door blocks the whole wall length: false blockage / no-path risk, and conversely no "missed collision" because walls use distance-to-centre-line, which is conservative).
- Linked bounding boxes: transformed via 8-corner transform (correct AABB of transformed box).
- Walls: `HalfWidth = Width/2`, Z band from bounding box, distance via `LocationCurve` (Line fast path; `Project` fallback for curves) – slanted/curved walls handled; **wall base/top respected** via `NearBox` Z check.
- Bounding-box-only columns/framing can cause false blockage (ISS-011); equipment non-collision can cause routing **through** equipment (ISS-005).

## 11. Vertical Limits

Implementation (`RouteService.ClampVertical`, [Routing/RouteService.cs](../Routing/RouteService.cs#L243-L275)):

- `zMin = reference level + pipe radius + clearance`, never below the level (`Math.Max(zMin, levelZ)`); above-level connectors kept inside the band.
- `zMax = min(region max, level above − radius)` (or region max if no level above).
- Hard limits `zLimitMin/Max` are captured **before** retry expansion; expansion clamps Z (L117-L125). BFS and A* share the check inside `CheckIfObstacle` (L430-L434) – the level band cannot be escaped below walls or below the level.
- Without a valid level, the band falls back to connector Z ± 1000 mm.
- **Residual risks**: (a) sloped path can leave the band because slope is applied after validation and never re-checked (ISS-004); (b) `PathValidator`'s wall re-check does not include the vertical band (constraints only); (c) preview and final pipe use the **same** path object, so no divergence there.
- Conclusion for the known critical failure (escape below the walls/level): **blocked** in A*/BFS for the horizontal route; **not re-verified after slope**.

## 12. Path Simplification

- `RetracePath` + `RemoveDuplicates` (1e-6 ft) → `SimplifyCollinearPoints` merges on major-axis change → `PathUtils.MergeCollinear` again after adding connector origins.
- `AlignLeads` absorbs offsets < max(100 mm, 6×radius) into lead lengths, otherwise warns ("the route needs a short jog…") – this is the 52 mm-jog mitigation ([Routing/RouteService.cs](../Routing/RouteService.cs#L279-L314)).
- `PathValidator` flags zero-length (Error), non-axis-aligned (Error), short segments (Warning, leads exempt), U-turns (Error, dot < −0.999).
- Gaps: no minimum-segment **enforcement** (warning only), no self-intersection check, no clearance re-validation **after** slope (wall re-sampling exists in Validate, but runs before slope). Diagonal first/last segments prevented by direction rules + aligned grid.

## 13. Slope

- Parsing: `SlopeSettings.TryParse` – invariant culture; 1:N ∈ [5,1000]; % ∈ (0,20]; `Gradient` conversion both ways; unit switch preserves gradient ([Shared/SlopeSettings.cs](../Shared/SlopeSettings.cs#L30-L42)).
- Gravity auto-enable on system-type change (Sanitary or name match) – [MainViewModel.Constraints.cs](../UI/ViewModels/MainViewModel.Constraints.cs#L163-L177); pressurized → slope off.
- `SlopeApplier.Apply`: endpoints fixed; horizontal runs tilted; last run anchored at target (flow Target→Source handled by reversal); vertical segments must keep direction and ≥ MinVerticalMm (50 mm default) else Error; upward vertical in flow direction → Warning.
- **Timing: applied after pathfinding and after validation, before commit.** No collision/vertical-limit re-check afterwards (ISS-004). Preview and final model use the same sloped path (consistent with each other, both unchecked).
- Total fall = |z0−zn| shown in Summary.
- Revit pipe slope compatibility: sloped segments are created as straight `Pipe.Create` between tilted endpoints – no check that the pipe type/system supports slope, no slope parameter set.

## 14. Segment and Fitting Creation

| Discipline | Creation | Custom size | Status |
|---|---|---|---|
| Pipe | `Pipe.Create` | RBS_PIPE_DIAMETER_PARAM | implemented |
| Duct | `Duct.Create` | **bug – uses pipe parameter (ISS-007)** | implemented with defect |
| Conduit | `Conduit.Create` | RBS_CONDUIT_DIAMETER_PARAM | implemented |
| CableTray | `CableTray.Create` | **bug – pipe parameter; W/H never set (ISS-007)** | implemented with defect |

- `doc.Regenerate()` once after all segments, then elbow connector lookup by proximity (`ConnectorUtils.GetNearestConnector`) – correct order.
- Elbows: `doc.Create.NewElbowFitting(c1, c2)`; failures logged with position, turn angle, adjacent segment lengths; counted → Warning in PROBLEMS. Straight joins (<0.5°) skipped.
- Ends: `ConnectTo` equipment connector; skipped with warning if already connected.
- No union/tee/transition creation. **Partial creation is possible**: elbow failure leaves segments committed (by design, ISS-026).

## 15. Transactions and Rollback

- One `Transaction` ("MEP Auto Route"), `RouteFailureCollector` attached (`ProceedWithRollBack` on Revit errors, optional warning deletion), `SetClearAfterRollback(true)` – [Routing/RouteService.cs](../Routing/RouteService.cs#L186-L231).
- Rollback on: exception (catch → `RollBack`), Revit errors, non-committed status. Nothing is created when pre-checks fail (return before `Start`).
- User cancellation happens before the transaction (picks) – no rollback needed.
- Empty/silent catches that hide information: [Core/GeometryExtractor.cs](../Core/GeometryExtractor.cs#L53,L100) (category extraction), [Core/AStarPathfinder.cs](../Core/AStarPathfinder.cs#L378) (`AddBoxesSafe`), settings load/save catches (acceptable), `ConnectorInfo.From` size catch (acceptable).

## 16. Preview State

- Preview = point list in the Path Preview tab; **no in-canvas preview geometry** (no DirectShape anywhere).
- Cleared: at route start, on failure (empty result), on connector change/swap/clear, on discipline change (via connector reset).
- **Not cleared**: boundary, slope, size, type, system-type changes (ISS-012).
- Preview and creation use the same `result.Path` – consistent.

## 17. Problems and Diagnostics

- All user-facing strings verified English (CJK regex over string literals: 0 hits). Code **comments** are extensively Cantonese (ISS-022).
- `TaskDialog.Show`: **0 occurrences** (one comment mention only). No modal routing errors remain.
- Diagnostics already implemented: route started/queued, source/target sizes+lead, region size, wall count host/linked + first 40 wall ids, vertical limits with level elevation, A* point count + grid step + blocked/total nodes, BFS reachable counts, path/preview counts, segment/elbow counts, transaction status, per-elbow failure detail.
- Partially: obstacle count by category (only columns/framing aggregate), grid origin (not logged), vertical min/max (logged), elapsed time per stage (**missing**).
- Missing: source/target element+connector ids in route log, selected discipline/type/system/size/flow/boundary-id echo at route start, heap/cell size (implied), link names.

## 18. Performance

- BFS full-grid + A* re-walk (2× passability work); `IsSegmentBlocked` samples every ≈38 mm per neighbour; `CheckIfObstacle` is O(walls) per sample with `NearBox` broad phase – acceptable for room-scale regions, heavy for large ones.
- 400k-iteration cap bounds worst case; no cancellation → Revit UI frozen during long searches (ISS-010).
- Obstacles re-collected on the retry attempt (2× extraction per route).
- `SolidVolume` ray-cache exists but is only used for Mass boundaries.
- UI log is capped at 500 entries; auto-scroll throttled – good.
- Estimated complexity: nodes ≈ region/0.5 ft per axis; 20 m × 20 m × 3 m region ≈ 131×131×20 ≈ 343k potential nodes – near the cap.
- Suggested measurement points (not implemented): wall collection, obstacle extraction, BFS, A*, slope, build – log `Stopwatch` per stage.

## 19. Settings

- Location: `%AppData%\MEPAutoRouting\settings.json`, `System.Text.Json`, tolerant load (corrupt → defaults), culture-invariant parsing, mm stored.
- **Defect**: `UserSettings` and `RoutingOptions` share the same file; save order guarantees UserSettings loss (ISS-002). Slope/size/clearance (RoutingOptions) do persist; discipline/strategy/lead/topmost (UserSettings) do not.
- No schema version/migration; window geometry not persisted; boundary not persisted (per-session object – acceptable).

## 20. Tests

- None. No test project or test file exists. See [MEPAutoRouting_Test_Matrix.md](MEPAutoRouting_Test_Matrix.md) for the proposed matrix.
- Revit-independent testable units: `SlopeSettings.TryParse`, `SlopeApplier` (needs XYZ – Revit.DB-dependent; consider an adapter), `PathUtils`, `RoutePlanner.DirectionLabel`, `Geom`, `PipeSizeCatalog.Snap`, `RoutingConstraints`/`WallObstacle` geometry (XYZ-dependent), grid math inside `AStarPathfinder` (XYZ-dependent). Practical approach: extract a small `Vec3` abstraction or reference RevitAPI types in tests with `XYZ` shims; alternatively test via `RevitTestFramework`-style in-Revit tests.

## 21. Dead and Legacy Code

| Item | Kind | Evidence |
|---|---|---|
| RoutePlanner.Plan/Simplify/Validate | legacy (DirectionLabel only live) | [Routing/RoutePlanner.cs](../Routing/RoutePlanner.cs#L16-L55) – no callers |
| RoutingVolumeUtils | dead | no callers (workspace search) |
| WarningSwallower | dead | superseded by RouteFailureCollector |
| MainViewModel.SetPreview / BuildOptions | dead | definitions only |
| SlopeApplier.TotalFall | dead | VM computes its own |
| UI/*.snippet (4 files) | legacy reference sketches | not compiled |
| README_CONNECTOR_ROUTING.txt, README_BUILD.txt | stale docs | net48/2024, UnifiedRoutingCommand |
| gitignore (no dot) | stray | git ignores it |
| Strategy / Elevation UI controls | UI-only | ISS-008 |

## 22. Risk Register

Full register with 30 items: [MEPAutoRouting_Issues.csv](MEPAutoRouting_Issues.csv). Counts: **Critical 1 · High 4 · Medium 6 · Low 11 · Info 8**.

## 23. Recommended v4.7 Scope

1. Register `App` in the manifest (ISS-001) – restore the ribbon button.
2. Align deployment: single DLL location; remove stale root DLL + legacy 2024 manifest (ISS-003, ISS-009).
3. Split settings files (ISS-002).
4. Re-validate path after slope (walls + vertical band, `allowSlope:true`) (ISS-004).
5. Add same-connector guard + `SelectedType` to `RouteBlockedReason` (ISS-014, ISS-016).

## 24. Recommended v4.8 Scope

1. Fix pick-request race (ISS-006) and duct/cable-tray sizing (ISS-007).
2. Remove or wire Strategy/Elevation/SelectAfterRoute (ISS-008, ISS-015); preview clearing on parameter change (ISS-012, ISS-013).
3. Obstacle coverage: floors + equipment (ISS-005); narrow-phase solid checks (ISS-011); unify `GetTotalTransform` (ISS-017).
4. Cancellation + per-stage timing (ISS-010, perf).
5. First automated tests for slope/unit/grid math (ISS-023); dead-code cleanup (ISS-020); comment translation (ISS-022).

## 25. Evidence Appendix

Search evidence (queries + hits): [MEPAutoRouting_Search_Evidence.md](MEPAutoRouting_Search_Evidence.md). Build output: [MEPAutoRouting_Build_Log.txt](MEPAutoRouting_Build_Log.txt). File inventory: [MEPAutoRouting_File_Inventory.txt](MEPAutoRouting_File_Inventory.txt). All line numbers verified against the workspace on 2026-10-07.
