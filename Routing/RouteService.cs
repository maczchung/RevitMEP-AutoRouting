using System.Collections.Generic;
using System;
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

    public sealed class RoutePlan
    {
        public List<XYZ> Points { get; }
        public List<RouteProblem> Problems { get; }
        public string Boundary { get; }
        public double TotalFallFt { get; }

        public RoutePlan(List<XYZ> points, List<RouteProblem> problems, string boundary)
        {
            Points = points ?? new List<XYZ>();
            Problems = problems ?? new List<RouteProblem>();
            Boundary = string.IsNullOrEmpty(boundary) ? "None" : boundary;
            TotalFallFt = Points.Count < 2 ? 0 : System.Math.Abs(Points[0].Z - Points[Points.Count - 1].Z);
        }
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

            // v4.4 – absorb small connector offsets into the leads (avoids unbuildable short jogs)
            double routeRadius = request.Size?.OuterRadiusFt ?? Math.Max(source.Radius, target.Radius);
            double minJog = Math.Max(Geom.MmToFt(100), routeRadius * 6);    // ≈ 3 × D
            double minLead = Math.Max(Geom.MmToFt(75), routeRadius * 4);    // ≈ 2 × D
            AlignLeads(ref source, ref target, minJog, minLead, result.Problems, log);

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
                double margin = Geom.MmToFt(2000);
                regionMin = new XYZ(Math.Min(source.Lead.X, target.Lead.X) - margin, Math.Min(source.Lead.Y, target.Lead.Y) - margin, Math.Min(source.Lead.Z, target.Lead.Z) - Geom.MmToFt(1000));
                regionMax = new XYZ(Math.Max(source.Lead.X, target.Lead.X) + margin, Math.Max(source.Lead.Y, target.Lead.Y) + margin, Math.Max(source.Lead.Z, target.Lead.Z) + Geom.MmToFt(1000));
            }
            log?.Invoke($"Region: {Geom.FtToMm(regionMax.X - regionMin.X):0} x {Geom.FtToMm(regionMax.Y - regionMin.Y):0} x {Geom.FtToMm(regionMax.Z - regionMin.Z):0} mm.");

            double radius = routeRadius;
            double clearance = Geom.MmToFt(options.WallClearanceMm);
            List<WallObstacle> walls = WallObstacleCollector.Collect(doc, regionMin, regionMax, options.IncludeLinkWalls, clearance + radius);
            var constraints = new RoutingConstraints(request.Boundary, walls, clearance, radius, options.ClearanceOnBoundary);
            constraints.AddExemptCorridor(source, Geom.MmToFt(150));
            constraints.AddExemptCorridor(target, Geom.MmToFt(150));

            AStarPathfinder finder = new AStarPathfinder(doc, CreateBounds(regionMin, regionMax), constraints);
            log?.Invoke("Running A* pathfinder.");
            List<XYZ> middle = finder.FindPath(source.Lead, target.Lead, source.Direction, source, target);
            log?.Invoke($"A* finished: {middle.Count} points, grid {finder.GridInfo}.");
            if (!PathUtils.IsValid(middle))
            {
                result.Problems.Add(RouteProblem.Error("No path found. Try a smaller wall clearance, a larger boundary or a shorter lead length."));
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

        // ------------------------------------------------------------------ v4.4 lead alignment

        /// <summary>
        /// If source.Lead and target.Lead differ by less than <paramref name="minJogFt"/> on an axis,
        /// an elbow pair cannot fit in that jog. When the axis is a connector axis, lengthen / shorten
        /// that connector's lead so the offset becomes zero.
        /// </summary>
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

        // ------------------------------------------------------------------ preview (RoutePlanner)

        public static RoutePlan Plan(ConnectorInfo source, ConnectorInfo target, RouteOptions options,
                                     double levelElevationFt, RoutingOptions routingOptions)
        {
            return Plan(null, source, target, options, levelElevationFt, routingOptions, null, null, 0);
        }

        public static RoutePlan Plan(Document doc, ConnectorInfo source, ConnectorInfo target, RouteOptions options,
                                     double levelElevationFt, RoutingOptions routingOptions, IRoutingBoundary boundary,
                                     PipeSizeInfo? size = null, double minSegmentMm = 0)
        {
            var problems = new List<RouteProblem>();
            var plannerMessages = new List<string>();
            List<XYZ> points = RoutePlanner.Plan(source, target, options, levelElevationFt, plannerMessages);
            foreach (string message in plannerMessages)
                problems.Add(RouteProblem.Warn(message));

            bool sloped = routingOptions?.Slope != null && routingOptions.Slope.Enabled;
            if (sloped)
                points = SlopeApplier.Apply(points, routingOptions.Slope, problems);

            if (boundary != null)
            {
                if (!boundary.Contains(source.Origin, 0, 0) || !boundary.Contains(target.Origin, 0, 0))
                    problems.Add(RouteProblem.Error($"Source or target is outside {boundary.Kind} '{boundary.Name}'."));

                if (doc != null)
                {
                    double radius = size?.OuterRadiusFt ?? System.Math.Max(source.Radius, target.Radius);
                    double clearance = UnitConv.MmToFt(routingOptions?.WallClearanceMm ?? 0);
                    var walls = WallObstacleCollector.Collect(doc, boundary.Min, boundary.Max,
                        routingOptions?.IncludeLinkWalls == true, clearance + radius);
                    var constraints = new RoutingConstraints(boundary, walls, clearance, radius,
                        routingOptions?.ClearanceOnBoundary != false);
                    problems.AddRange(PathValidator.Validate(points, UnitConv.MmToFt(minSegmentMm > 0 ? minSegmentMm : options.MinSegmentMm),
                        constraints.IsBlocked, UnitConv.MmToFt(50) / 2.0, sloped));
                }
            }

            problems.AddRange(PathValidator.Validate(
                points,
                UnitConv.MmToFt(minSegmentMm > 0 ? minSegmentMm : options.MinSegmentMm),
                allowSlope: sloped));

            string boundaryText = boundary == null ? "None" : $"{boundary.Kind} {boundary.Name}";
            return new RoutePlan(points, problems, boundaryText);
        }
    }
}
