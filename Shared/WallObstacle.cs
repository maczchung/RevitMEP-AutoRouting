using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;

namespace MEPAutoRouting
{
    /// <summary>牆（world 座標），用中心線 + 半厚度計距離：斜牆 / 弧形牆都準。</summary>
    public sealed class WallObstacle
    {
        public ElementId Id { get; init; }
        public bool FromLink { get; init; }
        public Curve CurveWorld { get; init; }
        public double HalfWidth { get; init; }
        public double MinZ { get; init; }
        public double MaxZ { get; init; }
        public XYZ BBMin { get; init; }
        public XYZ BBMax { get; init; }

        public double DistanceXY(XYZ p)
        {
            if (CurveWorld is Line ln)
                return Geom.DistanceToSegmentXY(p, ln.GetEndPoint(0), ln.GetEndPoint(1));
            XYZ q = new XYZ(p.X, p.Y, CurveWorld.GetEndPoint(0).Z);
            IntersectionResult ir = CurveWorld.Project(q);
            return ir == null ? double.MaxValue : Geom.DistanceXY(ir.XYZPoint, p);
        }

        public bool NearBox(XYZ p, double margin, double marginZ)
            => p.X >= BBMin.X - margin && p.X <= BBMax.X + margin &&
               p.Y >= BBMin.Y - margin && p.Y <= BBMax.Y + margin &&
               p.Z >= MinZ - marginZ && p.Z <= MaxZ + marginZ;
    }

    public static class WallObstacleCollector
    {
        public static List<WallObstacle> Collect(Document doc, XYZ regionMin, XYZ regionMax,
                                                 bool includeLinks, double margin)
        {
            var list = new List<WallObstacle>();
            AddFrom(doc, null, false, regionMin, regionMax, margin, list);
            if (includeLinks)
                foreach (RevitLinkInstance li in new FilteredElementCollector(doc)
                             .OfClass(typeof(RevitLinkInstance)).Cast<RevitLinkInstance>())
                {
                    Document ld = li.GetLinkDocument();
                    if (ld != null) AddFrom(ld, li.GetTotalTransform(), true, regionMin, regionMax, margin, list);
                }
            return list;
        }

        private static void AddFrom(Document src, Transform t, bool fromLink,
                                    XYZ rMin, XYZ rMax, double margin, List<WallObstacle> list)
        {
            foreach (Wall w in new FilteredElementCollector(src)
                         .OfClass(typeof(Wall)).WhereElementIsNotElementType().Cast<Wall>())
            {
                if (w.Location is not LocationCurve lc || lc.Curve == null) continue;
                BoundingBoxXYZ bb = w.get_BoundingBox(null);
                if (bb == null) continue;

                var (min, max) = BoundingBoxUtils.GetWorldBounds(bb, t);
                if (max.X < rMin.X - margin || min.X > rMax.X + margin ||
                    max.Y < rMin.Y - margin || min.Y > rMax.Y + margin ||
                    max.Z < rMin.Z - margin || min.Z > rMax.Z + margin) continue;

                list.Add(new WallObstacle
                {
                    Id = w.Id,
                    FromLink = fromLink,
                    CurveWorld = t == null ? lc.Curve : lc.Curve.CreateTransformed(t),
                    HalfWidth = Math.Max(w.Width, 0) / 2.0,
                    MinZ = min.Z, MaxZ = max.Z, BBMin = min, BBMax = max
                });
            }
        }
    }
}
