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

        /// <summary>v4.8.4 – all physical End connectors (open or connected), optionally filtered by domain.</summary>
        public static IEnumerable<Connector> GetEndConnectors(Element e, Domain? domain = null)
        {
            return GetConnectors(e).Where(c =>
                c.ConnectorType == ConnectorType.End &&
                (domain == null || c.Domain == domain.Value));
        }

        /// <summary>Open physical end connectors, optionally filtered by domain.</summary>
        public static IEnumerable<Connector> GetOpenConnectors(Element e, Domain? domain = null)
            => GetEndConnectors(e, domain).Where(c => !c.IsConnected);

        /// <summary>
        /// v4.8.4 – nearest End connector to the pick point, open OR connected.
        /// Never falls back to another connector: the caller decides what to do when it is connected.
        /// Without a pick point, the first open connector is returned (or the first End connector).
        /// </summary>
        public static Connector GetNearestEndConnector(Element e, XYZ pickPoint, Domain? domain)
        {
            var all = GetEndConnectors(e, domain).ToList();
            if (all.Count == 0) return null;
            if (pickPoint == null) return all.FirstOrDefault(c => !c.IsConnected) ?? all[0];
            return all.OrderBy(c => c.Origin.DistanceTo(pickPoint)).First();
        }

        /// <summary>Nearest open connector to the picked point. Falls back to the first open connector.</summary>
        public static Connector GetNearestOpenConnector(Element e, XYZ pickPoint, Domain? domain)
        {
            var open = GetOpenConnectors(e, domain).ToList();
            if (open.Count == 0) return null;
            if (pickPoint == null) return open[0];
            return open.OrderBy(c => c.Origin.DistanceTo(pickPoint)).First();
        }

        /// <summary>v4.8.4 – Id of the element a connector is physically connected to (null if open).</summary>
        public static ElementId GetConnectedOwnerId(Connector c)
        {
            if (c == null || !c.IsConnected) return null;
            try
            {
                foreach (Connector r in c.AllRefs)
                {
                    if (r?.Owner == null || r.Owner is MEPSystem) continue;
                    if (r.Owner.Id == c.Owner.Id) continue;
                    return r.Owner.Id;
                }
            }
            catch { /* AllRefs can throw on some family connectors – treat as unknown owner */ }
            return ElementId.InvalidElementId;
        }

        /// <summary>v4.8.4 – "Ø80 mm, -Y, open" / "Ø80 mm, -Y, connected to 123456".</summary>
        public static string Describe(Connector c)
        {
            string size = c.Shape == ConnectorProfileType.Round
                ? $"Ø{c.Radius * 2 * 304.8:0.#} mm"
                : ShapeLabel(c);
            string dir = DirectionLabel(c.CoordinateSystem?.BasisZ);
            if (!c.IsConnected) return $"{size}, {dir}, open";
            ElementId other = GetConnectedOwnerId(c);
            return other == null || other == ElementId.InvalidElementId
                ? $"{size}, {dir}, connected"
                : $"{size}, {dir}, connected to {other}";
        }

        public static string DirectionLabel(XYZ d)
        {
            if (d == null) return "?";
            double ax = System.Math.Abs(d.X), ay = System.Math.Abs(d.Y), az = System.Math.Abs(d.Z);
            if (az >= ax && az >= ay) return d.Z >= 0 ? "Up" : "Down";
            if (ax >= ay) return d.X >= 0 ? "+X" : "-X";
            return d.Y >= 0 ? "+Y" : "-Y";
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
