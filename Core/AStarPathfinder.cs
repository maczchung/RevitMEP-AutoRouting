using Autodesk.Revit.DB;
using System.Collections.Generic;

namespace MEPAutoRouting.Core
{
    public class AStarPathfinder
    {
        public List<XYZ> FindPath(XYZ start, XYZ end)
        {
            return MEPAutoRouting.Shared.SimplePath.GenerateLShape(start, end);
        }
    }
}
