using System;
using System.Collections.Generic;
using Autodesk.Revit.DB;
using MEPAutoRouting.Shared;

namespace MEPAutoRouting.Routing
{
    /// <summary>Creates MEP curves + elbows along planned points. Caller owns the transaction.</summary>
    public static class RouteBuilder
    {
        public static RouteResult Build(Document doc, ConnectorInfo s, ConnectorInfo t,
                                        IList<XYZ> pts, RouteOptions o,
                                        Action<LogLevel, string> log)
        {
            var result = new RouteResult();
            var factory = SegmentFactory.For(o);
            var segments = new List<MEPCurve>();

            // 1. Segments
            for (int i = 0; i < pts.Count - 1; i++)
            {
                MEPCurve seg = factory.Create(doc, pts[i], pts[i + 1]);
                if (o.MatchSize) factory.ApplySize(seg, s);
                segments.Add(seg);
                result.Created.Add(seg.Id);
            }
            result.Segments = segments.Count;
            doc.Regenerate();

            // 2. Elbows
            if (o.AddFittings)
            {
                for (int i = 1; i < segments.Count; i++)
                {
                    var c1 = ConnectorUtils.GetNearestConnector(segments[i - 1], pts[i]);
                    var c2 = ConnectorUtils.GetNearestConnector(segments[i], pts[i]);
                    try
                    {
                        var fit = doc.Create.NewElbowFitting(c1, c2);
                        result.Created.Add(fit.Id);
                        result.Fittings++;
                    }
                    catch (Exception ex)
                    {
                        result.FittingFailures++;
                        log(LogLevel.Warn, $"Elbow {i} failed: {ex.Message} (check routing preferences / segment length)");
                    }
                }
            }

            // 3. End connections
            if (o.ConnectEnds && segments.Count > 0)
            {
                TryConnect(s.Resolve(doc), segments[0], pts[0], "source", log);
                TryConnect(t.Resolve(doc), segments[segments.Count - 1], pts[pts.Count - 1], "target", log);
            }

            result.Success = true;
            return result;
        }

        private static void TryConnect(Connector equip, MEPCurve seg, XYZ at, string label,
                                       Action<LogLevel, string> log)
        {
            if (equip == null) { log(LogLevel.Warn, $"Could not resolve {label} connector."); return; }
            if (equip.IsConnected) { log(LogLevel.Warn, $"{label} connector already connected – skipped."); return; }
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
