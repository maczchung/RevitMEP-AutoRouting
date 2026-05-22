using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Electrical;

namespace MEPAutoRouting.Electrical
{
    public static class ConduitUtils
    {
        public static ElementId GetConduitType(Document doc)
        {
            return new FilteredElementCollector(doc).OfClass(typeof(ConduitType)).FirstElementId();
        }

        public static ElementId GetLevel(Document doc)
        {
            return new FilteredElementCollector(doc).OfClass(typeof(Level)).FirstElementId();
        }
    }
}
