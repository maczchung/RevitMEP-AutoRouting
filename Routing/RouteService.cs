using System.Collections.Generic;
using System;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using MEPAutoRouting.Shared;
using MEPAutoRouting.Core;

namespace MEPAutoRouting.Routing
{
    public sealed class RouteRequest
    {
        public ConnectorInfo Source { get; init; }
        public ConnectorInfo Target { get; init; }
        public IRoutingBoundary Boundary { get; init; }
        public RoutingOptions Options { get; init; }
        public ElementId PipeTypeId { get; init; }
        public ElementId SystemTypeId { get; init; }
        public ElementId LevelId { get; init; }
        public Discipline Discipline { get; init; }
        public PipeSizeInfo? Size { get; init; }
        public List<RouteProblem> PreChecks { get; init; } = new List<RouteProblem>();
        public double MinSegmentMm { get; init; }
        public bool SuppressWarnings { get; init; } = true;
        public bool AddFittings { get; init; } = true;
        public bool ConnectEnds { get; init; } = true;
        public bool Commit { get; init; } = true;
        public bool SelectAfterRoute { get; init; } = false;   // v4.8 (ISS-015)
    }

    public static class RouteService
    {
        private const double MaxElbowDeg = 90.05;          // v4.8.3 – standard elbows cannot exceed 90°
        private const double StraightJoinDeg = 0.5;        // same threshold RouteBuilder uses to skip elbows
        private const double LeadParallelDeg = 0.01;       // v4.8.2 – lead must be parallel to connector axis

