using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;

namespace MEPAutoRouting
{
    /// <summary>
    /// Applies the gradient to an A* path (after MergeCollinear).
    /// v4.8.2 / v4.8.3 rules:
    ///  • Both connector origins are fixed.
    ///  • Horizontal connector leads (first / last segment) stay flat and parallel to the connector axis.
    ///    Vertical leads may change length (they stay parallel to the connector axis).
    ///  • Lead points absorbed by MergeCollinear are re-inserted first, so a lead stays flat while the
    ///    run beyond it takes the gradient.
    ///  • Interior horizontal runs take the gradient; the fall is absorbed by adjacent vertical segments.
    ///  • A run ending at a fixed horizontal target lead is anchored at its end (upstream rises);
    ///    otherwise it is anchored at its start (downstream falls).
    ///  • A run next to an upward riser (flow direction) is kept flat: sloping it needs a > 90° elbow.
    ///  • A run fixed at both ends (horizontal leads, no vertical segment) cannot absorb the fall.
    ///  • Forced connector leads are excluded from the "rises in the flow direction" check.
    /// </summary>
    public static class SlopeApplier
    {
        private const double Tol = 1e-6;
        private const double OnSegmentTol = 1e-5;

        public static List<XYZ> Apply(IList<XYZ> input, SlopeSettings s, List<RouteProblem> problems)
            => Apply(input, s, problems, null, null, false, out _);

        /// <summary>Kept for compatibility. targetDirection is no longer needed (leads are always excluded).</summary>
        public static List<XYZ> Apply(IList<XYZ> input, SlopeSettings s, List<RouteProblem> problems,
                                      XYZ targetDirection, bool strictGravity)
            => Apply(input, s, problems, null, null, strictGravity, out _);

        /// <summary>Kept for compatibility. targetDirection is no longer needed (leads are always excluded).</summary>
        public static List<XYZ> Apply(IList<XYZ> input, SlopeSettings s, List<RouteProblem> problems,
                                      XYZ targetDirection, bool strictGravity, out double appliedFallFt)
            => Apply(input, s, problems, null, null, strictGravity, out appliedFallFt);

