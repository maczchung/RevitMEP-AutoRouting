using System;
using Autodesk.Revit.DB;

namespace MEPAutoRouting
{
    internal static class Geom
    {
        public static double DistanceToSegmentXY(XYZ p, XYZ a, XYZ b)
        {
            double dx = b.X - a.X, dy = b.Y - a.Y;
            double len2 = dx * dx + dy * dy;
            double t = len2 < 1e-12 ? 0 : ((p.X - a.X) * dx + (p.Y - a.Y) * dy) / len2;
            t = Math.Max(0, Math.Min(1, t));
            double cx = a.X + t * dx - p.X, cy = a.Y + t * dy - p.Y;
            return Math.Sqrt(cx * cx + cy * cy);
        }

        public static double DistanceXY(XYZ p, XYZ q)
        {
            double dx = p.X - q.X, dy = p.Y - q.Y;
            return Math.Sqrt(dx * dx + dy * dy);
        }

        public static double DistanceToSegment3D(XYZ p, XYZ a, XYZ b)
        {
            XYZ ab = b - a;
            double len2 = ab.DotProduct(ab);
            double t = len2 < 1e-12 ? 0 : Math.Max(0, Math.Min(1, (p - a).DotProduct(ab) / len2));
            return p.DistanceTo(a + ab * t);
        }

        public static double MmToFt(double mm) => UnitUtils.ConvertToInternalUnits(mm, UnitTypeId.Millimeters);
        public static double FtToMm(double ft) => UnitUtils.ConvertFromInternalUnits(ft, UnitTypeId.Millimeters);
    }
}