        public static RouteResult Run(UIDocument uidoc, RouteRequest request, Action<string> log)
        {
            var result = new RouteResult();
            if (uidoc == null || uidoc.Document == null)
            {
                result.Problems.Add(RouteProblem.Error("No active Revit document."));
                return result;
            }
            if (request == null)
            {
                result.Problems.Add(RouteProblem.Error("Route request is null."));
                return result;
            }

            result.Problems.AddRange(request.PreChecks ?? new List<RouteProblem>());
            if (PathValidator.HasErrors(result.Problems)) return result;
            if (request.Source == null || request.Target == null)
            {
                result.Problems.Add(RouteProblem.Error("Source and target connectors are required."));
                return result;
            }

            // v4.7 (ISS-014) – same connector / same point guard, before any pathfinding
            bool sameConnector = request.Source.OwnerId == request.Target.OwnerId
                              && request.Source.ConnectorId == request.Target.ConnectorId;
            bool samePoint = request.Source.Origin != null && request.Target.Origin != null
                          && request.Source.Origin.DistanceTo(request.Target.Origin) < Geom.MmToFt(1);
            if (sameConnector || samePoint)
            {
                result.Problems.Add(RouteProblem.Error("Source and target are the same connector."));
                return result;
            }

            Document doc = uidoc.Document;
            Connector sourceConnector = request.Source.Resolve(doc);
            Connector targetConnector = request.Target.Resolve(doc);
            if (sourceConnector == null || targetConnector == null)
            {
                result.Problems.Add(RouteProblem.Error("Could not resolve the selected Revit connectors."));
                return result;
            }

            RoutingOptions options = request.Options ?? new RoutingOptions();
            double lead = Geom.MmToFt(options.LeadLengthMm);
            ConnectorEndpoint source = ConnectorEndpoint.From(sourceConnector, lead);
            ConnectorEndpoint target = ConnectorEndpoint.From(targetConnector, lead);
            log?.Invoke($"Start: source {Geom.FtToMm(source.Radius * 2):0} mm, target {Geom.FtToMm(target.Radius * 2):0} mm, lead {options.LeadLengthMm:0} mm.");

            // v4.8.3 (Task 6) – ONE effective size: used for clearance, segment creation, OUTPUT and warnings
            bool fixedSize = ResolveSize(doc, request, source, log, out PipeSizeInfo routeSize);
            double routeMm = routeSize.NominalMm;

            // v4.4 – absorb small connector offsets into the leads
            double radius = fixedSize ? routeSize.OuterRadiusFt : Math.Max(source.Radius, target.Radius);
            double minJog = Math.Max(Geom.MmToFt(100), radius * 6);
            double minLead = Math.Max(Geom.MmToFt(75), radius * 4);
            AlignLeads(ref source, ref target, minJog, minLead, result.Problems, log);

            // v4.8 (Task 5) / v4.8.1 (Task 3) – warn for ANY size mismatch; no reducer is created
            void CheckSize(ConnectorEndpoint ep, string label)
            {
                if (ep.Radius > 0)
                {
                    double connMm = Geom.FtToMm(ep.Radius * 2);
                    if (Math.Abs(connMm - routeMm) > 0.5)
                        result.Problems.Add(RouteProblem.Warn(
                            $"Route size {routeMm:0.#} mm differs from {label} connector Ø{connMm:0.#} mm – no reducer will be created."));
                }
            }
            CheckSize(source, "source");
            CheckSize(target, "target");

            // v4.6 – configurable margin around the route region (default 2000 mm).
            double regionMargin = Geom.MmToFt(options.RegionMarginMm > 0 ? options.RegionMarginMm : 2000);

            XYZ regionMin;
            XYZ regionMax;
            if (request.Boundary != null)
            {
                regionMin = request.Boundary.Min;
                regionMax = request.Boundary.Max;
                if (!request.Boundary.Contains(source.Lead) || !request.Boundary.Contains(target.Lead))
                {
                    result.Problems.Add(RouteProblem.Error($"Source or target is outside {request.Boundary.Kind} '{request.Boundary.Name}'."));
                    return result;
                }
            }
            else
            {
                double margin = regionMargin;
                regionMin = new XYZ(Math.Min(source.Lead.X, target.Lead.X) - margin, Math.Min(source.Lead.Y, target.Lead.Y) - margin, Math.Min(source.Lead.Z, target.Lead.Z) - Geom.MmToFt(1000));
                regionMax = new XYZ(Math.Max(source.Lead.X, target.Lead.X) + margin, Math.Max(source.Lead.Y, target.Lead.Y) + margin, Math.Max(source.Lead.Z, target.Lead.Z) + Geom.MmToFt(1000));
            }

            double clearance = Geom.MmToFt(options.WallClearanceMm);

            // v4.5/v4.6 – keep the route between the reference level and the level above
            ClampVertical(doc, request.LevelId, source, target, radius, clearance, ref regionMin, ref regionMax, log);
            double zLimitMin = regionMin.Z, zLimitMax = regionMax.Z;

            log?.Invoke($"Region: {Geom.FtToMm(regionMax.X - regionMin.X):0} x {Geom.FtToMm(regionMax.Y - regionMin.Y):0} x {Geom.FtToMm(regionMax.Z - regionMin.Z):0} mm.");

            List<XYZ> middle = null;
            AStarPathfinder finder = null;
            RoutingConstraints constraints = null;

            // v4.6 – if A* fails, retry once with an expanded region before reporting no path
            for (int attempt = 0; attempt < 2; attempt++)
            {
                if (attempt == 1)
                {
                    regionMin = new XYZ(regionMin.X - regionMargin, regionMin.Y - regionMargin,
                                        Math.Max(regionMin.Z - regionMargin, zLimitMin));
                    regionMax = new XYZ(regionMax.X + regionMargin, regionMax.Y + regionMargin,
                                        Math.Min(regionMax.Z + regionMargin, zLimitMax));
                    log?.Invoke($"A* failed – retrying once with expanded region: {Geom.FtToMm(regionMax.X - regionMin.X):0} x {Geom.FtToMm(regionMax.Y - regionMin.Y):0} x {Geom.FtToMm(regionMax.Z - regionMin.Z):0} mm.");
                }

                XYZ wallsMin = new XYZ(regionMin.X - regionMargin, regionMin.Y - regionMargin, zLimitMin);
                XYZ wallsMax = new XYZ(regionMax.X + regionMargin, regionMax.Y + regionMargin, zLimitMax);
                List<WallObstacle> walls = WallObstacleCollector.Collect(doc, wallsMin, wallsMax, options.IncludeLinkWalls, clearance + radius);
                int linkedWalls = 0;
                foreach (WallObstacle w in walls) if (w.FromLink) linkedWalls++;
                log?.Invoke($"Walls: {walls.Count} ({walls.Count - linkedWalls} host, {linkedWalls} linked), clearance {options.WallClearanceMm:0} mm, region margin {Geom.FtToMm(regionMargin):0} mm.");
                log?.Invoke("Wall Ids: " + (walls.Count == 0
                    ? "-"
                    : string.Join(", ", walls.Take(40).Select(w => w.Id.ToString())) + (walls.Count > 40 ? ", …" : "")));
                if (attempt == 0 && walls.Count == 0)
                    result.Problems.Add(RouteProblem.Warn(
                        "No walls found in the routing region – check 'Include linked model walls' and that the link is loaded."));

                constraints = new RoutingConstraints(request.Boundary, walls, clearance, radius, options.ClearanceOnBoundary);
                constraints.AddExemptCorridor(source, Geom.MmToFt(150));
                constraints.AddExemptCorridor(target, Geom.MmToFt(150));

                finder = new AStarPathfinder(doc, CreateBounds(regionMin, regionMax), constraints);
                finder.SetVerticalLimits(zLimitMin, zLimitMax);
                log?.Invoke("Running A* pathfinder.");
                middle = finder.FindPath(source.Lead, target.Lead, source.Direction, source, target);
                log?.Invoke($"A* finished: {middle.Count} points, grid {finder.GridInfo}, blocked nodes {finder.BlockedNodeCount} / {finder.TotalNodeCount}.");
                if (PathUtils.IsValid(middle)) break;
                if (finder.LastFailure?.Diagnostics != null) log?.Invoke(finder.LastFailure.Diagnostics);
            }

            if (!PathUtils.IsValid(middle))
            {
                result.Problems.Add(RouteProblem.Error(
                    finder?.LastFailure?.Message ?? "No path found – source/target enclosed by walls."));
                return result;
            }

            var path = new List<XYZ> { source.Origin };
            path.AddRange(middle);
            path.Add(target.Origin);
            result.Path = PathUtils.MergeCollinear(path);

            result.Problems.AddRange(PathValidator.Validate(result.Path,
                Geom.MmToFt(request.MinSegmentMm > 0 ? request.MinSegmentMm : 0), constraints.IsBlocked, Geom.MmToFt(50) / 2.0,
                allowSlope: false));

            if (options.Slope != null && options.Slope.Enabled)
            {
                // v4.8 (Task 4) – gravity systems: uphill / unslopeable runs are Errors; pressurised systems: Warn.
                bool strictGravity = false;
                if (doc.GetElement(request.SystemTypeId) is Autodesk.Revit.DB.Plumbing.PipingSystemType pst)
                {
                    strictGravity = SlopeSettings.IsGravitySystem(pst, out bool isPressurised);
                    if (isPressurised)
                        result.Problems.Add(RouteProblem.Warn(
                            $"Slope is applied to a pressurised system ({pst.Name})."));
                            log?.Invoke(SlopeSettings.Describe(pst));
                }

                // v4.8.2 / v4.8.3 – leads stay flat, forced leads excluded from uphill, no > 90° slope turns
                result.Path = SlopeApplier.Apply(result.Path, options.Slope, result.Problems,
                    source.Lead, target.Lead, strictGravity, out double appliedFallFt);
                result.AppliedFallFt = appliedFallFt;
                log?.Invoke($"Slope applied: total fall {Geom.FtToMm(appliedFallFt):0} mm.");
                ValidateSlopedPath(result.Path, constraints, finder, source, target, zLimitMin, zLimitMax,
                    Geom.MmToFt(request.MinSegmentMm > 0 ? request.MinSegmentMm : 0), result.Problems, log);
            }

            // v4.8.3 (Task 4) – elbow angle gate BEFORE the transaction (also shown in Preview)
            if (request.AddFittings) CheckTurnAngles(result.Path, result.Problems, log);

            log?.Invoke($"Path ready: {result.Path.Count} points, slope {options.Slope?.Display ?? "None"}.");

            if (!request.Commit) return result;

            if (PathValidator.HasErrors(result.Problems))
            {
                result.Problems.Add(RouteProblem.Error("Errors found. Nothing was created."));
                return result;
            }

            if (request.PipeTypeId == ElementId.InvalidElementId || request.LevelId == ElementId.InvalidElementId)
            {
                result.Problems.Add(RouteProblem.Error("A valid type and reference level are required."));
                return result;
            }

            log?.Invoke($"Creating {result.Path.Count - 1} segments at {routeMm:0.#} mm.");
            using (var transaction = new Transaction(doc, "MEP Auto Route"))
            {
                transaction.Start();
                RouteFailureCollector failures = RouteFailureCollector.Attach(transaction, request.SuppressWarnings);
                try
                {
                    RouteOptions routeOptions = new RouteOptions
                    {
                        Discipline = request.Discipline,
                        TypeId = request.PipeTypeId,
                        SystemTypeId = request.SystemTypeId,
                        LevelId = request.LevelId,
                        MatchSize = !fixedSize,                       // v4.8.3 – created size == logged size
                        Size = fixedSize ? routeSize : (PipeSizeInfo?)null,
                        AddFittings = request.AddFittings,
                        ConnectEnds = request.ConnectEnds
                    };

                    RouteResult built = RouteBuilder.Build(doc, request.Source, request.Target, result.Path, routeOptions,
                        (level, message) => log?.Invoke($"{level}: {message}"));

                    result.CreatedSegments = built.Segments;
                    result.CreatedElbows = built.Fittings;
                    result.FittingFailures = built.FittingFailures;
                    result.Created.AddRange(built.Created);
                    log?.Invoke($"Elbows: {built.Fittings} created, {built.FittingFailures} failed.");

                    // v4.8 (ISS-026) – any elbow failure rolls the whole route back
                    if (built.FittingFailures > 0)
                    {
                        transaction.RollBack();
                        foreach (RouteBuilder.FittingFailureInfo f in built.FittingFailureDetails)
                            log?.Invoke($"Elbow failure detail: #{f.Index}, {f.AngleDeg:0.00}°, {f.Detail}.");
                        result.Problems.Add(RouteProblem.Error(
                            $"Route not created: {built.FittingFailures} elbow(s) failed. See OUTPUT for details."));
                        return result;
                    }

                    TransactionStatus status = transaction.Commit();
                    foreach (string warning in failures.Warnings) result.Problems.Add(RouteProblem.Warn(warning));
                    foreach (string error in failures.Errors) result.Problems.Add(RouteProblem.Error(error));

                    if (status != TransactionStatus.Committed)
                    {
                        result.Problems.Add(RouteProblem.Error($"Revit transaction was not committed ({status})."));
                        return result;
                    }
                    result.Committed = true;

                    // v4.8 (ISS-015) – select the created segments + fittings in Revit
                    if (request.SelectAfterRoute && result.Created.Count > 0)
                    {
                        try
                        {
                            uidoc.Selection.SetElementIds(result.Created);
                            log?.Invoke($"Selected {result.Created.Count} created element(s).");
                        }
                        catch (Exception selEx) { log?.Invoke($"Selection failed: {selEx.Message}"); }
                    }
                }
                catch (Exception ex)
                {
                    transaction.RollBack();
                    result.Problems.Add(RouteProblem.Error($"Route creation failed: {ex.GetType().Name}: {ex.Message}"));
                    return result;
                }
            }

            return result;
        }

