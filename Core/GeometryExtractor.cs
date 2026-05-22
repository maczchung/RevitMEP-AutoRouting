using Autodesk.Revit.DB;
using System.Collections.Generic;

namespace MEPAutoRouting.Core
{
    public class GeometryExtractor
    {
        private readonly Document _doc;

        public GeometryExtractor(Document doc)
        {
            _doc = doc;
        }

        public List<BoundingBoxXYZ> GetBoundingBoxes(BuiltInCategory category)
        {
            List<BoundingBoxXYZ> boxes = new List<BoundingBoxXYZ>();
            FilteredElementCollector collector = new FilteredElementCollector(_doc)
                .OfCategory(category)
                .WhereElementIsNotElementType();

            foreach (Element elem in collector)
            {
                BoundingBoxXYZ bb = elem.get_BoundingBox(null);
                if (bb != null) boxes.Add(bb);
            }

            return boxes;
        }
    }
}
