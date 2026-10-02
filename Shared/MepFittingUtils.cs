using System.Collections.Generic;
using Autodesk.Revit.DB;

namespace MEPAutoRouting
{
    /// <summary>用共用轉角點配對 connector 建 elbow，Pipe / Conduit 通用。要喺 Transaction 入面 call。</summary>
    public static class MepFittingUtils
    {
        public static Connector ClosestEndConnector(MEPCurve curve, XYZ pt)
        {
            if (curve == null || pt == null) return null;
            Connector best = null;
            double min = double.MaxValue;
            foreach (Connector c in curve.ConnectorManager.Connectors)
            {
                if (c.ConnectorType != ConnectorType.End || c.IsConnected) continue;
                double d = c.Origin.DistanceTo(pt);
                if (d < min) { min = d; best = c; }
            }
            return best;
        }

        /// <summary>segments[i] 由 path[i] 行到 path[i+1]；轉角點 = path[i+1]。</summary>
        public static int CreateElbows(Document doc, IList<MEPCurve> segments, IList<XYZ> path,
                                       IList<string> failures = null)
        {
            if (doc == null || segments == null || path == null) return 0;
            if (path.Count != segments.Count + 1)
            {
                failures?.Add($"Path has {path.Count} points but {segments.Count} segments (expected points = segments + 1).");
                return 0;
            }

            int created = 0;
            for (int i = 0; i < segments.Count - 1; i++)
            {
                MEPCurve a = segments[i], b = segments[i + 1];
                if (a == null || b == null) continue;

                XYZ d1 = (path[i + 1] - path[i]).Normalize();
                XYZ d2 = (path[i + 2] - path[i + 1]).Normalize();
                if (d1.IsAlmostEqualTo(d2)) continue;

                XYZ corner = path[i + 1];
                Connector c1 = ClosestEndConnector(a, corner);
                Connector c2 = ClosestEndConnector(b, corner);
                if (c1 == null || c2 == null) { failures?.Add($"Elbow at corner {i + 1}: connector not found."); continue; }

                try { doc.Create.NewElbowFitting(c1, c2); created++; }
                catch (Autodesk.Revit.Exceptions.InvalidOperationException ex) { failures?.Add($"Elbow at corner {i + 1} failed: {ex.Message}"); }
                catch (Autodesk.Revit.Exceptions.ArgumentException ex) { failures?.Add($"Elbow at corner {i + 1} failed: {ex.Message}"); }
            }
            return created;
        }
    }
}