        /// <summary>
        /// v4.8.3 – main entry. sourceLead / targetLead are the lead points of the two connectors
        /// (ConnectorEndpoint.Lead); they may be null. appliedFallFt = sum(sloped run length) × gradient.
        /// </summary>
        public static List<XYZ> Apply(IList<XYZ> input, SlopeSettings s, List<RouteProblem> problems,
                                      XYZ sourceLead, XYZ targetLead, bool strictGravity, out double appliedFallFt)
        {
            appliedFallFt = 0;
            List<XYZ> pts = PathUtils.RemoveDuplicates(input).ToList();
            if (s == null || !s.Enabled || s.Gradient <= 0 || pts.Count < 2) return pts;

            // v4.8.3 – re-insert lead points that MergeCollinear absorbed into the first / last segment
            InsertOnSegment(pts, 0, sourceLead);
            InsertOnSegment(pts, pts.Count - 2, targetLead);

            bool reversed = s.Flow == FlowDirection.TargetToSource;
            if (reversed) pts.Reverse();

            int n = pts.Count, segCount = n - 1;
            double g = s.Gradient;
            double minV = Geom.MmToFt(s.MinVerticalMm);
            double elbowDeg = 90.0 + Math.Atan(g) * 180.0 / Math.PI;
            double[] z = pts.Select(p => p.Z).ToArray();

            bool IsHoriz(int i) => Math.Abs(pts[i + 1].Z - pts[i].Z) < Tol;
            bool IsUp(int i) => i >= 0 && i < segCount && pts[i + 1].Z - pts[i].Z > Tol;
            bool IsLead(int i) => i == 0 || i == segCount - 1;
            bool Slopeable(int i) => !IsLead(i) && IsHoriz(i);
            double HLen(int i) => Geom.DistanceXY(pts[i], pts[i + 1]);
            int Label(int seg) => reversed ? (segCount - 1 - seg) : seg;

            double slopedRunFt = 0;
            int k = 0;
            while (k < segCount)
            {
                if (!Slopeable(k)) { k++; continue; }
                int a = k;
                while (k < segCount && Slopeable(k)) k++;
                int b = k;                                   // run = points a..b, segments a..b-1

                double runFt = 0;
                for (int i = a; i < b; i++) runFt += HLen(i);

                bool startFixed = a == 1 && IsHoriz(0);                         // flat source lead before the run
                bool endFixed = b == segCount - 1 && IsHoriz(segCount - 1);   // flat target lead after the run

                // v4.8.1 / v4.8.3 – fixed at both ends: no vertical segment can absorb the fall (R-13)
                if (startFixed && endFixed)
                {
                    string fallMm = Geom.FtToMm(runFt * g).ToString("0");
                    if (strictGravity)
                        problems.Add(RouteProblem.Error(
                            $"Slope cannot be applied: no vertical segment to absorb {fallMm} mm fall."));
                    else
                        problems.Add(RouteProblem.Warn(
                            $"Slope {SlopeLabel(s)} was not applied – route is flat (no vertical segment)."));
                    continue;
                }

                // v4.8.3 (Task 5) – sloping next to an upward riser always gives 90° + atan(g) > 90°
                if (IsUp(a - 1) || IsUp(b))
                {
                    string where = IsUp(a - 1) ? "after" : "before";
                    if (strictGravity)
                        problems.Add(RouteProblem.Error(
                            $"Run {Label(a)} is {where} an upward riser – sloping it needs a {elbowDeg:0.00}° elbow; " +
                            "gravity drainage cannot be routed this way.", Label(a)));
                    else
                        problems.Add(RouteProblem.Warn(
                            $"Run {Label(a)} {where} an upward riser was kept flat to avoid a {elbowDeg:0.00}° elbow.",
                            Label(a)));
                    continue;
                }

                if (endFixed)
                {
                    // anchored at the target lead: upstream rises, the vertical before the run absorbs it
                    for (int i = b - 1; i >= a; i--) z[i] = z[i + 1] + HLen(i) * g;
                }
                else
                {
                    // anchored at the run start: downstream falls, the vertical after the run absorbs it
                    for (int i = a + 1; i <= b; i++) z[i] = z[i - 1] - HLen(i - 1) * g;
                }
                slopedRunFt += runFt;
            }

            for (int i = 0; i < segCount; i++)
            {
                if (IsHoriz(i)) continue;
                double oldDz = pts[i + 1].Z - pts[i].Z;
                double newDz = z[i + 1] - z[i];
                bool changed = Math.Abs(newDz - oldDz) > Tol;

                if (Math.Sign(oldDz) != Math.Sign(newDz) || (changed && Math.Abs(newDz) < minV))
                {
                    problems.Add(RouteProblem.Error(
                        $"Vertical segment {Label(i)} is only {Geom.FtToMm(Math.Abs(newDz)):0} mm after slope (min {s.MinVerticalMm:0} mm). " +
                        "Raise the source, lower the run or reduce the slope.", Label(i)));
                }
                else if (newDz > 0 && !IsLead(i))
                {
                    // v4.8.2 (Task 2) – forced connector leads are not checked; only interior risers
                    string riseMsg = $"Segment {Label(i)} rises {Geom.FtToMm(newDz):0} mm in the flow direction";
                    if (strictGravity)
                        problems.Add(RouteProblem.Error(riseMsg + " – gravity drainage cannot flow uphill.", Label(i)));
                    else
                        problems.Add(RouteProblem.Warn(riseMsg + " (not recommended for gravity drainage).", Label(i)));
                }
            }

            List<XYZ> result = pts.Select((p, i) => new XYZ(p.X, p.Y, z[i])).ToList();
            if (reversed) result.Reverse();

            appliedFallFt = slopedRunFt * g;
            return MergeExactCollinear(result);
        }

        public static double TotalFall(IList<XYZ> sloped) =>
            sloped == null || sloped.Count < 2 ? 0 : Math.Abs(sloped[0].Z - sloped[^1].Z);

        // ------------------------------------------------------------------ helpers

        private static string SlopeLabel(SlopeSettings s)
        {
            string d = s.Display ?? "";
            int sp = d.IndexOf(' ');
            return sp > 0 ? d.Substring(0, sp) : d;
        }

        /// <summary>Insert p into segment seg if it lies strictly inside that segment.</summary>
        private static void InsertOnSegment(List<XYZ> pts, int seg, XYZ p)
        {
            if (p == null || seg < 0 || seg >= pts.Count - 1) return;
            XYZ a = pts[seg], b = pts[seg + 1];
            if (p.DistanceTo(a) < OnSegmentTol * 10 || p.DistanceTo(b) < OnSegmentTol * 10) return;
            if (Math.Abs(a.DistanceTo(p) + p.DistanceTo(b) - a.DistanceTo(b)) > OnSegmentTol) return;
            pts.Insert(seg + 1, p);
        }

        /// <summary>
        /// Remove only EXACTLY collinear middle points. A flat lead followed by a 1:40 run differs by ~1.4°
        /// and must not be merged into one skewed segment, so a loose collinear tolerance is not used here.
        /// </summary>
        private static List<XYZ> MergeExactCollinear(List<XYZ> pts)
        {
            if (pts.Count < 3) return pts;
            var outList = new List<XYZ> { pts[0] };
            for (int i = 1; i < pts.Count - 1; i++)
            {
                XYZ v1 = pts[i] - outList[^1];
                XYZ v2 = pts[i + 1] - pts[i];
                if (v1.GetLength() < Tol) continue;
                if (v2.GetLength() < Tol) { outList.Add(pts[i]); continue; }
                if ((v1.Normalize() - v2.Normalize()).GetLength() < 1e-9) continue;
                outList.Add(pts[i]);
            }
            outList.Add(pts[^1]);
            return outList;
        }
    }
}