        // ------------------------------------------------------------------ v4.8.3 size resolution

        /// <summary>
        /// Returns true when the size is fixed (user input or snapped to the type catalog) and must be
        /// applied to the created segments; false when RouteBuilder should match the connector size.
        /// </summary>
        private static bool ResolveSize(Document doc, RouteRequest request, ConnectorEndpoint source,
                                        Action<string> log, out PipeSizeInfo size)
        {
            if (request.Size.HasValue)
            {
                size = request.Size.Value;
                log?.Invoke($"Pipe size: {size.NominalMm:0.#} mm (user input).");
                return true;
            }

            double srcMm = Geom.FtToMm(source.Radius * 2);
            List<PipeSizeInfo> sizes;
            switch (request.Discipline)
            {
                case Discipline.Pipe:
                    sizes = PipeSizeCatalog.GetPipeSizes(doc, request.PipeTypeId, log);
                    break;
                case Discipline.Conduit:
                    sizes = PipeSizeCatalog.GetConduitSizes(doc, request.PipeTypeId);
                    break;
                default:
                    sizes = new List<PipeSizeInfo>();
                    break;
            }

            if (sizes.Count == 0)
            {
                size = new PipeSizeInfo(srcMm, srcMm);
                log?.Invoke($"Pipe size: {srcMm:0.#} mm (matched source Ø{srcMm:0.#}; no size list for this type – connector size used).");
                return false;
            }

            size = PipeSizeCatalog.Snap(srcMm, sizes, out bool exact);
            log?.Invoke($"Pipe size: {size.NominalMm:0.#} mm (matched source Ø{srcMm:0.#}" +
                        $"{(exact ? "" : ", snapped to type catalog")}).");
            return true;
        }

