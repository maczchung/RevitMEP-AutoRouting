using System;
using System.Collections.Generic;
using Autodesk.Revit.DB;
using MEPAutoRouting.Shared;

namespace MEPAutoRouting.Routing
{
    /// <summary>
    /// Creates MEP curves, elbows and endpoint transitions along planned points.
    /// Caller owns the transaction.
    ///
    /// v4.4: elbow failures report position, turn angle and both segment lengths.
    /// v4.8: elbow failures additionally report the segment sizes, selected type and
    ///       routing-preference elbow family; caller rolls the route back on failure.
    /// v4.9: pipe endpoints with different round connector sizes use
    ///       Document.Create.NewTransitionFitting() to create an automatic reducer.
    ///       Reducer failures are included in FittingFailures, so RouteService rolls
    ///       the complete route back.
    /// </summary>
    public static class RouteBuilder
    {
        private const double StraightTolDeg = 0.5;
        private const double VerticalDot = 0.999;
        private const double SizeToleranceMm = 0.5;

        // One failed elbow/reducer carries its detail here so RouteService can
        // put the failure in OUTPUT/PROBLEMS and roll back the transaction.
        public sealed class FittingFailureInfo
        {
            public int Index { get; set; }
            public double AngleDeg { get; set; }
            public string Detail { get; set; }
        }

        public static RouteResult Build(
            Document doc,
            ConnectorInfo s,
            ConnectorInfo t,
            IList<XYZ> pts,
            RouteOptions o,
            Action<LogLevel, string> log)
        {
            var result = new RouteResult();
            var factory = SegmentFactory.For(o);
            var segments = new List<MEPCurve>();

            // Sizes and routing-preference elbow for per-elbow diagnostics.
            string sizeText = o.Size.HasValue
                ? $"{o.Size.Value.NominalMm:0.#} mm"
                : s.Shape == ConnectorProfileType.Round
                    ? $"{UnitConv.Mm(s.Radius * 2)} mm (match source)"
                    : "match source";

            string elbowName = GetRoutingPreferenceElbow(doc, o);

            // 1. Create route segments.
            for (int i = 0; i < pts.Count - 1; i++)
            {
                MEPCurve seg = factory.Create(doc, pts[i], pts[i + 1]);

                if (o.MatchSize)
                    factory.ApplySize(seg, s);
                else if (o.Size.HasValue)
                    ApplyNominalSize(seg, o.Size.Value.NominalFt, o.Discipline);

                segments.Add(seg);
                result.Created.Add(seg.Id);
            }

            result.Segments = segments.Count;
            doc.Regenerate();

            // 2. Create internal elbows.
            if (o.AddFittings)
            {
                // Log every turn before creating any elbow.
                for (int i = 1; i < segments.Count; i++)
                {
                    XYZ p1 = (pts[i] - pts[i - 1]).Normalize();
                    XYZ p2 = (pts[i + 1] - pts[i]).Normalize();
                    double preDeg = p1.AngleTo(p2) * 180.0 / Math.PI;

                    if (preDeg < StraightTolDeg)
                        continue;

                    log?.Invoke(
                        LogLevel.Info,
                        $"Elbow precheck {i}: turn {preDeg:0.00} deg at " +
                        $"({UnitConv.Mm(pts[i].X)}, {UnitConv.Mm(pts[i].Y)}, {UnitConv.Mm(pts[i].Z)}).");
                }

                for (int i = 1; i < segments.Count; i++)
                {
                    XYZ d1 = (pts[i] - pts[i - 1]).Normalize();
                    XYZ d2 = (pts[i + 1] - pts[i]).Normalize();
                    double turnDeg = d1.AngleTo(d2) * 180.0 / Math.PI;

                    if (turnDeg < StraightTolDeg)
                        continue;

                    bool slopedToVertical =
                        (Math.Abs(d1.Z) < 0.5 || Math.Abs(d2.Z) < 0.5) &&
                        Math.Abs(Math.Abs(d1.Z) - Math.Abs(d2.Z)) > 0.01 &&
                        (Math.Abs(d1.Z) > VerticalDot ||
                         Math.Abs(d2.Z) > VerticalDot ||
                         (Math.Abs(d1.Z) > 0.01 && Math.Abs(d1.Z) < VerticalDot) ||
                         (Math.Abs(d2.Z) > 0.01 && Math.Abs(d2.Z) < VerticalDot));

                    Connector c1 = ConnectorUtils.GetNearestConnector(segments[i - 1], pts[i]);
                    Connector c2 = ConnectorUtils.GetNearestConnector(segments[i], pts[i]);

                    string where =
                        $"({UnitConv.Mm(pts[i].X)}, {UnitConv.Mm(pts[i].Y)}, {UnitConv.Mm(pts[i].Z)})";

                    string lens =
                        $"{UnitConv.Mm(pts[i - 1].DistanceTo(pts[i]))} / " +
                        $"{UnitConv.Mm(pts[i].DistanceTo(pts[i + 1]))} mm";

                    string detail =
                        $"angle {turnDeg:0.00} deg, segments {lens}, " +
                        $"size {sizeText}, elbow '{elbowName}'";

                    if (c1 == null || c2 == null)
                    {
                        AddFittingFailure(
                            result,
                            i,
                            turnDeg,
                            $"connector not found ({detail})");

                        log?.Invoke(
                            LogLevel.Warn,
                            $"Elbow {i} at {where}: connector not found - {detail}.");

                        continue;
                    }

                    try
                    {
                        FamilyInstance fit = doc.Create.NewElbowFitting(c1, c2);
                        result.Created.Add(fit.Id);
                        result.Fittings++;
                    }
                    catch (Exception ex)
                    {
                        AddFittingFailure(
                            result,
                            i,
                            turnDeg,
                            $"{detail} - {ex.Message}");

                        log?.Invoke(
                            LogLevel.Warn,
                            $"Elbow {i} failed at {where}: {detail} - {ex.Message}" +
                            (slopedToVertical ? " (sloped-to-vertical turn)" : ""));
                    }
                }

                doc.Regenerate();
            }

            // 3. Connect route ends. For round piping size mismatch, create a reducer.
            if (o.ConnectEnds && segments.Count > 0)
            {
                TryConnectOrReduce(
                    doc,
                    s.Resolve(doc),
                    segments[0],
                    pts[0],
                    "source",
                    o,
                    result,
                    log,
                    failureIndex: -1);

                TryConnectOrReduce(
                    doc,
                    t.Resolve(doc),
                    segments[segments.Count - 1],
                    pts[pts.Count - 1],
                    "target",
                    o,
                    result,
                    log,
                    failureIndex: -2);
            }

            result.Success = result.FittingFailures == 0;
            return result;
        }

