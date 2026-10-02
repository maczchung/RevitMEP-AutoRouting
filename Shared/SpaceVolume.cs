using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Mechanical;

namespace MEPAutoRouting
{
    /// <summary>Space / Room 範圍。用 boundary polygon 判斷（L 形都啱），Host / Linked 都用得。</summary>
    public sealed class SpaceVolume : IRoutingBoundary
    {
        public string Name { get; private set; } = "";
        public string Kind { get; private set; } = "Space";
        public bool IsFromLink { get; private set; }
        public List<List<XYZ>> Loops { get; } = new();
        public double MinZ { get; private set; }
        public double MaxZ { get; private set; }
        public XYZ Min { get; private set; }
        public XYZ Max { get; private set; }

        public static SpaceVolume FromSpatialElement(SpatialElement se, Transform linkTransform = null)
        {
            if (se == null) throw new ArgumentNullException(nameof(se));
            string kind = se is Space ? "Space" : "Room";
            if (se.Area <= 0)
                throw new InvalidOperationException($"{kind} '{se.Name}' is not placed or not enclosed (area = 0).");

            Transform t = linkTransform ?? Transform.Identity;

            double localMinZ, localMaxZ;
            BoundingBoxXYZ bb = se.get_BoundingBox(null);
            if (bb != null && bb.Max.Z - bb.Min.Z > 1e-6)
            {
                var (bmin, bmax) = BoundingBoxUtils.GetWorldBounds(bb);
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

            var opt = new SpatialElementBoundaryOptions
            {
                SpatialElementBoundaryLocation = SpatialElementBoundaryLocation.Finish
            };
            IList<IList<BoundarySegment>> loops = se.GetBoundarySegments(opt);
            if (loops == null || loops.Count == 0)
                throw new InvalidOperationException($"Cannot read the boundary of {kind} '{se.Name}' (not enclosed?).");

            var vol = new SpaceVolume
            {
                Name = $"{se.Number} - {se.Name}",
                Kind = kind,
                IsFromLink = linkTransform != null
            };

            foreach (IList<BoundarySegment> loop in loops)
            {
                var poly = new List<XYZ>();
                foreach (BoundarySegment seg in loop)
                {
                    IList<XYZ> pts = seg.GetCurve().Tessellate();
                    for (int i = 0; i < pts.Count - 1; i++)
                    {
                        XYZ w = t.OfPoint(new XYZ(pts[i].X, pts[i].Y, localMinZ));
                        poly.Add(new XYZ(w.X, w.Y, 0));
                    }
                }
                if (poly.Count >= 3) vol.Loops.Add(poly);
            }
            if (vol.Loops.Count == 0)
                throw new InvalidOperationException($"{kind} '{se.Name}' has no valid boundary.");

            double zShift = t.OfPoint(XYZ.Zero).Z;   // 假設 link 冇傾斜
            vol.MinZ = localMinZ + zShift;
            vol.MaxZ = localMaxZ + zShift;

            var all = vol.Loops.SelectMany(l => l).ToList();
            vol.Min = new XYZ(all.Min(p => p.X), all.Min(p => p.Y), vol.MinZ);
            vol.Max = new XYZ(all.Max(p => p.X), all.Max(p => p.Y), vol.MaxZ);
            return vol;
        }

        public bool Contains(XYZ p, double insetXY = 0, double insetZ = 0)
        {
            if (p.Z < MinZ + insetZ || p.Z > MaxZ - insetZ) return false;
            if (p.X < Min.X || p.X > Max.X || p.Y < Min.Y || p.Y > Max.Y) return false;

            bool inside = false;
            foreach (var poly in Loops)
                for (int i = 0, j = poly.Count - 1; i < poly.Count; j = i++)
                {
                    XYZ a = poly[i], b = poly[j];
                    if ((a.Y > p.Y) != (b.Y > p.Y) &&
                        p.X < (b.X - a.X) * (p.Y - a.Y) / (b.Y - a.Y) + a.X)
                        inside = !inside;
                }
            if (!inside) return false;
            if (insetXY <= 0) return true;

            foreach (var poly in Loops)
                for (int i = 0, j = poly.Count - 1; i < poly.Count; j = i++)
                    if (Geom.DistanceToSegmentXY(p, poly[j], poly[i]) < insetXY) return false;
            return true;
        }
    }
}
