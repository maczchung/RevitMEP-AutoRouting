using System;
using System.Collections.Generic;
using Autodesk.Revit.DB;

namespace MEPAutoRouting
{
    /// <summary>
    /// 用 element 實際 Solid 判斷點喺唔喺入面（唔係 bounding box）。
    /// 用途：① Mass 計算範圍 ② Source / Target 設備做障礙物（修 U-turn 穿機身）。
    /// </summary>
    public sealed class SolidVolume
    {
        private const double Q = 1e-4;
        private readonly List<Solid> _solids;
        private readonly Dictionary<(long, long), List<(double A, double B)>> _cache = new();

        public XYZ Min { get; }
        public XYZ Max { get; }
        public int SolidCount => _solids.Count;

        private SolidVolume(List<Solid> solids, XYZ min, XYZ max)
        {
            _solids = solids; Min = min; Max = max;
        }

        public static SolidVolume FromElement(Element e, Transform linkTransform = null)
        {
            if (e == null) return null;
            var opt = new Options { ComputeReferences = false, DetailLevel = ViewDetailLevel.Fine };
            GeometryElement ge = e.get_Geometry(opt);
            if (ge == null) return null;

            var solids = new List<Solid>();
            Collect(ge, solids);
            if (linkTransform != null)
                for (int i = 0; i < solids.Count; i++)
                    solids[i] = SolidUtils.CreateTransformed(solids[i], linkTransform);
            if (solids.Count == 0) return null;

            double minX = double.MaxValue, minY = double.MaxValue, minZ = double.MaxValue;
            double maxX = double.MinValue, maxY = double.MinValue, maxZ = double.MinValue;
            foreach (Solid s in solids)
            {
                var (mn, mx) = BoundingBoxUtils.GetWorldBounds(s.GetBoundingBox());
                minX = Math.Min(minX, mn.X); minY = Math.Min(minY, mn.Y); minZ = Math.Min(minZ, mn.Z);
                maxX = Math.Max(maxX, mx.X); maxY = Math.Max(maxY, mx.Y); maxZ = Math.Max(maxZ, mx.Z);
            }
            return new SolidVolume(solids, new XYZ(minX, minY, minZ), new XYZ(maxX, maxY, maxZ));
        }

        private static void Collect(GeometryElement ge, List<Solid> list)
        {
            foreach (GeometryObject go in ge)
            {
                switch (go)
                {
                    case Solid s when s.Volume > 1e-9:
                        list.Add(s); break;
                    case GeometryInstance gi:
                        GeometryElement inst = gi.GetInstanceGeometry();
                        if (inst != null) Collect(inst, list);
                        break;
                }
            }
        }

        private bool InBox(XYZ p, double m = 0) =>
            p.X >= Min.X - m && p.X <= Max.X + m &&
            p.Y >= Min.Y - m && p.Y <= Max.Y + m &&
            p.Z >= Min.Z - m && p.Z <= Max.Z + m;

        public bool IsInside(XYZ p)
        {
            if (!InBox(p)) return false;
            foreach (var (a, b) in IntervalsX(p.Y, p.Z))
                if (p.X >= a && p.X <= b) return true;
            return false;
        }

        public bool Contains(XYZ p, double insetXY = 0, double insetZ = 0)
        {
            if (!IsInside(p)) return false;
            if (insetXY > 0)
            {
                if (!IsInside(p + new XYZ(insetXY, 0, 0)) || !IsInside(p - new XYZ(insetXY, 0, 0))) return false;
                if (!IsInside(p + new XYZ(0, insetXY, 0)) || !IsInside(p - new XYZ(0, insetXY, 0))) return false;
            }
            if (insetZ > 0)
            {
                if (!IsInside(p + new XYZ(0, 0, insetZ)) || !IsInside(p - new XYZ(0, 0, insetZ))) return false;
            }
            return true;
        }

        public bool Hits(XYZ p, double inflate)
        {
            if (!InBox(p, inflate)) return false;
            if (IsInside(p)) return true;
            if (inflate <= 0) return false;
            return IsInside(p + new XYZ(inflate, 0, 0)) || IsInside(p - new XYZ(inflate, 0, 0))
                || IsInside(p + new XYZ(0, inflate, 0)) || IsInside(p - new XYZ(0, inflate, 0))
                || IsInside(p + new XYZ(0, 0, inflate)) || IsInside(p - new XYZ(0, 0, inflate));
        }

        private List<(double A, double B)> IntervalsX(double y, double z)
        {
            var key = ((long)Math.Round(y / Q), (long)Math.Round(z / Q));
            if (_cache.TryGetValue(key, out var hit)) return hit;

            var result = new List<(double, double)>();
            if (y >= Min.Y && y <= Max.Y && z >= Min.Z && z <= Max.Z)
            {
                Line ray = Line.CreateBound(new XYZ(Min.X - 1, y, z), new XYZ(Max.X + 1, y, z));
                var opt = new SolidCurveIntersectionOptions
                {
                    ResultType = SolidCurveIntersectionMode.CurveSegmentsInside
                };
                foreach (Solid s in _solids)
                {
                    try
                    {
                        SolidCurveIntersection sci = s.IntersectWithCurve(ray, opt);
                        for (int i = 0; i < sci.SegmentCount; i++)
                        {
                            Curve c = sci.GetCurveSegment(i);
                            double a = c.GetEndPoint(0).X, b = c.GetEndPoint(1).X;
                            result.Add((Math.Min(a, b), Math.Max(a, b)));
                        }
                    }
                    catch (Autodesk.Revit.Exceptions.InvalidOperationException) { }
                    catch (Autodesk.Revit.Exceptions.ArgumentException) { }
                }
            }
            _cache[key] = result;
            return result;
        }
    }
}
