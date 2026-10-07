using System;
using Autodesk.Revit.DB;

namespace MEPAutoRouting
{
    /// <summary>
    /// Connector 起點 / 終點：Origin、向外方向（對齊軸）、Lead point。
    /// A* 由 Start.Lead 行去 End.Lead；完整路徑 = [Start.Origin, Start.Lead, ...A*..., End.Lead, End.Origin]。
    /// v4.4：新增 WithLeadLength() – RouteService 用嚟吸收細小偏移。
    /// </summary>
    public sealed class ConnectorEndpoint
    {
        public ElementId OwnerId { get; private set; }
        public Connector Connector { get; private set; }
        public XYZ Origin { get; private set; }
        public XYZ Direction { get; private set; }
        public (int dx, int dy, int dz) AxisDir { get; private set; }
        public bool IsAxisAligned { get; private set; }
        public XYZ Lead { get; private set; }
        public double LeadLength { get; private set; }
        public double Radius { get; private set; }

        public static ConnectorEndpoint From(Connector c, double leadLengthFt)
        {
            if (c == null) throw new ArgumentNullException(nameof(c));
            XYZ z = c.CoordinateSystem.BasisZ.Normalize();
            (int dx, int dy, int dz) axis = SnapAxis(z, out bool aligned);
            double radius = c.Shape == ConnectorProfileType.Round
                ? c.Radius
                : Math.Max(c.Width, c.Height) / 2.0;
            var dir = new XYZ(axis.dx, axis.dy, axis.dz);
            double lead = Math.Max(leadLengthFt, radius * 2);
            return new ConnectorEndpoint
            {
                OwnerId = c.Owner.Id,
                Connector = c,
                Origin = c.Origin,
                Direction = dir,
                AxisDir = axis,
                IsAxisAligned = aligned,
                LeadLength = lead,
                Lead = c.Origin + dir * lead,
                Radius = radius
            };
        }

        /// <summary>Same connector, different lead length (feet).</summary>
        public ConnectorEndpoint WithLeadLength(double leadFt) => new ConnectorEndpoint
        {
            OwnerId = OwnerId,
            Connector = Connector,
            Origin = Origin,
            Direction = Direction,
            AxisDir = AxisDir,
            IsAxisAligned = IsAxisAligned,
            LeadLength = leadFt,
            Lead = Origin + Direction * leadFt,
            Radius = Radius
        };

        private static (int dx, int dy, int dz) SnapAxis(XYZ d, out bool aligned)
        {
            double ax = Math.Abs(d.X), ay = Math.Abs(d.Y), az = Math.Abs(d.Z);
            aligned = Math.Max(ax, Math.Max(ay, az)) > 0.999;
            if (ax >= ay && ax >= az) return (Math.Sign(d.X), 0, 0);
            if (ay >= az) return (0, Math.Sign(d.Y), 0);
            return (0, 0, Math.Sign(d.Z));
        }
    }

    /// <summary>A* 方向規則：防止起點 / 終點 U-turn。</summary>
    public static class AStarDirectionRules
    {
        public static bool IsAllowedFirstMove((int dx, int dy, int dz) move, ConnectorEndpoint start)
            => move != PathUtils.Negate(start.AxisDir);

        public static bool IsAllowedGoalEntry((int dx, int dy, int dz) move, ConnectorEndpoint end)
            => move != end.AxisDir;
    }
}
