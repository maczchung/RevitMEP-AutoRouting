using System;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Mechanical;
using Autodesk.Revit.DB.Plumbing;
using Autodesk.Revit.DB.Electrical;

namespace MEPAutoRouting.Core
{
    public static class ConnectorUtils
    {
        public static XYZ GetFallbackPoint(Element elem)
        {
            if (elem == null) return null;

            LocationPoint lp = elem.Location as LocationPoint;
            if (lp != null) return lp.Point;

            LocationCurve lc = elem.Location as LocationCurve;
            if (lc != null) return lc.Curve.Evaluate(0.5, true);

            BoundingBoxXYZ bb = elem.get_BoundingBox(null);
            if (bb != null)
                return new XYZ((bb.Min.X + bb.Max.X) / 2.0, (bb.Min.Y + bb.Max.Y) / 2.0, (bb.Min.Z + bb.Max.Z) / 2.0);

            return null;
        }

        public static Connector GetClosestConnector(Element elem, XYZ pickPoint, bool preferPiping, bool preferElectrical)
        {
            if (elem == null) return null;

            ConnectorSet connectors = GetConnectorSet(elem);
            if (connectors == null) return null;

            Connector best = null;
            double bestDist = double.MaxValue;

            foreach (Connector c in connectors)
            {
                if (c == null) continue;
                if (c.ConnectorType != ConnectorType.End && c.ConnectorType != ConnectorType.Curve) continue;

                if (preferPiping && c.Domain != Domain.DomainPiping && c.Domain != Domain.DomainUndefined) continue;
                if (preferElectrical && c.Domain != Domain.DomainCableTrayConduit && c.Domain != Domain.DomainElectrical && c.Domain != Domain.DomainUndefined) continue;

                XYZ refPoint = pickPoint ?? c.Origin;
                double d = c.Origin.DistanceTo(refPoint);
                if (d < bestDist)
                {
                    bestDist = d;
                    best = c;
                }
            }

            // fallback: ignore domain if nothing found
            if (best == null)
            {
                foreach (Connector c in connectors)
                {
                    if (c == null) continue;
                    if (c.ConnectorType != ConnectorType.End && c.ConnectorType != ConnectorType.Curve) continue;
                    XYZ refPoint = pickPoint ?? c.Origin;
                    double d = c.Origin.DistanceTo(refPoint);
                    if (d < bestDist)
                    {
                        bestDist = d;
                        best = c;
                    }
                }
            }

            return best;
        }

        public static ConnectorSet GetConnectorSet(Element elem)
        {
            if (elem == null) return null;

            FamilyInstance fi = elem as FamilyInstance;
            if (fi != null && fi.MEPModel != null && fi.MEPModel.ConnectorManager != null)
                return fi.MEPModel.ConnectorManager.Connectors;

            MEPCurve curve = elem as MEPCurve;
            if (curve != null && curve.ConnectorManager != null)
                return curve.ConnectorManager.Connectors;

            return null;
        }
    }
}
