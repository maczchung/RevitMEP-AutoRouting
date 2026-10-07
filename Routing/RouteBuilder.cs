using System;
using System.Collections.Generic;
using Autodesk.Revit.DB;
using MEPAutoRouting.Shared;

namespace MEPAutoRouting.Routing
{
    /// <summary>
    /// Creates MEP curves + elbows along planned points. Caller owns the transaction.
    /// v4.4: elbow failures report position, turn angle and both segment lengths.
    /// </summary>
    public static class RouteBuilder
    {
        private const double StraightTolDeg = 0.5;

        public static RouteResult Build(Document doc, ConnectorInfo s, ConnectorInfo t,
                                        IList<XYZ> pts, RouteOptions o,
                                        Action<LogLevel, string> log)
        {
            var result = new RouteResult();
            var factory = SegmentFactory.For(o);
            var segments = new List<MEPCurve>();

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
                for (int i = 1; i < segments.Count; i++)
                {
                    XYZ d1 = (pts[i] - pts[i - 1]).Normalize();
                    XYZ d2 = (pts[i + 1] - pts[i]).Normalize();
                    double turnDeg = d1.AngleTo(d2) * 180.0 / Math.PI;
                    if (turnDeg < StraightTolDeg) continue;

                    var c1 = ConnectorUtils.GetNearestConnector(segments[i - 1], pts[i]);
                    var c2 = ConnectorUtils.GetNearestConnector(segments[i], pts[i]);
                    string where = $"({UnitConv.Mm(pts[i].X)}, {UnitConv.Mm(pts[i].Y)}, {UnitConv.Mm(pts[i].Z)})";
                    string lens = $"{UnitConv.Mm(pts[i - 1].DistanceTo(pts[i]))} / {UnitConv.Mm(pts[i].DistanceTo(pts[i + 1]))} mm";

                    if (c1 == null || c2 == null)
                    {
                        result.FittingFailures++;
                        log(LogLevel.Warn, $"Elbow {i} at {where}: connector not found.");
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
                        log(LogLevel.Warn,
                            $"Elbow {i} failed at {where}: turn {turnDeg:0.0}°, segments {lens} – {ex.Message}");
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

        private static void ApplyNominalSize(MEPCurve curve, double diameterFt, Discipline discipline)
        {
            BuiltInParameter parameter = discipline == Discipline.Conduit
                ? BuiltInParameter.RBS_CONDUIT_DIAMETER_PARAM
                : BuiltInParameter.RBS_PIPE_DIAMETER_PARAM;
            Parameter size = curve?.get_Parameter(parameter);
            if (size != null && !size.IsReadOnly && diameterFt > 0) size.Set(diameterFt);
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
