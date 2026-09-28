using Autodesk.Revit.DB;

namespace MEPAutoRouting.Core
{
    public static class RoutingVolumeUtils
    {
        public static bool IsSupportedRoutingVolume(Element elem)
        {
            if (elem == null || elem.Category == null)
                return false;

            BuiltInCategory category = (BuiltInCategory)elem.Category.Id.Value;
            return category == BuiltInCategory.OST_Rooms ||
                   category == BuiltInCategory.OST_MEPSpaces ||
                   category == BuiltInCategory.OST_Mass ||
                   category == BuiltInCategory.OST_GenericModel;
        }

        public static BoundingBoxXYZ GetHostElementBounds(Document doc, Element elem)
        {
            if (doc == null || elem == null)
                return null;

            BoundingBoxXYZ bb = null;
            try { bb = elem.get_BoundingBox(doc.ActiveView); } catch { bb = null; }
            if (bb == null)
            {
                try { bb = elem.get_BoundingBox(null); } catch { bb = null; }
            }
            var world = BoundingBoxUtils.GetWorldBounds(bb);
            BoundingBoxXYZ result = new BoundingBoxXYZ();
            result.Min = world.Min;
            result.Max = world.Max;
            return result;
        }

        public static BoundingBoxXYZ GetElementBounds(Document doc, Element elem)
        {
            return GetHostElementBounds(doc, elem);
        }

        public static bool IsPointInsideBounds(XYZ point, BoundingBoxXYZ bounds)
        {
            if (point == null || bounds == null)
                return false;
                 var world = BoundingBoxUtils.GetWorldBounds(bounds);
                 return point.X >= world.Min.X && point.X <= world.Max.X &&
                     point.Y >= world.Min.Y && point.Y <= world.Max.Y &&
                     point.Z >= world.Min.Z && point.Z <= world.Max.Z;
        }

        public static string FormatBounds(BoundingBoxXYZ bounds)
        {
            if (bounds == null) return "(null)";
            return "Min(" + bounds.Min.X.ToString("F2") + ", " + bounds.Min.Y.ToString("F2") + ", " + bounds.Min.Z.ToString("F2") + ")" +
                   " / Max(" + bounds.Max.X.ToString("F2") + ", " + bounds.Max.Y.ToString("F2") + ", " + bounds.Max.Z.ToString("F2") + ")";
        }
    }
}
