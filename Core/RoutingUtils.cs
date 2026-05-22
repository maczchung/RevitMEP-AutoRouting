using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Plumbing;
using Autodesk.Revit.DB.Electrical;

namespace MEPAutoRouting.Core
{
    public static class RoutingUtils
    {
        public static ElementId GetFirstLevelId(Document doc)
        {
            return new FilteredElementCollector(doc).OfClass(typeof(Level)).FirstElementId();
        }

        public static ElementId GetFirstPipeTypeId(Document doc)
        {
            return new FilteredElementCollector(doc).OfClass(typeof(PipeType)).FirstElementId();
        }

        public static ElementId GetFirstPipingSystemTypeId(Document doc)
        {
            return new FilteredElementCollector(doc).OfClass(typeof(PipingSystemType)).FirstElementId();
        }

        public static ElementId GetFirstConduitTypeId(Document doc)
        {
            return new FilteredElementCollector(doc).OfClass(typeof(ConduitType)).FirstElementId();
        }
    }
}
