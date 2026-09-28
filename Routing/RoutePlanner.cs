using System;
using System.Collections.Generic;
using Autodesk.Revit.DB;
using MEPAutoRouting.Shared;

namespace MEPAutoRouting.Routing
{
    /// <summary>
    /// Orthogonal (Manhattan) path planner between two connectors.
    /// Pure geometry – no transaction needed.
    /// </summary>
    public static class RoutePlanner
    {
        private const double Tol = 1e-4; // ft

        public static List<XYZ> Plan(ConnectorInfo s, ConnectorInfo t, RouteOptions o,
                                     double levelElevationFt, List<string> problems)
        {
            double lead = UnitConv.MmToFt(Math.Max(0, o.LeadMm));
            XYZ a0 = s.Origin, b0 = t.Origin;
            XYZ a1 = a0 + s.Direction * lead;
            XYZ b1 = b0 + t.Direction * lead;

            var pts = new List<XYZ> { a0, a1 };

            switch (o.Strategy)
            {
                case RouteStrategy.XThenY:
                    pts.Add(new XYZ(b1.X, a1.Y, a1.Z));
                    pts.Add(new XYZ(b1.X, b1.Y, a1.Z));
                    break;
                case RouteStrategy.YThenX:
                    pts.Add(new XYZ(a1.X, b1.Y, a1.Z));
                    pts.Add(new XYZ(b1.X, b1.Y, a1.Z));
                    break;
                case RouteStrategy.VerticalFirst:
                    pts.Add(new XYZ(a1.X, a1.Y, b1.Z));
                    pts.Add(new XYZ(b1.X, a1.Y, b1.Z));
                    break;
                case RouteStrategy.AtElevation:
                    double z = levelElevationFt + UnitConv.MmToFt(o.ElevationMm);
                    pts.Add(new XYZ(a1.X, a1.Y, z));
                    pts.Add(new XYZ(b1.X, a1.Y, z));
                    pts.Add(new XYZ(b1.X, b1.Y, z));
                    break;
            }

            pts.Add(b1);
            pts.Add(b0);

            var found = new List<string>();
            var clean = Simplify(pts, found);
            Validate(clean, o, found);
            foreach (var p in found)
                if (!problems.Contains(p)) problems.Add(p);
            return clean;
        }

        /// <summary>Removes duplicate and collinear points; flags U-turns.</summary>
        private static List<XYZ> Simplify(List<XYZ> pts, List<string> problems)
        {
            var dedup = new List<XYZ>();
            foreach (var p in pts)
                if (dedup.Count == 0 || dedup[dedup.Count - 1].DistanceTo(p) > Tol)
                    dedup.Add(p);

            bool changed = true;
            while (changed && dedup.Count > 2)
            {
                changed = false;
                for (int i = 1; i < dedup.Count - 1; i++)
                {
                    XYZ d1 = (dedup[i] - dedup[i - 1]).Normalize();
                    XYZ d2 = (dedup[i + 1] - dedup[i]).Normalize();
                    if (d1.CrossProduct(d2).GetLength() < 1e-6)
                    {
                        if (d1.DotProduct(d2) > 0)
                        {
                            dedup.RemoveAt(i);
                            changed = true;
                            break;
                        }
                        problems.Add($"U-turn at point {i} – reduce lead length or change strategy.");
                    }
                }
            }
            return dedup;
        }

        private static void Validate(List<XYZ> pts, RouteOptions o, List<string> problems)
        {
            double min = UnitConv.MmToFt(o.MinSegmentMm);
            for (int i = 0; i < pts.Count - 1; i++)
            {
                double len = pts[i].DistanceTo(pts[i + 1]);
                if (len < min)
                    problems.Add($"Segment {i + 1} is {UnitConv.Mm(len)} mm (< {o.MinSegmentMm:0} mm) – elbow may fail.");

                XYZ d = (pts[i + 1] - pts[i]).Normalize();
                bool ortho = Math.Abs(d.X) > 0.9999 || Math.Abs(d.Y) > 0.9999 || Math.Abs(d.Z) > 0.9999;
                if (!ortho)
                    problems.Add($"Segment {i + 1} is not axis-aligned (connector not orthogonal).");
            }
        }

        public static string DirectionLabel(XYZ a, XYZ b)
        {
            XYZ d = b - a;
            double ax = Math.Abs(d.X), ay = Math.Abs(d.Y), az = Math.Abs(d.Z);
            if (az >= ax && az >= ay) return d.Z > 0 ? "▲ Up" : "▼ Down";
            if (ax >= ay) return d.X > 0 ? "→ +X" : "← -X";
            return d.Y > 0 ? "↑ +Y" : "↓ -Y";
        }
    }
}