        // ------------------------------------------------------------------ v4.8.3 elbow angle gate

        private static void CheckTurnAngles(IList<XYZ> path, List<RouteProblem> problems, Action<string> log)
        {
            if (path == null || path.Count < 3) return;
            double max = 0;
            int turns = 0, bad = 0;
            for (int i = 1; i < path.Count - 1; i++)
            {
                XYZ v1 = path[i] - path[i - 1];
                XYZ v2 = path[i + 1] - path[i];
                if (v1.GetLength() < 1e-6 || v2.GetLength() < 1e-6) continue;
                double dot = Math.Max(-1.0, Math.Min(1.0, v1.Normalize().DotProduct(v2.Normalize())));
                double deg = Math.Acos(dot) * 180.0 / Math.PI;
                if (deg < StraightJoinDeg) continue;
                turns++;
                max = Math.Max(max, deg);
                if (deg > MaxElbowDeg)
                {
                    bad++;
                    XYZ p = path[i];
                    problems.Add(RouteProblem.Error(
                        $"Turn {i} is {deg:0.00}° at ({Geom.FtToMm(p.X):0}, {Geom.FtToMm(p.Y):0}, {Geom.FtToMm(p.Z):0}) – " +
                        "standard elbows cannot exceed 90°.", i));
                }
            }
            log?.Invoke($"Turn precheck: {turns} turn(s), max {max:0.00}°{(bad > 0 ? $", {bad} over 90°" : "")}.");
        }