        /// <summary>
        /// Connects a route-end connector to an equipment connector.
        /// For round piping connectors whose diameters differ by more than 0.5 mm,
        /// creates a Revit transition fitting (reducer) instead of ConnectTo().
        /// </summary>
        private static void TryConnectOrReduce(
            Document doc,
            Connector equipment,
            MEPCurve segment,
            XYZ at,
            string label,
            RouteOptions options,
            RouteResult result,
            Action<LogLevel, string> log,
            int failureIndex)
        {
            string displayLabel = Capitalize(label);

            if (equipment == null)
            {
                AddFittingFailure(
                    result,
                    failureIndex,
                    0,
                    $"{displayLabel} connector could not be resolved");

                log?.Invoke(LogLevel.Warn, $"Could not resolve {label} connector.");
                return;
            }

            if (equipment.IsConnected)
            {
                AddFittingFailure(
                    result,
                    failureIndex,
                    0,
                    $"{displayLabel} connector is already connected");

                log?.Invoke(
                    LogLevel.Warn,
                    $"{label} connector already connected - route cannot connect. " +
                    "Delete pipes from a previous test or re-pick the connector.");

                return;
            }

            Connector routeConnector = ConnectorUtils.GetNearestConnector(segment, at);
            if (routeConnector == null)
            {
                AddFittingFailure(
                    result,
                    failureIndex,
                    0,
                    $"Route connector at {label} endpoint was not found");

                log?.Invoke(LogLevel.Warn, $"Route connector at {label} endpoint was not found.");
                return;
            }

            if (routeConnector.IsConnected)
            {
                AddFittingFailure(
                    result,
                    failureIndex,
                    0,
                    $"Route connector at {label} endpoint is already connected");

                log?.Invoke(LogLevel.Warn, $"Route connector at {label} endpoint is already connected.");
                return;
            }

            if (routeConnector.Domain != equipment.Domain)
            {
                AddFittingFailure(
                    result,
                    failureIndex,
                    0,
                    $"{displayLabel} domain mismatch: route={routeConnector.Domain}, equipment={equipment.Domain}");

                log?.Invoke(
                    LogLevel.Warn,
                    $"{displayLabel} domain mismatch: route={routeConnector.Domain}, " +
                    $"equipment={equipment.Domain}.");

                return;
            }

            bool canCreateReducer =
                options.Discipline == Discipline.Pipe &&
                routeConnector.Domain == Domain.DomainPiping &&
                equipment.Domain == Domain.DomainPiping &&
                routeConnector.Shape == ConnectorProfileType.Round &&
                equipment.Shape == ConnectorProfileType.Round;

            double routeDiameterMm = GetRoundDiameterMm(routeConnector);
            double equipmentDiameterMm = GetRoundDiameterMm(equipment);
            bool sizeDifferent =
                routeDiameterMm > 0 &&
                equipmentDiameterMm > 0 &&
                Math.Abs(routeDiameterMm - equipmentDiameterMm) > SizeToleranceMm;

            try
            {
                if (canCreateReducer && sizeDifferent)
                {
                    FamilyInstance reducer =
                        doc.Create.NewTransitionFitting(routeConnector, equipment);

                    if (reducer == null)
                        throw new InvalidOperationException("Revit returned no transition fitting.");

                    result.Created.Add(reducer.Id);
                    doc.Regenerate();

                    log?.Invoke(
                        LogLevel.Success,
                        $"{displayLabel} reducer created: " +
                        $"route Ø{routeDiameterMm:0.#} mm -> " +
                        $"connector Ø{equipmentDiameterMm:0.#} mm, " +
                        $"ElementId {reducer.Id.Value}.");
                }
                else
                {
                    routeConnector.ConnectTo(equipment);
                    doc.Regenerate();

                    log?.Invoke(
                        LogLevel.Info,
                        $"Connected to {label} connector" +
                        (routeDiameterMm > 0 && equipmentDiameterMm > 0
                            ? $" (route Ø{routeDiameterMm:0.#} mm, connector Ø{equipmentDiameterMm:0.#} mm)."
                            : "."));
                }
            }
            catch (Exception ex)
            {
                string sizeDetail =
                    routeDiameterMm > 0 && equipmentDiameterMm > 0
                        ? $"route Ø{routeDiameterMm:0.#} mm, connector Ø{equipmentDiameterMm:0.#} mm"
                        : "size unavailable";

                string action = canCreateReducer && sizeDifferent
                    ? "reducer creation"
                    : "connection";

                AddFittingFailure(
                    result,
                    failureIndex,
                    0,
                    $"{displayLabel} {action} failed ({sizeDetail}) - {ex.Message}");

                log?.Invoke(
                    LogLevel.Warn,
                    $"{displayLabel} {action} failed: {sizeDetail} - {ex.Message}");
            }
        }

