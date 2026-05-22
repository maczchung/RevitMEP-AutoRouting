using Autodesk.Revit.DB;
using System.Collections.Generic;

namespace MEPAutoRouting.Shared
{
    public static class SimplePath
    {
        public static List<XYZ> GenerateLShape(XYZ start, XYZ end)
        {
            List<XYZ> pts = new List<XYZ>();
            pts.Add(start);

            XYZ mid1 = new XYZ(end.X, start.Y, start.Z);
            XYZ mid2 = new XYZ(end.X, end.Y, start.Z);

            if (pts[pts.Count - 1].DistanceTo(mid1) > 0.001) pts.Add(mid1);
            if (pts[pts.Count - 1].DistanceTo(mid2) > 0.001) pts.Add(mid2);
            if (pts[pts.Count - 1].DistanceTo(end) > 0.001) pts.Add(end);

            return pts;
        }
    }
}
