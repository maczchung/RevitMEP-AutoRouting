using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Electrical;
using System;
using System.Collections.Generic;
using MEPAutoRouting.Core;

namespace MEPAutoRouting.Electrical
{
    public class ConduitCreator
    {
        private readonly Document _doc;

        public ConduitCreator(Document doc)
        {
            _doc = doc;
        }

        public void Create(List<XYZ> pts)
        {
            Create(pts, null);
        }

        public int Create(List<XYZ> pts, IList<string> failures)
        {
            if (!PathUtils.IsValid(pts)) return 0;
            List<XYZ> path = PathUtils.MergeCollinear(pts);
            if (!PathUtils.IsValid(path)) return 0;
            ElementId levelId = RoutingUtils.GetFirstLevelId(_doc);
            ElementId typeId = RoutingUtils.GetFirstConduitTypeId(_doc);

            if (levelId == ElementId.InvalidElementId || typeId == ElementId.InvalidElementId)
                throw new Exception("Cannot find Level or ConduitType.");

            using (Transaction t = new Transaction(_doc, "Create Conduit"))
            {
                t.Start();

                List<MEPCurve> segments = new List<MEPCurve>();
                for (int i = 0; i < path.Count - 1; i++)
                {
                    XYZ p1 = path[i];
                    XYZ p2 = path[i + 1];
                    if (p1.DistanceTo(p2) < 0.001) continue;
                    Conduit conduit = Conduit.Create(_doc, typeId, p1, p2, levelId);
                    if (conduit != null) segments.Add(conduit);
                }

                MepFittingUtils.CreateElbows(_doc, segments, path, failures);
                t.Commit();
                return segments.Count;
            }
        }
    }
}
