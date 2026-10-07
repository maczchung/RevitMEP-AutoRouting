using System.Collections.Generic;
using Autodesk.Revit.DB;
using MEPAutoRouting.Shared;

namespace MEPAutoRouting.Routing
{
    public enum Discipline { Pipe, Duct, Conduit, CableTray }

    public enum RouteStrategy
    {
        XThenY,         // horizontal X -> Y, then vertical at target
        YThenX,         // horizontal Y -> X, then vertical at target
        VerticalFirst,  // rise/drop at source, then horizontal
        AtElevation     // rise to fixed elevation above level, run, then drop
    }

    public enum LogLevel { Info, Success, Warn, Error }

    public static class DisciplineExt
    {
        public static Domain ToDomain(this Discipline d) => d switch
        {
            Discipline.Pipe => Domain.DomainPiping,
            Discipline.Duct => Domain.DomainHvac,
            _ => Domain.DomainCableTrayConduit
        };

        public static string Label(this Discipline d) => d switch
        {
            Discipline.Pipe => "Pipe",
            Discipline.Duct => "Duct",
            Discipline.Conduit => "Conduit",
            Discipline.CableTray => "Cable Tray",
            _ => d.ToString()
        };

        public static bool HasSystemType(this Discipline d) => d == Discipline.Pipe || d == Discipline.Duct;
    }

    /// <summary>Snapshot of a picked connector (safe to hold between API calls).</summary>
    public class ConnectorInfo
    {
        public ElementId OwnerId { get; set; }
        public int ConnectorId { get; set; }
        public XYZ Origin { get; set; }
        public XYZ Direction { get; set; }
        public Domain Domain { get; set; }
        public ConnectorProfileType Shape { get; set; }
        public double Radius { get; set; }
        public double Width { get; set; }
        public double Height { get; set; }
        public string Title { get; set; }

        // ---- UI display ----
        public string IdText => $"Id {OwnerId.Value} · C{ConnectorId}";
        public string DomainText => ConnectorUtils.DomainLabel(Domain);
        public string SizeText => Shape == ConnectorProfileType.Round
            ? $"Ø{UnitConv.Mm(Radius * 2)}"
            : Shape == ConnectorProfileType.Rectangular || Shape == ConnectorProfileType.Oval
                ? $"{UnitConv.Mm(Width)}×{UnitConv.Mm(Height)}"
                : "-";
        public string DetailText => $"{DomainText} · {SizeText}";
        public string OriginText => $"({UnitConv.Mm(Origin.X)}, {UnitConv.Mm(Origin.Y)}, {UnitConv.Mm(Origin.Z)})";

        public static ConnectorInfo From(Connector c, Element owner)
        {
            var info = new ConnectorInfo
            {
                OwnerId = owner.Id,
                ConnectorId = c.Id,
                Origin = c.Origin,
                Direction = c.CoordinateSystem.BasisZ.Normalize(),
                Domain = c.Domain,
                Shape = c.Shape,
                Title = BuildTitle(owner)
            };
            try
            {
                if (c.Shape == ConnectorProfileType.Round) info.Radius = c.Radius;
                else { info.Width = c.Width; info.Height = c.Height; }
            }
            catch { /* some connectors do not expose size */ }
            return info;
        }

        private static string BuildTitle(Element e)
        {
            if (e is FamilyInstance fi) return $"{fi.Symbol.FamilyName} : {fi.Symbol.Name}";
            return $"{e.Category?.Name} : {e.Name}";
        }

        public Connector Resolve(Document doc)
        {
            var owner = doc.GetElement(OwnerId);
            if (owner == null) return null;
            foreach (var c in ConnectorUtils.GetConnectors(owner))
                if (c.Id == ConnectorId) return c;
            return null;
        }
    }

    public class RouteOptions
    {
        public Discipline Discipline { get; set; } = Discipline.Pipe;
        public ElementId TypeId { get; set; } = ElementId.InvalidElementId;
        public ElementId SystemTypeId { get; set; } = ElementId.InvalidElementId;
        public ElementId LevelId { get; set; } = ElementId.InvalidElementId;
        public RouteStrategy Strategy { get; set; } = RouteStrategy.XThenY;
        public double LeadMm { get; set; } = 150;
        public double ElevationMm { get; set; } = 3000;
        public double MinSegmentMm { get; set; } = 100;
        public bool MatchSize { get; set; } = true;
        public bool AddFittings { get; set; } = true;
        public bool ConnectEnds { get; set; } = true;
        public bool SuppressWarnings { get; set; } = true;
        public PipeSizeInfo? Size { get; set; }
    }

    public class RouteResult
    {
        public List<XYZ> Path { get; set; } = new List<XYZ>();
        public List<RouteProblem> Problems { get; } = new List<RouteProblem>();
        public int CreatedSegments { get; set; }
        public int CreatedElbows { get; set; }
        public bool Committed { get; set; }
        public double AppliedFallFt { get; set; } = -1;   // v4.8.1 (Task 1) – slope actually applied; -1 = not sloped
        public List<ElementId> Created { get; } = new List<ElementId>();
        public int Segments { get; set; }
        public int Fittings { get; set; }
        public int FittingFailures { get; set; }
        // v4.8 (Task 3) – per-elbow failure detail (index, angle, sizes, elbow family)
        public List<RouteBuilder.FittingFailureInfo> FittingFailureDetails { get; } = new List<RouteBuilder.FittingFailureInfo>();
        public bool Success { get; set; }
    }

    public class TypeItem
    {
        public ElementId Id { get; set; }
        public string Name { get; set; }
        public Element Element { get; set; }
        public double Elevation { get; set; }   // for levels (ft)
        public override string ToString() => Name;
    }
}
