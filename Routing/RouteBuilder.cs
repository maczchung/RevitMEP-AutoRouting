using System;
using System.Collections.Generic;
using Autodesk.Revit.DB;
using MEPAutoRouting.Shared;

namespace MEPAutoRouting.Routing
{
    /// <summary>
    /// Creates MEP curves + elbows along planned points. Caller owns the transaction.
    /// v4.4: elbow failures report position, turn angle and both segment lengths.
    /// v4.8: elbow failures additionally report the segment sizes, the selected type and the
    ///       routing-preference elbow family; the caller rolls the whole route back on any failure.
    /// </summary>
    public static class RouteBuilder
    {
        private const double StraightTolDeg = 0.5;
        private const double VerticalDot = 0.999;

        // v4.8 (Task 3) – one failed elbow carries its detail here so RouteService can put it in PROBLEMS.
        public sealed class FittingFailureInfo
        {
            public int Index { get; set; }
            public double AngleDeg { get; set; }
            public string Detail { get; set; }
        }

        public static RouteResult Build(Document doc, ConnectorInfo s, ConnectorInfo t,
                                        IList<XYZ> pts, RouteOptions o,
                                        Action<LogLevel, string> log)
        {
            var result = new RouteResult();
            var factory = SegmentFactory.For(o);
            var segments = new List<MEPCurve>();

            // v4.8 (Task 2) – sizes and routing-preference elbow for per-elbow diagnostics
            double sizeFt = o.Size?.NominalFt ?? (s.Shape == ConnectorProfileType.Round ? s.Radius * 2 : 0);
            string sizeText = o.Size.HasValue ? $"{o.Size.Value.NominalMm:0.#} mm"
                : s.Shape == ConnectorProfileType.Round ? $"{UnitConv.Mm(s.Radius * 2)} mm (match source)" : "match source";
            string elbowName = GetRoutingPreferenceElbow(doc, o);

            for (int i = 0; i < pts.Count - 1; i++)
            {
                MEPCurve seg = factory.Create(doc, pts[i], pts[i + 1]);
                if (o.MatchSize) factory.ApplySize(seg, s);
                else if (o.Size.HasValue) ApplyNominalSize(seg, o.Size.Value.NominalFt, o.Discipline);
                segments.Add(seg);
                result.Created.Add(seg.Id);
            }
            result.Segments = segments.Count;
            doc.Regenerate();

            if (o.AddFittings)
            {
                // v4.8 (Task 2/3) – angle precheck: log every turn before creating any elbow
                for (int i = 1; i < segments.Count; i++)
                {
                    XYZ p1 = (pts[i] - pts[i - 1]).Normalize();
                    XYZ p2 = (pts[i + 1] - pts[i]).Normalize();
                    double preDeg = p1.AngleTo(p2) * 180.0 / Math.PI;
                    if (preDeg < StraightTolDeg) continue;
                    log(LogLevel.Info, $"Elbow precheck {i}: turn {preDeg:0.00}° at " +
                        $"({UnitConv.Mm(pts[i].X)}, {UnitConv.Mm(pts[i].Y)}, {UnitConv.Mm(pts[i].Z)}).");
                }

                for (int i = 1; i < segments.Count; i++)
                {
                    XYZ d1 = (pts[i] - pts[i - 1]).Normalize();
                    XYZ d2 = (pts[i + 1] - pts[i]).Normalize();
                    double turnDeg = d1.AngleTo(d2) * 180.0 / Math.PI;
                    if (turnDeg < StraightTolDeg) continue;

                    // v4.8 (Task 3) – sloped-run-to-vertical transitions need special attention
                    bool slopedToVertical = (Math.Abs(d1.Z) < 0.5 || Math.Abs(d2.Z) < 0.5)
                        && Math.Abs(Math.Abs(d1.Z) - Math.Abs(d2.Z)) > 0.01
                        && (Math.Abs(d1.Z) > VerticalDot || Math.Abs(d2.Z) > VerticalDot ||
                            (Math.Abs(d1.Z) > 0.01 && Math.Abs(d1.Z) < VerticalDot) ||
                            (Math.Abs(d2.Z) > 0.01 && Math.Abs(d2.Z) < VerticalDot));

                    var c1 = ConnectorUtils.GetNearestConnector(segments[i - 1], pts[i]);
                    var c2 = ConnectorUtils.GetNearestConnector(segments[i], pts[i]);
                    string where = $"({UnitConv.Mm(pts[i].X)}, {UnitConv.Mm(pts[i].Y)}, {UnitConv.Mm(pts[i].Z)})";
                    string lens = $"{UnitConv.Mm(pts[i - 1].DistanceTo(pts[i]))} / {UnitConv.Mm(pts[i].DistanceTo(pts[i + 1]))} mm";
                    string detail = $"angle {turnDeg:0.00}°, segments {lens}, size {sizeText}, elbow '{elbowName}'";

                    if (c1 == null || c2 == null)
                    {
                        result.FittingFailures++;
                        result.FittingFailureDetails.Add(new FittingFailureInfo
                            { Index = i, AngleDeg = turnDeg, Detail = $"connector not found ({detail})" });
                        log(LogLevel.Warn, $"Elbow {i} at {where}: connector not found – {detail}.");
                        continue;
                    }

                    try
                    {
                        var fit = doc.Create.NewElbowFitting(c1, c2);
                        result.Created.Add(fit.Id);
                        result.Fittings++;
                    }
                    catch (Exception ex)
                    {
                        result.FittingFailures++;
                        result.FittingFailureDetails.Add(new FittingFailureInfo
                            { Index = i, AngleDeg = turnDeg, Detail = $"{detail} – {ex.Message}" });
                        log(LogLevel.Warn, $"Elbow {i} failed at {where}: {detail} – {ex.Message}" +
                            (slopedToVertical ? " (sloped-to-vertical turn)" : ""));
                    }
                }
            }

            if (o.ConnectEnds && segments.Count > 0)
            {
                TryConnect(s.Resolve(doc), segments[0], pts[0], "source", log);
                TryConnect(t.Resolve(doc), segments[segments.Count - 1], pts[pts.Count - 1], "target", log);
            }

            result.Success = true;
            return result;
        }

