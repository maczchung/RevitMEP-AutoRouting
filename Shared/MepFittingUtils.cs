using System.Collections.Generic;
using Autodesk.Revit.DB;

namespace MEPAutoRouting
{
    /// <summary>
    /// Fix #2 + #4: 用共用轉角點配對 connector，Pipe 同 Conduit 通用。
    /// 必須喺已開啟嘅 Transaction 入面 call。
    /// </summary>
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
                failures?.Add($"Path 點數 ({path.Count}) 應該 = 段數 ({segments.Count}) + 1");
                return 0;
            }

            int created = 0;
            for (int i = 0; i < segments.Count - 1; i++)
            {
                MEPCurve a = segments[i], b = segments[i + 1];
                if (a == null || b == null) continue;

                XYZ d1 = (path[i + 1] - path[i]).Normalize();
                XYZ d2 = (path[i + 2] - path[i + 1]).Normalize();
                if (d1.IsAlmostEqualTo(d2)) continue;            // 直線唔使 elbow

                XYZ corner = path[i + 1];
                Connector c1 = ClosestEndConnector(a, corner);
                Connector c2 = ClosestEndConnector(b, corner);
                if (c1 == null || c2 == null) { failures?.Add($"轉角 {i + 1}: 搵唔到 connector"); continue; }

                try { doc.Create.NewElbowFitting(c1, c2); created++; }
                catch (Autodesk.Revit.Exceptions.InvalidOperationException ex) { failures?.Add($"轉角 {i + 1}: {ex.Message}"); }
                catch (Autodesk.Revit.Exceptions.ArgumentException ex) { failures?.Add($"轉角 {i + 1}: {ex.Message}"); }
            }
            return created;
        }
    }
}
