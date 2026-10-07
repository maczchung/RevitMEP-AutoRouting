# Changelog

## v4.8.1 (1.6.1)

- **Task 1** – Slope silently not applied (R-13): `SlopeApplier` now reports the actually applied fall and compares it with the required fall (interior horizontal run × gradient). A flat gravity route raises Error "Slope cannot be applied: no vertical segment to absorb {fall} mm fall." (commit blocked); a non-gravity flat route raises Warning "Slope 1:{N} was not applied – route is flat (no vertical segment)." Summary "Total fall" shows the applied fall (0 mm in this case).
- **Task 2** – "Match source connector" size is recomputed whenever the source connector changes, the checkbox is toggled, or the segment type / size catalog changes; route start logs "Pipe size: {x} mm (matched source Ø{y}, snapped to type catalog)".
- **Task 3** – Size-mismatch warning now fires for every route (matched and explicit sizes), comparing the final route diameter with each round connector's nominal diameter at 0.5 mm tolerance; warnings are reported even when an error is also present.

## v4.8 (1.6.0)

- **Task 1** – Slope no longer tilts the connector leads: the first segment (origin → lead) and last segment (lead → origin) stay parallel to the connector axis; only interior horizontal runs take the gradient; a single horizontal run yields total fall 0 with no error.
- **Task 2 / ISS-026** – Any elbow failure rolls the whole route back ("Route not created: {n} elbow(s) failed. See OUTPUT for details."); per-elbow OUTPUT detail (index, position, angle to 2 decimals, both segment lengths, size, pipe type's routing-preference elbow) plus a pre-commit elbow-angle precheck log.
- **Task 3** – Sloped-to-vertical turns are identified and logged with the actual angle; elbow failure detail includes the angle and the routing-preference elbow family name.
- **Task 4** – Gravity systems (Sanitary/Storm/name match): a vertical segment rising in the flow direction is now an Error ("…gravity drainage cannot flow uphill."), excluding the fixed end lead when the target connector faces downward; slope on a pressurised system adds a Warning.
- **Task 5** – Size-mismatch warnings: "Route size {x} mm differs from source/target connector Ø{y} mm – no reducer will be created."
- **ISS-006** – Pick requests are captured in the queued closure; the shared `Request` field was removed (no more lost/overwritten picks).
- **ISS-007** – Discipline-correct size parameters: duct → `RBS_CURVE_DIAMETER_PARAM`, cable tray → width/height, conduit unchanged, pipe unchanged.
- **ISS-017** – `GeometryExtractor` uses `GetTotalTransform()` (consistent with walls/boundaries).
- **ISS-008** – Removed the dead Strategy combo and Elevation Offset textbox from the UI and from persisted settings.
- **ISS-015** – `SelectAfterRoute` now selects the created segments/fittings in Revit after a committed route.

## v4.7 (1.5.0)

Based on `reports/MEPAutoRouting_Technical_Audit.md` and `reports/MEPAutoRouting_Issues.csv`.

- **ISS-001** – Registered `MEPAutoRouting.Core.App` as an `Application` add-in: the "MEP Tools → Auto Routing" ribbon button now exists.
- **ISS-003** – Debug builds deploy `MEPAutoRouting.dll/.pdb` + manifest to the Addins **root** (same location as the manifest's relative Assembly path); a locked DLL produces a build warning instead of failing the build; `Deploy-Revit2025.ps1` also removes the legacy `2025\MEPAutoRouting\` subfolder.
- **ISS-002** – Settings split: `RoutingOptions` persists to `constraints.json` with one-time migration from `settings.json`; `UserSettings` keeps `settings.json` – neither file can overwrite the other.
- **ISS-004** – Post-slope validation: after `SlopeApplier.Apply` the path is re-validated (`allowSlope: true`, wall/boundary re-sampling, vertical-band check per point and per sampled segment point, column/framing box re-check); any failure blocks commit.
- **ISS-014** – Same-connector guard: identical source/target (same owner + connector, or origins < 1 mm) fails fast with "Source and target are the same connector."
- **ISS-016** – `RouteBlockedReason` now reports "Select a segment type." when no type is selected.
- **ISS-013** – Total fall resets to 0 mm when the connector selection changes or is cleared.
- **ISS-012** – Path preview and summary are cleared when boundary, slope (enabled/value/unit/flow), size, segment type or system type changes.

## v4.6 (1.4.0)

- Baseline before this audit; see `reports/` for the full technical audit.
