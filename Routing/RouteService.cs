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

            double radius = request.Size?.OuterRadiusFt ?? Math.Max(source.Radius, target.Radius);
            double clearance = Geom.MmToFt(options.WallClearanceMm);
            List<WallObstacle> walls = WallObstacleCollector.Collect(doc, regionMin, regionMax, options.IncludeLinkWalls, clearance + radius);
            var constraints = new RoutingConstraints(request.Boundary, walls, clearance, radius, options.ClearanceOnBoundary);
            constraints.AddExemptCorridor(source, Geom.MmToFt(150));
            constraints.AddExemptCorridor(target, Geom.MmToFt(150));

            AStarPathfinder finder = new AStarPathfinder(doc, CreateBounds(regionMin, regionMax), constraints);
            log?.Invoke("Running A* pathfinder.");
            List<XYZ> middle = finder.FindPath(source.Lead, target.Lead, source.Direction, source, target);
            log?.Invoke($"A* finished: {middle.Count} points.");
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
                Geom.MmToFt(request.MinSegmentMm > 0 ? request.MinSegmentMm : 0), constraints.IsBlocked, Geom.MmToFt(50) / 2.0));
            if (options.Slope != null && options.Slope.Enabled)
                result.Path = SlopeApplier.Apply(result.Path, options.Slope, result.Problems);
            log?.Invoke($"Path ready: {result.Path.Count} points, slope {options.Slope.Display}.");

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

        private static BoundingBoxXYZ CreateBounds(XYZ min, XYZ max)
        {
            var bounds = new BoundingBoxXYZ { Min = min, Max = max };
            return bounds;
        }

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

            if (routingOptions?.Slope != null && routingOptions.Slope.Enabled)
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
                        constraints.IsBlocked, UnitConv.MmToFt(50) / 2.0));
                }
            }

            problems.AddRange(PathValidator.Validate(
                points,
                UnitConv.MmToFt(minSegmentMm > 0 ? minSegmentMm : options.MinSegmentMm)));

            string boundaryText = boundary == null ? "None" : $"{boundary.Kind} {boundary.Name}";
            return new RoutePlan(points, problems, boundaryText);
        }
    }
}
