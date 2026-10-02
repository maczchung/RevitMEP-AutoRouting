using System;
using System.Collections.Generic;
using Autodesk.Revit.DB;

namespace MEPAutoRouting
{
    /// <summary>牆間距 + 計算範圍。VoxelGrid 逐個 cell 中心 call IsBlocked()。長度全部係 feet。</summary>
    public sealed class RoutingConstraints
    {
        public IRoutingBoundary Boundary { get; }
        public IReadOnlyList<WallObstacle> Walls { get; }
        public double WallClearance { get; }
        public double PipeRadius { get; }
        public bool ClearanceOnBoundary { get; }

        private readonly List<(XYZ A, XYZ B, double R)> _exempt = new();

        public RoutingConstraints(IRoutingBoundary boundary, IReadOnlyList<WallObstacle> walls,
                                  double wallClearance, double pipeRadius, bool clearanceOnBoundary = true)
        {
            Boundary = boundary;
            Walls = walls ?? Array.Empty<WallObstacle>();
            WallClearance = Math.Max(0, wallClearance);
            PipeRadius = Math.Max(0, pipeRadius);
            ClearanceOnBoundary = clearanceOnBoundary;
        }

        public void AddExemptSegment(XYZ a, XYZ b, double radius)
        {
            if (a != null && b != null && radius > 0) _exempt.Add((a, b, radius));
        }

        public void AddExemptCorridor(ConnectorEndpoint ep, double cellSize)
            => AddExemptSegment(ep.Origin, ep.Lead, PipeRadius + cellSize);

        public double RequiredDistance(WallObstacle w) => w.HalfWidth + WallClearance + PipeRadius;

        public bool IsBlocked(XYZ p) => GetBlockReason(p) != null;

        public string GetBlockReason(XYZ p)
        {
            foreach (var (a, b, r) in _exempt)
                if (Geom.DistanceToSegment3D(p, a, b) <= r) return null;

            if (Boundary != null)
            {
                double inset = PipeRadius + (ClearanceOnBoundary ? WallClearance : 0);
                if (!Boundary.Contains(p, inset, PipeRadius)) return $"Outside {Boundary.Kind}";
            }

            double maxMargin = WallClearance + PipeRadius;
            foreach (WallObstacle w in Walls)
            {
                if (!w.NearBox(p, maxMargin, PipeRadius)) continue;
                if (w.DistanceXY(p) < RequiredDistance(w)) return $"Wall clearance ({w.Id})";
            }
            return null;
        }
    }
}
