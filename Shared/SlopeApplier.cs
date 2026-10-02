using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;

namespace MEPAutoRouting
{
    /// <summary>
    /// 將坡度套落 A* 路徑（MergeCollinear 之後）。
    ///  • 兩端 connector Origin 固定唔郁。
    ///  • 路徑按垂直段切成「水平 run」。
    ///  • 最後一個 run 如果接住終點：由終點向上游計（終點固定，上游升高）。
    ///  • 其他 run：以 run 上游起點做錨，向下游落。
    ///  • 高度差由相鄰垂直段吸收；垂直段唔夠長或者反轉 → Error。
    /// </summary>
    public static class SlopeApplier
    {
        private const double Tol = 1e-6;

        public static List<XYZ> Apply(IList<XYZ> input, SlopeSettings s, List<RouteProblem> problems)
        {
            var pts = PathUtils.RemoveDuplicates(input);
            if (s == null || !s.Enabled || s.Gradient <= 0 || pts.Count < 2) return pts;

            bool reversed = s.Flow == FlowDirection.TargetToSource;
            if (reversed) pts.Reverse();

            int n = pts.Count;
            double g = s.Gradient;
            double minV = Geom.MmToFt(s.MinVerticalMm);
            double[] z = pts.Select(p => p.Z).ToArray();

            bool IsHoriz(int i) => Math.Abs(pts[i + 1].Z - pts[i].Z) < Tol;
            double HLen(int i) => Geom.DistanceXY(pts[i], pts[i + 1]);
            int Label(int segIndex) => reversed ? (n - 2 - segIndex) : segIndex;

            int k = 0, segCount = n - 1;
            while (k < segCount)
            {
                if (!IsHoriz(k)) { k++; continue; }
                int a = k;
                while (k < segCount && IsHoriz(k)) k++;
                int b = k;

                bool touchesStart = a == 0, touchesEnd = b == n - 1;
                if (touchesStart && touchesEnd)
                {
                    double total = Enumerable.Range(a, b - a).Sum(HLen) * g;
                    problems.Add(RouteProblem.Error(
                        $"The whole route is horizontal with both ends fixed – there is no vertical segment to take up the fall (about {Geom.FtToMm(total):0} mm needed)."));
                    continue;
                }

                if (touchesEnd)
                {
                    double rise = 0;
                    for (int i = b - 1; i >= a; i--) { rise += HLen(i); z[i] = pts[b].Z + rise * g; }
                }
                else
                {
                    double drop = 0;
                    for (int i = a + 1; i <= b; i++) { drop += HLen(i - 1); z[i] = pts[a].Z - drop * g; }
                }
            }

            for (int i = 0; i < segCount; i++)
            {
                if (IsHoriz(i)) continue;
                double oldDz = pts[i + 1].Z - pts[i].Z;
                double newDz = z[i + 1] - z[i];
                if (Math.Sign(oldDz) != Math.Sign(newDz) || Math.Abs(newDz) < minV)
                    problems.Add(RouteProblem.Error(
                        $"Vertical segment {Label(i)} is only {Geom.FtToMm(Math.Abs(newDz)):0} mm after slope (min {s.MinVerticalMm:0} mm). " +
                        "Raise the source, lower the run or reduce the slope.", Label(i)));
                else if (newDz > 0)
                    problems.Add(RouteProblem.Warn(
                        $"Segment {Label(i)} rises in the flow direction (not recommended for gravity drainage).", Label(i)));
            }

            var result = pts.Select((p, i) => new XYZ(p.X, p.Y, z[i])).ToList();
            if (reversed) result.Reverse();
            return result;
        }

        public static double TotalFall(IList<XYZ> sloped) =>
            sloped == null || sloped.Count < 2 ? 0 : Math.Abs(sloped[0].Z - sloped[^1].Z);
    }
}
