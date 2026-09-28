using System;
using System.Collections.Generic;
using Autodesk.Revit.DB;

namespace MEPAutoRouting
{
    /// <summary>
    /// 新功能 1 + 2：牆間距 + Space 範圍。
    /// VoxelGrid 建 grid 嘅時候，逐個 cell 中心 call IsBlocked()。
    /// 所有長度都係 Revit internal units (feet)。
    /// </summary>
    public sealed class RoutingConstraints
    {
        public SpaceVolume Space { get; }
        public IReadOnlyList<WallObstacle> Walls { get; }
        public double WallClearance { get; }          // 牆面 → 管外皮 淨距
        public double PipeRadius { get; }             // 管 / conduit 外徑 / 2
        public bool ClearanceOnSpaceBoundary { get; } // Space 邊界都當牆咁留間距

        private readonly List<(XYZ P, double R)> _exempt = new();

        public RoutingConstraints(SpaceVolume space, IReadOnlyList<WallObstacle> walls,
                                  double wallClearance, double pipeRadius,
                                  bool clearanceOnSpaceBoundary = true)
        {
            Space = space;
            Walls = walls ?? Array.Empty<WallObstacle>();
            WallClearance = Math.Max(0, wallClearance);
            PipeRadius = Math.Max(0, pipeRadius);
            ClearanceOnSpaceBoundary = clearanceOnSpaceBoundary;
        }

        /// <summary>
        /// 起點 / 終點附近唔計牆間距（例如潔具貼牆嘅 connector），
        /// 否則 A* 一開始就被困住。radius 建議 = WallClearance + PipeRadius + 1.5 × cellSize。
        /// </summary>
        public void AddExemptPoint(XYZ p, double radius)
        {
            if (p != null && radius > 0) _exempt.Add((p, radius));
        }

        public double SuggestedExemptRadius(double cellSize) => WallClearance + PipeRadius + 1.5 * cellSize;

        /// <summary>牆中心線去管中心線嘅最少距離。</summary>
        public double RequiredDistance(WallObstacle w) => w.HalfWidth + WallClearance + PipeRadius;

        public bool IsBlocked(XYZ p) => GetBlockReason(p) != null;

        /// <summary>返回 null = 可以行；否則返回原因（debug 用）。</summary>
        public string GetBlockReason(XYZ p)
        {
            foreach (var (ep, r) in _exempt)
                if (ep.DistanceTo(p) <= r) return null;

            if (Space != null)
            {
                double inset = PipeRadius + (ClearanceOnSpaceBoundary ? WallClearance : 0);
                if (!Space.Contains(p, inset, PipeRadius)) return "Outside space";
            }

            double maxMargin = WallClearance + PipeRadius;
            foreach (WallObstacle w in Walls)
            {
                double need = RequiredDistance(w);
                if (!w.NearBox(p, maxMargin, PipeRadius)) continue;
                if (w.DistanceXY(p) < need) return $"Wall clearance ({w.Id})";
            }
            return null;
        }

        /// <summary>UI 輸入 mm → feet。</summary>
        public static double MmToFeet(double mm) => UnitUtils.ConvertToInternalUnits(mm, UnitTypeId.Millimeters);
    }
}
