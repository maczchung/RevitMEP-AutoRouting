using System.Collections.Generic;
using Autodesk.Revit.DB;

namespace MEPAutoRouting
{
    /// <summary>路徑檢查、去重複點、合併同一直線上嘅點、6 方向 neighbour。</summary>
    public static class PathUtils
    {
        public static readonly (int dx, int dy, int dz)[] SixNeighbours =
        {
            ( 1, 0, 0), (-1, 0, 0), ( 0, 1, 0), ( 0,-1, 0), ( 0, 0, 1), ( 0, 0,-1),
        };

        public static bool IsValid(IList<XYZ> path) => path != null && path.Count >= 2;

        public static List<XYZ> RemoveDuplicates(IList<XYZ> pts, double tol = 1e-6)
        {
            var result = new List<XYZ>();
            if (pts == null) return result;
            foreach (XYZ p in pts)
                if (result.Count == 0 || result[^1].DistanceTo(p) > tol) result.Add(p);
            return result;
        }

        public static List<XYZ> MergeCollinear(IList<XYZ> input)
        {
            List<XYZ> pts = RemoveDuplicates(input);
            if (pts.Count < 3) return pts;
            var result = new List<XYZ> { pts[0] };
            for (int i = 1; i < pts.Count - 1; i++)
            {
                XYZ d1 = (pts[i] - result[^1]).Normalize();
                XYZ d2 = (pts[i + 1] - pts[i]).Normalize();
                if (!d1.IsAlmostEqualTo(d2)) result.Add(pts[i]);
            }
            result.Add(pts[^1]);
            return result;
        }

        public static double TurnPenalty((int dx, int dy, int dz) prevDir, (int dx, int dy, int dz) newDir,
                                         double penalty = 5.0)
            => prevDir == (0, 0, 0) || prevDir == newDir ? 0 : penalty;

        public static (int dx, int dy, int dz) Negate((int dx, int dy, int dz) d) => (-d.dx, -d.dy, -d.dz);
    }
}