        /// <summary>v4.8 (Task 3) – elbow family currently chosen by the type's routing preferences.</summary>
        private static string GetRoutingPreferenceElbow(Document doc, RouteOptions o)
        {
            try
            {
                if (o.Discipline != Discipline.Pipe) return "-";
                if (doc?.GetElement(o.TypeId) is not Autodesk.Revit.DB.Plumbing.PipeType pt) return "-";
                var rpm = pt.RoutingPreferenceManager;
                if (rpm == null) return "-";
                int n = rpm.GetNumberOfRules(RoutingPreferenceRuleGroupType.Elbows);
                for (int i = 0; i < n; i++)
                {
                    RoutingPreferenceRule rule = rpm.GetRule(RoutingPreferenceRuleGroupType.Elbows, i);
                    if (doc.GetElement(rule.MEPPartId) is Element e) return e.Name;
                }
            }
            catch { /* diagnostics only */ }
            return "-";
        }

        private static void ApplyNominalSize(MEPCurve curve, double diameterFt, Discipline discipline)
        {
            // v4.8 (ISS-007) – discipline-correct size parameters (duct / cable tray no longer use the pipe parameter)
            if (curve == null || diameterFt <= 0) return;
            switch (discipline)
            {
                case Discipline.Conduit:
                    SegmentFactory.SetParam(curve, BuiltInParameter.RBS_CONDUIT_DIAMETER_PARAM, diameterFt);
                    break;
                case Discipline.Duct:
                    SegmentFactory.SetParam(curve, BuiltInParameter.RBS_CURVE_DIAMETER_PARAM, diameterFt);
                    break;
                case Discipline.CableTray:
                    SegmentFactory.SetParam(curve, BuiltInParameter.RBS_CABLETRAY_WIDTH_PARAM, diameterFt);
                    SegmentFactory.SetParam(curve, BuiltInParameter.RBS_CABLETRAY_HEIGHT_PARAM, diameterFt);
                    break;
                default:
                    SegmentFactory.SetParam(curve, BuiltInParameter.RBS_PIPE_DIAMETER_PARAM, diameterFt);
                    break;
            }
        }

        private static void TryConnect(Connector equip, MEPCurve seg, XYZ at, string label,
                                       Action<LogLevel, string> log)
        {
            if (equip == null) { log(LogLevel.Warn, $"Could not resolve {label} connector."); return; }
            if (equip.IsConnected)
            {
                log(LogLevel.Warn, $"{label} connector already connected – skipped. " +
                                   "Delete pipes from a previous test or re-pick the connector.");
                return;
            }
            try
            {
                var c = ConnectorUtils.GetNearestConnector(seg, at);
                c.ConnectTo(equip);
                log(LogLevel.Info, $"Connected to {label} connector.");
            }
            catch (Exception ex)
            {
                log(LogLevel.Warn, $"Connect to {label} failed: {ex.Message}");
            }
        }
    }
}
