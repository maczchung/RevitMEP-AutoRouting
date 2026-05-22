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
            ElementId levelId = RoutingUtils.GetFirstLevelId(_doc);
            ElementId typeId = RoutingUtils.GetFirstConduitTypeId(_doc);

            if (levelId == ElementId.InvalidElementId || typeId == ElementId.InvalidElementId)
                throw new Exception("Cannot find Level or ConduitType.");

            using (Transaction t = new Transaction(_doc, "Create Conduit"))
            {
                t.Start();

                for (int i = 0; i < pts.Count - 1; i++)
                {
                    XYZ p1 = pts[i];
                    XYZ p2 = pts[i + 1];
                    if (p1.DistanceTo(p2) < 0.001) continue;
                    Conduit.Create(_doc, typeId, p1, p2, levelId);
                }

                t.Commit();
            }
        }
    }
}
