using Autodesk.Revit.DB;
using System.Collections.Generic;
using MEPAutoRouting.Core;

namespace MEPAutoRouting.Electrical
{
    public class ConduitRoutingEngine
    {
        private readonly Document _doc;

        public ConduitRoutingEngine(Document doc)
        {
            _doc = doc;
        }

        public List<XYZ> GeneratePath(XYZ start, XYZ end)
        {
            AStarPathfinder astar = new AStarPathfinder();
            return astar.FindPath(start, end);
        }
    }
}
