using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;

namespace MEPAutoRouting
{
    /// <summary>
    /// Space / Room 嘅 routing 範圍（world 座標）。
    /// 用 boundary polygon 判斷，所以 L 形 / 唔規則嘅 Space 都啱，唔只係 bounding box。
    /// Host 同 Linked model 都用得（Linked 就傳 link transform）。
    /// </summary>
    public sealed class SpaceVolume
    {
        public string Name { get; private set; } = "";
        public bool IsFromLink { get; private set; }
        public List<List<XYZ>> Loops { get; } = new();   // world XY polygon（第一個 loop 係外框，其餘係洞）
        public double MinZ { get; private set; }
        public double MaxZ { get; private set; }
        public XYZ Min { get; private set; }             // world bounding box
        public XYZ Max { get; private set; }

        public static SpaceVolume FromSpatialElement(SpatialElement se, Transform linkTransform = null)
        {
            if (se == null) throw new ArgumentNullException(nameof(se));
            if (se.Area <= 0)
                throw new InvalidOperationException($"'{se.Name}' 未 placed 或者冇封閉邊界 (Area = 0)。");

            Transform t = linkTransform ?? Transform.Identity;

            // ---- 高度 (local) ----
            double localMinZ, localMaxZ;
            BoundingBoxXYZ bb = se.get_BoundingBox(null);
            if (bb != null && bb.Max.Z - bb.Min.Z > 1e-6)
            {
                var (bmin, bmax) = BoundingBoxUtils.GetWorldBounds(bb);   // 只處理 bb.Transform，未套 link
                localMinZ = bmin.Z; localMaxZ = bmax.Z;
            }
            else
            {
                double baseElev = se.Level?.Elevation ?? 0;
                double baseOffset = se.get_Parameter(BuiltInParameter.ROOM_LOWER_OFFSET)?.AsDouble() ?? 0;
                double height = se.get_Parameter(BuiltInParameter.ROOM_HEIGHT)?.AsDouble() ?? 10.0;
                localMinZ = baseElev + baseOffset;
                localMaxZ = localMinZ + height;
            }

            // ---- 邊界 ----
            var opt = new SpatialElementBoundaryOptions
            {
                SpatialElementBoundaryLocation = SpatialElementBoundaryLocation.Finish
            };
            IList<IList<BoundarySegment>> loops = se.GetBoundarySegments(opt);
            if (loops == null || loops.Count == 0)
                throw new InvalidOperationException($"'{se.Name}' 攞唔到邊界 (Not Enclosed?)。");

            var vol = new SpaceVolume
            {
                Name = $"{se.Number} - {se.Name}",
                IsFromLink = linkTransform != null
            };

            foreach (IList<BoundarySegment> loop in loops)
            {
                var poly = new List<XYZ>();
                foreach (BoundarySegment seg in loop)
                {
                    IList<XYZ> pts = seg.GetCurve().Tessellate();
                    for (int i = 0; i < pts.Count - 1; i++)          // 最後一點 = 下一段起點
                    {
                        XYZ w = t.OfPoint(new XYZ(pts[i].X, pts[i].Y, localMinZ));
                        poly.Add(new XYZ(w.X, w.Y, 0));
                    }
                }
                if (poly.Count >= 3) vol.Loops.Add(poly);
            }
            if (vol.Loops.Count == 0)
                throw new InvalidOperationException($"'{se.Name}' 邊界無效。");

            // Z：假設 link 冇傾斜（正常情況），只套 Z 位移
            double zShift = t.OfPoint(XYZ.Zero).Z;
            vol.MinZ = localMinZ + zShift;
            vol.MaxZ = localMaxZ + zShift;

            var all = vol.Loops.SelectMany(l => l).ToList();
            vol.Min = new XYZ(all.Min(p => p.X), all.Min(p => p.Y), vol.MinZ);
            vol.Max = new XYZ(all.Max(p => p.X), all.Max(p => p.Y), vol.MaxZ);
            return vol;
        }

        /// <summary>
        /// 點係咪喺 Space 入面，而且離邊界最少 insetXY（例如 pipe 半徑 + 牆間距）。
        /// </summary>
        public bool Contains(XYZ p, double insetXY = 0, double insetZ = 0)
        {
            if (p.Z < MinZ + insetZ || p.Z > MaxZ - insetZ) return false;
            if (p.X < Min.X || p.X > Max.X || p.Y < Min.Y || p.Y > Max.Y) return false;

            // Even-odd rule：自動處理 Space 入面嘅洞（例如柱）
            bool inside = false;
            foreach (var poly in Loops)
            {
                for (int i = 0, j = poly.Count - 1; i < poly.Count; j = i++)
                {
                    XYZ a = poly[i], b = poly[j];
                    if ((a.Y > p.Y) != (b.Y > p.Y) &&
                        p.X < (b.X - a.X) * (p.Y - a.Y) / (b.Y - a.Y) + a.X)
                        inside = !inside;
                }
            }
            if (!inside) return false;
            if (insetXY <= 0) return true;

            foreach (var poly in Loops)
                for (int i = 0, j = poly.Count - 1; i < poly.Count; j = i++)
                    if (GeomXY.DistanceToSegment(p, poly[j], poly[i]) < insetXY) return false;
            return true;
        }
    }

    internal static class GeomXY
    {
        public static double DistanceToSegment(XYZ p, XYZ a, XYZ b)
        {
            double dx = b.X - a.X, dy = b.Y - a.Y;
            double len2 = dx * dx + dy * dy;
            double t = len2 < 1e-12 ? 0 : ((p.X - a.X) * dx + (p.Y - a.Y) * dy) / len2;
            t = Math.Max(0, Math.Min(1, t));
            double cx = a.X + t * dx - p.X, cy = a.Y + t * dy - p.Y;
            return Math.Sqrt(cx * cx + cy * cy);
        }

        public static double Distance(XYZ p, XYZ q)
        {
            double dx = p.X - q.X, dy = p.Y - q.Y;
            return Math.Sqrt(dx * dx + dy * dy);
        }
    }
}
