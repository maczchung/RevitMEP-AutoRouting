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
            // 將 Document 傳入，讓 A* 演算法可以擷取障礙物
            AStarPathfinder astar = new AStarPathfinder(_doc);
            return astar.FindPath(start, end);
        }
    }
}