        // ------------------------------------------------------------------ v4.7 post-slope validation

        /// <summary>
        /// v4.7 (ISS-004) – re-validate the path AFTER the slope is applied.
        /// v4.8.2 – also checks that both connector leads are parallel to their connector axis.
        /// </summary>
        private static void ValidateSlopedPath(List<XYZ> path, RoutingConstraints constraints, AStarPathfinder finder,
                                               ConnectorEndpoint source, ConnectorEndpoint target,
                                               double zLimitMin, double zLimitMax, double minSegmentFt,
                                               List<RouteProblem> problems, Action<string> log)
        {
            int errorsBefore = problems.Count(p => p.IsError);

            // (a) geometry + wall / boundary re-sampling, slope-aware
            problems.AddRange(PathValidator.Validate(path, minSegmentFt, constraints.IsBlocked,
                Geom.MmToFt(50) / 2.0, allowSlope: true));

            // (a2) v4.8.2 (Task 1) – connector leads parallel to the connector axis
            if (path.Count >= 2)
            {
                bool srcOk = IsParallel(path[1] - path[0], source.Direction);
                bool tgtOk = IsParallel(path[^1] - path[^2], target.Direction);
                if (!srcOk) problems.Add(RouteProblem.Error("Connector lead source is not aligned.", 0));
                if (!tgtOk) problems.Add(RouteProblem.Error("Connector lead target is not aligned.", path.Count - 2));
                log?.Invoke($"Leads fixed: source {(srcOk ? "OK" : "FAILED")}, target {(tgtOk ? "OK" : "FAILED")}.");
            }

            double zTol = Geom.MmToFt(1);
            string limits = $"limit {Geom.FtToMm(zLimitMin):0}-{Geom.FtToMm(zLimitMax):0} mm";

            // (b) vertical band – every path point
            for (int i = 0; i < path.Count; i++)
            {
                if (path[i].Z < zLimitMin - zTol || path[i].Z > zLimitMax + zTol)
                    problems.Add(RouteProblem.Error(
                        $"Sloped route leaves the permitted vertical range at point {i} " +
                        $"(Z = {Geom.FtToMm(path[i].Z):0} mm, {limits}).", i));
            }

            // (b) + (c) sampled points along every segment: vertical band and column / framing boxes
            for (int i = 0; i < path.Count - 1; i++)
            {
                double len = path[i].DistanceTo(path[i + 1]);
                int n = Math.Max(1, (int)Math.Ceiling(len / (Geom.MmToFt(50) / 2.0)));
                for (int k = 1; k < n; k++)
                {
                    XYZ p = path[i] + (path[i + 1] - path[i]) * ((double)k / n);
                    if (p.Z < zLimitMin - zTol || p.Z > zLimitMax + zTol)
                    {
                        problems.Add(RouteProblem.Error(
                            $"Sloped route leaves the permitted vertical range at segment {i} " +
                            $"(Z = {Geom.FtToMm(p.Z):0} mm, {limits}).", i));
                        break;
                    }
                }

                if (finder != null && finder.SegmentHitsObstacleBox(path[i], path[i + 1], source.Origin, target.Origin))
                    problems.Add(RouteProblem.Error(
                        $"Sloped route collides with a column or framing element at segment {i}.", i));
            }

            int errors = problems.Count(p => p.IsError) - errorsBefore;
            log?.Invoke(errors == 0 ? "Post-slope validation: OK" : $"Post-slope validation: {errors} error(s).");
        }

