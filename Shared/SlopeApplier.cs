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
            => Apply(input, s, problems, null, false);

        /// <summary>
        /// v4.8 (Task 4) – targetDirection excludes the fixed end lead when the target connector faces
        /// downward into the target; strictGravity turns "rises in flow direction" into an Error.
        /// </summary>
        public static List<XYZ> Apply(IList<XYZ> input, SlopeSettings s, List<RouteProblem> problems,
                                      XYZ targetDirection, bool strictGravity)
            => Apply(input, s, problems, targetDirection, strictGravity, out _);

        /// <summary>
        /// v4.8.1 (Task 1) – additionally reports the actually applied fall (ft) and raises an
        /// Error (gravity) / Warning (non-gravity) when the route cannot absorb the required fall.
        /// </summary>
        public static List<XYZ> Apply(IList<XYZ> input, SlopeSettings s, List<RouteProblem> problems,
                                      XYZ targetDirection, bool strictGravity, out double appliedFallFt)
        {
            var pts = PathUtils.RemoveDuplicates(input);
            appliedFallFt = 0;
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

                // v4.8 (Task 1) – the connector leads (origin → lead, lead → origin) stay parallel to
                //                 the connector axis; only interior horizontal runs take the gradient.
                bool touchesStart = a == 0, touchesEnd = b == n - 1;
                bool aFixed = touchesStart, bFixed = touchesEnd;
                if (touchesStart && touchesEnd)
                {
                    // Whole path is a single horizontal run: both ends fixed -> total fall = 0, no error.
                    continue;
                }

                if (bFixed)
                {
                    double rise = 0;
                    for (int i = b - 1; i >= a; i--)
                    {
                        rise += HLen(i);
                        double target = pts[b].Z + rise * g;
                        if (aFixed && i == a) continue;   // do not tilt the source lead
                        z[i] = target;
                    }
                }
                else if (aFixed)
                {
                    double drop = 0;
                    for (int i = a + 1; i <= b; i++)
                    {
                        drop += HLen(i - 1);
                        z[i] = pts[a].Z - drop * g;
                    }
                }
                else
                {
                    double drop = 0;
                    for (int i = a + 1; i <= b; i++)
                    {
                        drop += HLen(i - 1);
                        z[i] = z[a] - drop * g;
                    }
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
                {
                    // v4.8 (Task 4) – exclude the fixed end lead when the target connector faces downward
                    int origIndex = reversed ? (segCount - 1 - i) : i;
                    bool isEndLead = origIndex == segCount - 1;
                    bool targetFacesDown = targetDirection != null && targetDirection.Z < -0.5;
                    if (isEndLead && targetFacesDown) continue;

                    string riseMsg = $"Segment {Label(i)} rises {Geom.FtToMm(newDz):0} mm in the flow direction";
                    if (strictGravity)
                        problems.Add(RouteProblem.Error(riseMsg + " – gravity drainage cannot flow uphill.", Label(i)));
                    else
                        problems.Add(RouteProblem.Warn(riseMsg + " (not recommended for gravity drainage).", Label(i)));
                }
            }

            var result = pts.Select((p, i) => new XYZ(p.X, p.Y, z[i])).ToList();
            if (reversed) result.Reverse();

            // v4.8.1 (Task 1) – report the actually applied fall and fail/warn when the route cannot absorb it
            appliedFallFt = result.Count < 2 ? 0 : Math.Abs(result[0].Z - result[result.Count - 1].Z);
            double interiorRunFt = 0;
            for (int i = 1; i < result.Count - 2; i++)   // interior segments only (leads excluded)
                if (Math.Abs(result[i + 1].Z - result[i].Z) < Tol)
                    interiorRunFt += Geom.DistanceXY(result[i], result[i + 1]);
            double requiredFall = interiorRunFt * g;

            if (requiredFall > Geom.MmToFt(1) && appliedFallFt < requiredFall - Geom.MmToFt(1))
            {
                string fallMm = Geom.FtToMm(requiredFall).ToString("0");
                if (strictGravity)
                    problems.Add(RouteProblem.Error(
                        $"Slope cannot be applied: no vertical segment to absorb {fallMm} mm fall."));
                else
                    problems.Add(RouteProblem.Warn(
                        $"Slope {s.Display.Split(' ')[0]} was not applied – route is flat (no vertical segment)."));
            }

            return result;
        }

        public static double TotalFall(IList<XYZ> sloped) =>
            sloped == null || sloped.Count < 2 ? 0 : Math.Abs(sloped[0].Z - sloped[^1].Z);
    }
}
