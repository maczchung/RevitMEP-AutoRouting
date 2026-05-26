using Autodesk.Revit.DB;
using System.Collections.Generic;
using MEPAutoRouting.Core;

namespace MEPAutoRouting.Plumbing
{
    public class PipeRoutingEngine
    {
        private readonly Document _doc;
        private readonly BoundingBoxXYZ _routingBounds;

        public PipeRoutingEngine(Document doc)
        {
            _doc = doc;
            _routingBounds = null;
        }

        public PipeRoutingEngine(Document doc, BoundingBoxXYZ routingBounds)
        {
            _doc = doc;
            _routingBounds = routingBounds;
        }

        public List<XYZ> GeneratePath(XYZ start, XYZ end)
        {
            AStarPathfinder astar = new AStarPathfinder(_doc, _routingBounds);
            return astar.FindPath(start, end);
        }
    }
}
