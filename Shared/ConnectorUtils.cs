using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;

namespace MEPAutoRouting.Shared
{
    public static class ConnectorUtils
    {
        public static ConnectorManager GetConnectorManager(Element e)
        {
            if (e is MEPCurve curve) return curve.ConnectorManager;
            if (e is FamilyInstance fi) return fi.MEPModel?.ConnectorManager;
            return null;
        }

        public static IEnumerable<Connector> GetConnectors(Element e)
        {
            var cm = GetConnectorManager(e);
            if (cm == null) yield break;
            foreach (Connector c in cm.Connectors)
                yield return c;
        }

        /// <summary>Open physical end connectors, optionally filtered by domain.</summary>
        public static IEnumerable<Connector> GetOpenConnectors(Element e, Domain? domain = null)
        {
            return GetConnectors(e).Where(c =>
                c.ConnectorType == ConnectorType.End &&
                !c.IsConnected &&
                (domain == null || c.Domain == domain.Value));
        }

        /// <summary>Nearest open connector to the picked point. Falls back to the first open connector.</summary>
        public static Connector GetNearestOpenConnector(Element e, XYZ pickPoint, Domain? domain)
        {
            var open = GetOpenConnectors(e, domain).ToList();
            if (open.Count == 0) return null;
            if (pickPoint == null) return open[0];
            return open.OrderBy(c => c.Origin.DistanceTo(pickPoint)).First();
        }

        public static Connector GetNearestConnector(MEPCurve curve, XYZ point)
        {
            Connector best = null;
            double min = double.MaxValue;
            foreach (Connector c in curve.ConnectorManager.Connectors)
            {
                if (c.ConnectorType != ConnectorType.End) continue;
                double d = c.Origin.DistanceTo(point);
                if (d < min) { min = d; best = c; }
            }
            return best;
        }

        public static string DomainLabel(Domain d) => d switch
        {
            Domain.DomainPiping => "Piping",
            Domain.DomainHvac => "HVAC",
            Domain.DomainElectrical => "Electrical",
            Domain.DomainCableTrayConduit => "Cable Tray / Conduit",
            _ => d.ToString()
        };

        public static string ShapeLabel(Connector c) => c.Shape switch
        {
            ConnectorProfileType.Round => "Round",
            ConnectorProfileType.Rectangular => "Rect",
            ConnectorProfileType.Oval => "Oval",
            _ => "-"
        };
    }
}
