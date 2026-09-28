using System;
using Autodesk.Revit.DB;

namespace MEPAutoRouting
{
    /// <summary>Fix #6: BoundingBoxXYZ → world axis-aligned bounds（處理 bb.Transform 同 link transform）。</summary>
    public static class BoundingBoxUtils
    {
        public static (XYZ Min, XYZ Max) GetWorldBounds(BoundingBoxXYZ bb, Transform linkTransform = null)
        {
            if (bb == null) throw new ArgumentNullException(nameof(bb));
            Transform t = bb.Transform ?? Transform.Identity;
            if (linkTransform != null) t = linkTransform.Multiply(t);   // link × bb

            double minX = double.MaxValue, minY = double.MaxValue, minZ = double.MaxValue;
            double maxX = double.MinValue, maxY = double.MinValue, maxZ = double.MinValue;
            foreach (double x in new[] { bb.Min.X, bb.Max.X })
            foreach (double y in new[] { bb.Min.Y, bb.Max.Y })
            foreach (double z in new[] { bb.Min.Z, bb.Max.Z })
            {
                XYZ p = t.OfPoint(new XYZ(x, y, z));
                minX = Math.Min(minX, p.X); minY = Math.Min(minY, p.Y); minZ = Math.Min(minZ, p.Z);
                maxX = Math.Max(maxX, p.X); maxY = Math.Max(maxY, p.Y); maxZ = Math.Max(maxZ, p.Z);
            }
            return (new XYZ(minX, minY, minZ), new XYZ(maxX, maxY, maxZ));
        }

        public static (XYZ Min, XYZ Max)? GetWorldBounds(Element e, View view = null, Transform linkTransform = null)
        {
            BoundingBoxXYZ bb = e?.get_BoundingBox(view);
            return bb == null ? null : GetWorldBounds(bb, linkTransform);
        }
    }
}
