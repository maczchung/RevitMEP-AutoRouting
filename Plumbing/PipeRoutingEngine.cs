using Autodesk.Revit.DB;
using System.Collections.Generic;
using MEPAutoRouting.Core;

namespace MEPAutoRouting.Plumbing
{
    public class PipeRoutingEngine
    {
        private readonly Document _doc;

        public PipeRoutingEngine(Document doc)
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