        private static void AddFittingFailure(
            RouteResult result,
            int index,
            double angleDeg,
            string detail)
        {
            result.FittingFailures++;
            result.FittingFailureDetails.Add(
                new FittingFailureInfo
                {
                    Index = index,
                    AngleDeg = angleDeg,
                    Detail = detail
                });
        }

        private static double GetRoundDiameterMm(Connector connector)
        {
            if (connector == null || connector.Shape != ConnectorProfileType.Round)
                return 0;

            try
            {
                return connector.Radius * 2.0 * 304.8;
            }
            catch
            {
                return 0;
            }
        }

        private static string Capitalize(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return value ?? string.Empty;

            return char.ToUpperInvariant(value[0]) + value.Substring(1);
        }

        /// <summary>
        /// Elbow family currently chosen by the pipe type routing preferences.
        /// </summary>
        private static string GetRoutingPreferenceElbow(Document doc, RouteOptions o)
        {
            try
            {
                if (o.Discipline != Discipline.Pipe)
                    return "-";

                if (doc?.GetElement(o.TypeId) is not Autodesk.Revit.DB.Plumbing.PipeType pt)
                    return "-";

                RoutingPreferenceManager rpm = pt.RoutingPreferenceManager;
                if (rpm == null)
                    return "-";

                int count = rpm.GetNumberOfRules(RoutingPreferenceRuleGroupType.Elbows);
                for (int i = 0; i < count; i++)
                {
                    RoutingPreferenceRule rule =
                        rpm.GetRule(RoutingPreferenceRuleGroupType.Elbows, i);

                    if (doc.GetElement(rule.MEPPartId) is Element element)
                        return element.Name;
                }
            }
            catch
            {
                // Diagnostics only.
            }

            return "-";
        }

        private static void ApplyNominalSize(
            MEPCurve curve,
            double diameterFt,
            Discipline discipline)
        {
            if (curve == null || diameterFt <= 0)
                return;

            switch (discipline)
            {
                case Discipline.Conduit:
                    SegmentFactory.SetParam(
                        curve,
                        BuiltInParameter.RBS_CONDUIT_DIAMETER_PARAM,
                        diameterFt);
                    break;

                case Discipline.Duct:
                    SegmentFactory.SetParam(
                        curve,
                        BuiltInParameter.RBS_CURVE_DIAMETER_PARAM,
                        diameterFt);
                    break;

                case Discipline.CableTray:
                    SegmentFactory.SetParam(
                        curve,
                        BuiltInParameter.RBS_CABLETRAY_WIDTH_PARAM,
                        diameterFt);
                    SegmentFactory.SetParam(
                        curve,
                        BuiltInParameter.RBS_CABLETRAY_HEIGHT_PARAM,
                        diameterFt);
                    break;

                default:
                    SegmentFactory.SetParam(
                        curve,
                        BuiltInParameter.RBS_PIPE_DIAMETER_PARAM,
                        diameterFt);
                    break;
            }
        }
    }
}
