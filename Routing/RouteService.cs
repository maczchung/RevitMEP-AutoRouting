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
    }

    public static class RouteService
    {
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

            // v4.4 – absorb small connector offsets into the leads
            double radius = request.Size?.OuterRadiusFt ?? Math.Max(source.Radius, target.Radius);
            double minJog = Math.Max(Geom.MmToFt(100), radius * 6);
            double minLead = Math.Max(Geom.MmToFt(75), radius * 4);
            AlignLeads(ref source, ref target, minJog, minLead, result.Problems, log);

            // v4.6 – configurable margin around the route region (default 2000 mm).
            //        Walls are collected over region + margin so walls enclosing the room are included.
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

            // v4.5/v4.6 – keep the route between the reference level and the level above;
            //             zLimitMin = level + pipe OD/2 + clearance, never below the level.
            ClampVertical(doc, request.LevelId, source, target, radius, clearance, ref regionMin, ref regionMax, log);
            double zLimitMin = regionMin.Z, zLimitMax = regionMax.Z;   // hard limits – region expansion never crosses these

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
                finder.SetVerticalLimits(zLimitMin, zLimitMax);   // v4.6 – BFS + A* block nodes outside the level band
                log?.Invoke("Running A* pathfinder.");
                middle = finder.FindPath(source.Lead, target.Lead, source.Direction, source, target);
                log?.Invoke($"A* finished: {middle.Count} points, grid {finder.GridInfo}, blocked nodes {finder.BlockedNodeCount} / {finder.TotalNodeCount}.");
                if (PathUtils.IsValid(middle)) break;
                if (finder.LastFailure?.Diagnostics != null) log?.Invoke(finder.LastFailure.Diagnostics);
            }

            if (!PathUtils.IsValid(middle))
            {
                // v4.6 – no modal dialog: the failure is returned as a RouteProblem (PROBLEMS + status bar)
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
                result.Path = SlopeApplier.Apply(result.Path, options.Slope, result.Problems);

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

            log?.Invoke($"Creating {result.Path.Count - 1} segments.");
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
                        MatchSize = request.Size == null,
                        Size = request.Size,
                        AddFittings = request.AddFittings,
                        ConnectEnds = request.ConnectEnds
                    };

                    RouteResult built = RouteBuilder.Build(doc, request.Source, request.Target, result.Path, routeOptions,
                        (level, message) => log?.Invoke($"{level}: {message}"));

                    result.CreatedSegments = built.Segments;
                    result.CreatedElbows = built.Fittings;
                    result.FittingFailures = built.FittingFailures;
                    log?.Invoke($"Elbows: {built.Fittings} created, {built.FittingFailures} failed.");
                    if (built.FittingFailures > 0)
                        result.Problems.Add(RouteProblem.Warn(
                            $"{built.FittingFailures} elbow(s) could not be created – see OUTPUT for position, angle and segment lengths."));

                    TransactionStatus status = transaction.Commit();
                    foreach (string warning in failures.Warnings) result.Problems.Add(RouteProblem.Warn(warning));
                    foreach (string error in failures.Errors) result.Problems.Add(RouteProblem.Error(error));

                    if (status != TransactionStatus.Committed)
                    {
                        result.Problems.Add(RouteProblem.Error($"Revit transaction was not committed ({status})."));
                        return result;
                    }
                    result.Committed = true;
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

        // ------------------------------------------------------------------ v4.5 vertical limits

        /// <summary>
        /// Z range = [reference level + pipe radius, level above − pipe radius].
        /// The connectors themselves are always kept inside the range.
        /// </summary>
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

            // v4.6 – zLimitMin = reference level + pipe OD/2 + clearance, never below the reference level.
            double zMin = levelZ + radius + clearanceFt;
            if (lowEnd > levelZ) zMin = Math.Min(zMin, lowEnd);   // keep above-level connectors inside the band
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
