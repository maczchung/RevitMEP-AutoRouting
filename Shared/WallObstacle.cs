using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;

namespace MEPAutoRouting
{
    /// <summary>
    /// 牆（world 座標）。用中心線 + 半厚度計距離，所以斜牆 / 弧形牆都準，
    /// 唔會好似 bounding box 咁將斜牆成個長方形當障礙物。
    /// </summary>
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

        /// <summary>點（XY）去牆中心線嘅距離。</summary>
        public double DistanceXY(XYZ p)
        {
            if (CurveWorld is Line ln)
                return GeomXY.DistanceToSegment(p, ln.GetEndPoint(0), ln.GetEndPoint(1));

            // Arc / 其他曲線：投影去 curve 所在高度
            XYZ q = new XYZ(p.X, p.Y, CurveWorld.GetEndPoint(0).Z);
            IntersectionResult ir = CurveWorld.Project(q);
            return ir == null ? double.MaxValue : GeomXY.Distance(ir.XYZPoint, p);
        }

        /// <summary>快速 bbox 檢查（XY 擴大 margin，Z 擴大 marginZ）。</summary>
        public bool NearBox(XYZ p, double margin, double marginZ)
            => p.X >= BBMin.X - margin && p.X <= BBMax.X + margin &&
               p.Y >= BBMin.Y - margin && p.Y <= BBMax.Y + margin &&
               p.Z >= MinZ - marginZ && p.Z <= MaxZ + marginZ;
    }

    public static class WallObstacleCollector
    {
        /// <summary>
        /// 收集 routing 範圍 (regionMin/regionMax, 擴大 margin) 附近嘅牆。
        /// includeLinks = true 會包括所有 loaded Revit link 嘅牆。
        /// </summary>
        public static List<WallObstacle> Collect(Document doc, XYZ regionMin, XYZ regionMax,
                                                 bool includeLinks, double margin)
        {
            var list = new List<WallObstacle>();
            AddFrom(doc, null, false, regionMin, regionMax, margin, list);

            if (includeLinks)
            {
                foreach (RevitLinkInstance li in new FilteredElementCollector(doc)
                             .OfClass(typeof(RevitLinkInstance)).Cast<RevitLinkInstance>())
                {
                    Document ld = li.GetLinkDocument();
                    if (ld == null) continue;            // Unloaded link
                    AddFrom(ld, li.GetTotalTransform(), true, regionMin, regionMax, margin, list);
                }
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

                Curve c = t == null ? lc.Curve : lc.Curve.CreateTransformed(t);
                list.Add(new WallObstacle
                {
                    Id = w.Id,
                    FromLink = fromLink,
                    CurveWorld = c,
                    HalfWidth = Math.Max(w.Width, 0) / 2.0,   // Curtain wall 可能係 0
                    MinZ = min.Z,
                    MaxZ = max.Z,
                    BBMin = min,
                    BBMax = max
                });
            }
        }
    }
}
