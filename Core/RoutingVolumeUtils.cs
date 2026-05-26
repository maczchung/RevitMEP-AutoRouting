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

            try
            {
                bb = elem.get_BoundingBox(doc.ActiveView);
            }
            catch
            {
                bb = null;
            }

            if (bb == null)
            {
                try
                {
                    bb = elem.get_BoundingBox(null);
                }
                catch
                {
                    bb = null;
                }
            }

            return bb;
        }

        public static BoundingBoxXYZ GetElementBounds(Document doc, Element elem)
        {
            return GetHostElementBounds(doc, elem);
        }

        public static bool IsPointInsideBounds(XYZ point, BoundingBoxXYZ bounds)
        {
            if (point == null || bounds == null)
                return false;

            return point.X >= bounds.Min.X && point.X <= bounds.Max.X &&
                   point.Y >= bounds.Min.Y && point.Y <= bounds.Max.Y &&
                   point.Z >= bounds.Min.Z && point.Z <= bounds.Max.Z;
        }

        public static string FormatBounds(BoundingBoxXYZ bounds)
        {
            if (bounds == null)
                return "(null)";

            return "Min(" + bounds.Min.X.ToString("F2") + ", " + bounds.Min.Y.ToString("F2") + ", " + bounds.Min.Z.ToString("F2") + ")" +
                   " / Max(" + bounds.Max.X.ToString("F2") + ", " + bounds.Max.Y.ToString("F2") + ", " + bounds.Max.Z.ToString("F2") + ")";
        }
    }
}
