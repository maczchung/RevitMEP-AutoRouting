using Autodesk.Revit.DB;
using System.Collections.Generic;
using MEPAutoRouting.Core;

namespace MEPAutoRouting.Electrical
{
    public class ConduitRoutingEngine
    {
        private readonly Document _doc;
        private readonly BoundingBoxXYZ _routingBounds;
        public ConduitRoutingEngine(Document doc) { _doc = doc; _routingBounds = null; }
        public ConduitRoutingEngine(Document doc, BoundingBoxXYZ routingBounds) { _doc = doc; _routingBounds = routingBounds; }
        public List<XYZ> GeneratePath(XYZ start, XYZ end)
        {
            AStarPathfinder astar = new AStarPathfinder(_doc, _routingBounds);
            return astar.FindPath(start, end);
        }
    }
}
