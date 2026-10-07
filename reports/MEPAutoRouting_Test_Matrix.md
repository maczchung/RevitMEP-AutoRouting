# MEPAutoRouting – Test Matrix

Status legend: **Unit** = Revit-independent automated test (proposed) · **Revit** = manual/in-Revit test required.
Every Revit test assumes: ribbon button visible (after ISS-001 fix), current Release DLL deployed (after ISS-003 fix).

## Automated (Revit-independent) candidates

| Test ID | Priority | Target logic | Setup | Expected | Pass criteria |
|---|---|---|---|---|---|
| U-01 | High | `SlopeSettings.TryParse` 1:40 | unit=Ratio, "40" | value 40, no error | Gradient = 0.025 |
| U-02 | High | `SlopeSettings.TryParse` 2.5 % | unit=Percent, "2.5" | value 2.5 | Gradient = 0.025; rejects 0 / 21 % |
| U-03 | High | `SlopeSettings.TryParse` bounds | "4" / "1001" ratio, "abc" | error strings, English | TryParse=false |
| U-04 | Med | `Geom`/`UnitConv` mm↔ft | 1000 mm | 3.28084 ft round-trip | |a−b| < 1e-9 |
| U-05 | Med | `PathUtils.RemoveDuplicates` / `MergeCollinear` | dup + collinear points | duplicates dropped; collinear merged; U-turn point kept | exact point lists |
| U-06 | Med | `PathValidator` zero-length & U-turn | crafted paths | Error entries with point index | severity + index correct |
| U-07 | Med | `PipeSizeCatalog.Snap` | sizes {32,40,50}, input 44 | 40 mm, exact=false | snap result |
| U-08 | Med | `RoutingConstraints.IsBlocked` | wall at x=0, clearance 50, r=25 | blocked within 75+halfwidth, free outside | bool matrix |
| U-09 | Med | `SlopeApplier.Apply` run anchoring | L-shaped path, 1:40 | endpoints fixed; middle run tilted; total fall = run×g | z values ±1e-6 |
| U-10 | Med | `SlopeApplier` vertical reversal | vertical segment too short after slope | Error "Vertical segment … min 50 mm" | problem recorded |
| U-11 | Low | `AStarPathfinder` grid math (via XYZ shim) | known spans | `AlignedStep` divides span exactly; indices round-trip | ±1 mm |
| U-12 | Low | `MinHeap` ordering | random priorities | ascending pops | sequence sorted |

## Revit manual / integration tests

| Test ID | Priority | Preconditions | Revit model setup | User steps | Expected OUTPUT | Expected PROBLEMS | Expected preview | Expected model changes | Pass criteria | Cleanup |
|---|---|---|---|---|---|---|---|---|---|---|
| R-01 Straight unobstructed | High | Pipe discipline, type+system+level loaded | 2 open pipe connectors in one room, same height, no walls between | Pick source, pick target, F5, Ctrl+Enter | start/target sizes, region, "Running A*", "Created N segments" | none | point list matches path | pipes + 0 elbows, ends connected | One undo removes all | Undo |
| R-02 Single-wall detour | High | as R-01 | one full-height wall between connectors | same | wall count ≥1, id listed | none | path goes around/over within level band | elbows at turns | No segment intersects wall+clearance | Undo |
| R-03 Enclosed source | High | as R-01 | source inside 4-wall room, no door | Preview | "source enclosed by walls" | 1 Error "source enclosed" | empty | none | No transaction started | n/a |
| R-04 Enclosed target | High | as R-03, swapped | target enclosed | Preview | "target enclosed" | 1 Error "target enclosed" | empty | none | BFS reported correct side | n/a |
| R-05 Door opening in wall | Med | room with door opening between connectors | wall with door | Preview | path via opening | none | path threads opening | – | **KNOWN GAP**: openings not carved from wall obstacle → likely false "enclosed" (ISS-005 family) | n/a |
| R-06 Opening outside initial region | Med | door reachable only beyond first region | as R-05, offset | Preview | retry log "expanded region" | none or false enclosed | – | – | Retry keeps Z limits (verify Z in OUTPUT) | n/a |
| R-07 Linked-model wall | High | link loaded, "Include linked model walls" on | wall only in link | Preview | "1 host, 1 linked" wall counts | none | detour around linked wall | – | Linked wall respected (transform check) | n/a |
| R-08 Minimum vertical limit | High | connectors near floor level | level elevation known | Preview | "Vertical limits: Z …" logged | none | no point below level+radius+clearance | – | All Z ≥ zMin − 1 mm | n/a |
| R-09 Maximum vertical limit | Med | level above exists | connectors high | Preview | zMax = level above − radius | none | no point above zMax | – | All Z ≤ zMax + 1 mm | n/a |
| R-10 Different elevations | Med | source 500 mm above target | – | Preview | vertical segment present | none | orthogonal riser | – | No diagonal segments | Undo |
| R-11 Slope 1:40 | High | sanitary system, slope auto-on | route with one horizontal run | Preview | Slope "1:40 (2.5 %)", total fall = run/40 | none | tilted run | – | Fall matches ±1 mm; **post-slope wall check after ISS-004 fix** | n/a |
| R-12 Slope 2.5 % | Med | as R-11, unit % | – | enter 2.5 % | equivalent 1:40 shown | none | as R-11 | – | Unit conversion equal | n/a |
| R-13 Insufficient fall | Med | fully horizontal route, both ends fixed | – | Preview | – | Error "no vertical segment to take up the fall" | – | none | Route blocked before commit | n/a |
| R-14 Short-jog removal | Med | connectors offset 52 mm in X | – | Preview | "lead changed … to remove a 52 mm offset" | warn only if unfixable | no 52 mm jog | – | No segment < min jog unless warned | Undo |
| R-15 Zero-length prevention | Med | coaxial connectors | – | Preview | – | no zero-length errors | clean list | – | RemoveDuplicates effective | n/a |
| R-16 Elbow failure rollback policy | High | type without usable elbow family | 90° route | Route | "Elbow 1 failed …" | Warn "1 elbow(s) could not be created" | – | segments committed, no elbow (documented partial state, ISS-026) | Warning present; decide keep-vs-rollback | Undo |
| R-17 Cancel source pick | Med | – | – | Pick source → Esc | "Cancelled by user." | none | unchanged | none | IsBusy reset; Route still blocked | n/a |
| R-18 Cancel boundary pick | Med | – | – | Pick boundary → Esc | "Boundary pick cancelled." | none | unchanged | none | Boundary stays null | n/a |
| R-19 Stale preview clearing | Med | one successful preview | – | change slope/size/boundary, observe | – | – | **currently stale** (ISS-012); after fix: cleared | none | Preview empty after parameter change | n/a |
| R-20 Deployment integrity | High | build Debug, run Revit | – | open add-in | – | – | – | – | Loaded DLL == latest build (after ISS-003 fix); no 2024 manifest; ribbon button present (after ISS-001) | n/a |

## Cross-cutting checks per Revit test

- OUTPUT contains: source/target element+connector, discipline/type/system/size, slope+flow, boundary id+source, wall ids, grid info, vertical min/max, BFS result, A* iterations, raw/simplified counts, transaction result. (Missing items tracked in audit §17.)
- PROBLEMS empty or every entry English + actionable.
- Exactly one undo step per Route.
- Window close → reopen: no duplicate logs, queue recreated, settings per ISS-002 expectations.