        private static bool IsParallel(XYZ v, XYZ axis)
        {
            if (v == null || axis == null || v.GetLength() < 1e-9 || axis.GetLength() < 1e-9) return true;
            double dot = Math.Abs(v.Normalize().DotProduct(axis.Normalize()));
            return dot >= Math.Cos(LeadParallelDeg * Math.PI / 180.0);
        }

        // ------------------------------------------------------------------ v4.5 vertical limits

        private static void ClampVertical(Document doc, ElementId levelId, ConnectorEndpoint s, ConnectorEndpoint t,
                                          double radius, double clearanceFt, ref XYZ min, ref XYZ max, Action<string> log)
        {
            if (doc.GetElement(levelId) is not Level level)
            {
                log?.Invoke("Vertical limits: no valid reference level – Z range left unclamped.");
                return;
            }

            double levelZ = level.ProjectElevation;
            double? nextZ = null;
            foreach (Level l in new FilteredElementCollector(doc).OfClass(typeof(Level)))
            {
                double z = l.ProjectElevation;
                if (z > levelZ + Geom.MmToFt(1) && (nextZ == null || z < nextZ)) nextZ = z;
            }

            double lowEnd = Math.Min(Math.Min(s.Origin.Z, s.Lead.Z), Math.Min(t.Origin.Z, t.Lead.Z));
            double highEnd = Math.Max(Math.Max(s.Origin.Z, s.Lead.Z), Math.Max(t.Origin.Z, t.Lead.Z));

            double zMin = levelZ + radius + clearanceFt;
            if (lowEnd > levelZ) zMin = Math.Min(zMin, lowEnd);
            zMin = Math.Max(zMin, levelZ);

            double zMax = nextZ.HasValue ? Math.Min(max.Z, Math.Max(nextZ.Value - radius, highEnd)) : max.Z;

            min = new XYZ(min.X, min.Y, zMin);
            max = new XYZ(max.X, max.Y, zMax);

            log?.Invoke($"Vertical limits: Z {Geom.FtToMm(zMin):0} – {Geom.FtToMm(zMax):0} mm " +
                        $"(level elev {Geom.FtToMm(levelZ):0} mm{(nextZ.HasValue ? "" : ", no level above")}).");
        }

        // ------------------------------------------------------------------ v4.4 lead alignment

        private static void AlignLeads(ref ConnectorEndpoint s, ref ConnectorEndpoint t,
                                       double minJogFt, double minLeadFt,
                                       List<RouteProblem> problems, Action<string> log)
        {
            string[] names = { "X", "Y", "Z" };
            for (int a = 0; a < 3; a++)
            {
                double diff = C(t.Lead, a) - C(s.Lead, a);
                if (Math.Abs(diff) < 1.0 / 304.8 || Math.Abs(diff) >= minJogFt) continue;

                bool fixedIt = false;
                if (Math.Abs(C(s.Direction, a)) > 0.5)
                {
                    double len = (C(t.Lead, a) - C(s.Origin, a)) / C(s.Direction, a);
                    if (len >= minLeadFt)
                    {
                        log?.Invoke($"Source lead changed to {Geom.FtToMm(len):0} mm to remove a {Geom.FtToMm(Math.Abs(diff)):0} mm {names[a]} offset.");
                        s = s.WithLeadLength(len);
                        fixedIt = true;
                    }
                }
                if (!fixedIt && Math.Abs(C(t.Direction, a)) > 0.5)
                {
                    double len = (C(s.Lead, a) - C(t.Origin, a)) / C(t.Direction, a);
                    if (len >= minLeadFt)
                    {
                        log?.Invoke($"Target lead changed to {Geom.FtToMm(len):0} mm to remove a {Geom.FtToMm(Math.Abs(diff)):0} mm {names[a]} offset.");
                        t = t.WithLeadLength(len);
                        fixedIt = true;
                    }
                }
                if (!fixedIt)
                    problems.Add(RouteProblem.Warn(
                        $"Source and target are offset by only {Geom.FtToMm(Math.Abs(diff)):0} mm in {names[a]} – " +
                        $"the route needs a short jog (< {Geom.FtToMm(minJogFt):0} mm) and its elbows may fail. " +
                        "Move the equipment or change the lead length."));
            }
        }

        private static double C(XYZ p, int axis) => axis == 0 ? p.X : axis == 1 ? p.Y : p.Z;

        private static BoundingBoxXYZ CreateBounds(XYZ min, XYZ max) => new BoundingBoxXYZ { Min = min, Max = max };
    }
}
