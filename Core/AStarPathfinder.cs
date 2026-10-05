using System;
using System.Collections.Generic;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using MEPAutoRouting;

namespace MEPAutoRouting.Core
{
    /// <summary>
    /// BetterRoute Horizontal A* pathfinder.
    /// v4.4 – Aligned grid:
    ///   • Grid step is chosen PER AXIS so that BOTH start.Lead and end.Lead fall exactly on grid nodes.
    ///   • Every path point is therefore a node centre → the path is 100 % axis-aligned
    ///     (no more skewed first / last segment, no more tiny diagonal jog near the target).
    ///   • RoundPath / RemoveVeryShortSegments removed (they created diagonals).
    /// </summary>
    public class AStarPathfinder
    {
        private readonly Document _doc;
        private readonly BoundingBoxXYZ _routingBounds;
        private readonly RoutingConstraints _constraints;

        private ConnectorEndpoint _startEndpoint;
        private ConnectorEndpoint _endEndpoint;

        private const double GridSize = 0.5;                 // nominal step, feet (≈152 mm)
        private const double SnapTolFt = 1.0 / 304.8;        // 1 mm – smaller offsets are ignored
        private const int MarginCells = 12;
        private const int MaxIterations = 400000;
        private const double TurnPenalty = 22.0;
        private const double ZMovePenalty = 600.0;
        private const double ReversePenalty = 40.0;
        private const double ContinueStraightBonus = 0.20;
        private const double StartDirectionBonus = 0.15;
        private const double ObstacleToleranceFactor = 0.10;
        private const double EscapeZoneCells = 2.5;
        private const double SegmentSampleFactor = 0.25;
        private const double MinOverlapRatioToKeep = 0.18;
        private const double LongBoundaryRatio = 0.55;
        private const double BoundaryNearToleranceFt = 0.75;

        // aligned grid
        private XYZ _origin;
        private double _gx, _gy, _gz;
        private (int X, int Y, int Z) _endIdx;

        public string GridInfo =>
            _origin == null ? "-" :
            $"{_gx * 304.8:0} x {_gy * 304.8:0} x {_gz * 304.8:0} mm";

        public AStarPathfinder(Document doc) : this(doc, null, null) { }
        public AStarPathfinder(Document doc, BoundingBoxXYZ routingBounds) : this(doc, routingBounds, null) { }
        public AStarPathfinder(Document doc, BoundingBoxXYZ routingBounds, RoutingConstraints constraints)
        {
            _doc = doc;
            _routingBounds = routingBounds;
            _constraints = constraints;
        }

        public List<XYZ> FindPath(XYZ start, XYZ end) => FindPath(start, end, XYZ.BasisX);

        public List<XYZ> FindPath(XYZ start, XYZ end, XYZ startDir,
                                  ConnectorEndpoint startEndpoint, ConnectorEndpoint endEndpoint)
        {
            _startEndpoint = startEndpoint;
            _endEndpoint = endEndpoint;
            try { return FindPath(start, end, startDir); }
            finally { _startEndpoint = null; _endEndpoint = null; }
        }

        public List<XYZ> FindPath(XYZ start, XYZ end, XYZ startDir)
        {
            if (start == null || end == null) return new List<XYZ>();
            if (start.DistanceTo(end) < 0.001) return new List<XYZ>();

            try
            {
                SearchBox box = BuildSearchBox(start, end);

                if (_routingBounds != null &&
                    (!IsInsideRoutingVolume(start) || !IsInsideRoutingVolume(end)))
                {
                    TaskDialog.Show("A* BetterRoute-Horizontal",
                        "Start or End is outside the selected routing volume. No pipe was created.");
                    return new List<XYZ>();
                }

                SetupAlignedGrid(start, end, box);

                XYZ normalizedStartDir = NormalizeToMajorAxis(startDir);
                ObstacleResult obstacleResult = GetFilteredObstacles(box);
                List<BoundingBoxXYZ> obstacles = obstacleResult.UsedObstacles;

                var grid = new Dictionary<string, AStarNode>();
                AStarNode startNode = GetOrCreateNode(grid, start, obstacles, start, end);
                AStarNode endNode = GetOrCreateNode(grid, end, obstacles, start, end);
                _endIdx = (endNode.X, endNode.Y, endNode.Z);

                startNode.IsObstacle = false;
                endNode.IsObstacle = false;
                startNode.GCost = 0.0;
                startNode.HCost = GetHeuristic(startNode.Center, endNode.Center);

                var openHeap = new MinHeap<AStarNode>();
                var closedSet = new HashSet<string>();
                openHeap.Push(startNode, startNode.FCost);

                int iterations = 0;
                while (openHeap.Count > 0)
                {
                    if (++iterations > MaxIterations)
                    {
                        ShowFailureDebug("A* reached max iterations. No fallback pipe was created.", obstacleResult, iterations);
                        return new List<XYZ>();
                    }

                    AStarNode current = openHeap.Pop();
                    if (closedSet.Contains(current.Id)) continue;

                    if (current.X == endNode.X && current.Y == endNode.Y && current.Z == endNode.Z)
                    {
                        List<XYZ> result = RetracePath(startNode, current, start, end);
                        result = SimplifyCollinearPoints(result);

                        if (PathHitsObstacle(result, obstacles, start, end))
                        {
                            ShowFailureDebug("A* found a path but final cleaned path intersects an obstacle. No pipe was created.", obstacleResult, iterations);
                            return new List<XYZ>();
                        }
                        return result;
                    }

                    closedSet.Add(current.Id);

                    foreach (AStarNode neighbor in GetNeighbors(current, grid, box, obstacles, start, end))
                    {
                        if (neighbor.IsObstacle || closedSet.Contains(neighbor.Id)) continue;
                        if (IsSegmentBlocked(current.Center, neighbor.Center, obstacles, start, end)) continue;

                        int dx2 = neighbor.X - current.X;
                        int dy2 = neighbor.Y - current.Y;
                        int dz2 = neighbor.Z - current.Z;

                        double movementCost = dx2 != 0 ? _gx : dy2 != 0 ? _gy : _gz;
                        if (dz2 != 0) movementCost += ZMovePenalty;

                        if (current.Parent != null)
                        {
                            movementCost += PathUtils.TurnPenalty(current.Direction, (dx2, dy2, dz2), TurnPenalty);
                            if (current.Direction == (-dx2, -dy2, -dz2)) movementCost += ReversePenalty;
                            else if (current.Direction == (dx2, dy2, dz2)) movementCost -= ContinueStraightBonus;
                        }
                        else if (IsSameMajorAxis(new XYZ(dx2, dy2, dz2), normalizedStartDir))
                        {
                            movementCost -= StartDirectionBonus;
                        }

                        double tentativeG = current.GCost + movementCost;
                        if (tentativeG < neighbor.GCost)
                        {
                            neighbor.Parent = current;
                            neighbor.Direction = (dx2, dy2, dz2);
                            neighbor.GCost = tentativeG;
                            neighbor.HCost = GetHeuristic(neighbor.Center, endNode.Center);
                            openHeap.Push(neighbor, neighbor.FCost);
                        }
                    }
                }

                ShowFailureDebug("A* could not find a valid obstacle-free path. No fallback pipe was created.", obstacleResult, iterations);
                return new List<XYZ>();
            }
            catch (Exception ex)
            {
                TaskDialog.Show("A* BetterRoute-Horizontal Error", ex.ToString());
                return new List<XYZ>();
            }
        }

        // ================================================================ aligned grid

        /// <summary>Step for one axis so that |span| is an exact multiple of the step.</summary>
        private static double AlignedStep(double span)
        {
            double a = Math.Abs(span);
            if (a < SnapTolFt) return GridSize;                     // same coordinate → nominal step
            int n = Math.Max(1, (int)Math.Round(a / GridSize));
            return a / n;
        }

        /// <summary>Origin ≤ box.Min, and start lies exactly on a node.</summary>
        private static double AlignedOrigin(double s, double boxMin, double step)
        {
            double k = Math.Max(0, Math.Ceiling((s - boxMin) / step));
            return s - k * step;
        }

        private void SetupAlignedGrid(XYZ start, XYZ end, SearchBox box)
        {
            _gx = AlignedStep(end.X - start.X);
            _gy = AlignedStep(end.Y - start.Y);
            _gz = AlignedStep(end.Z - start.Z);
            _origin = new XYZ(
                AlignedOrigin(start.X, box.Min.X, _gx),
                AlignedOrigin(start.Y, box.Min.Y, _gy),
                AlignedOrigin(start.Z, box.Min.Z, _gz));
        }

        private XYZ NodeCenter(int x, int y, int z) =>
            new XYZ(_origin.X + x * _gx, _origin.Y + y * _gy, _origin.Z + z * _gz);

        private (int X, int Y, int Z) IndexOf(XYZ p) =>
            ((int)Math.Round((p.X - _origin.X) / _gx),
             (int)Math.Round((p.Y - _origin.Y) / _gy),
             (int)Math.Round((p.Z - _origin.Z) / _gz));

        // ================================================================ search box / obstacles

        private void ShowFailureDebug(string title, ObstacleResult obstacleResult, int iterations)
        {
            TaskDialog.Show("A* BetterRoute-Horizontal",
                title +
                Environment.NewLine + "Iterations: " + iterations +
                Environment.NewLine + "Grid step: " + GridInfo +
                Environment.NewLine + "RoutingBounds: " + (_routingBounds != null) +
                Environment.NewLine + "All obstacle boxes: " + obstacleResult.AllObstaclesCount +
                Environment.NewLine + "Used obstacle boxes: " + obstacleResult.UsedObstacles.Count +
                Environment.NewLine + "Ignored boundary boxes: " + obstacleResult.IgnoredBoundaryCount +
                Environment.NewLine + "Ignored low-overlap boxes: " + obstacleResult.IgnoredLowOverlapCount +
                Environment.NewLine + "Tip: if no path is found, lower ZMovePenalty or check routing volume height.");
        }

        private SearchBox BuildSearchBox(XYZ start, XYZ end)
        {
            if (_routingBounds != null)
            {
                XYZ expand = new XYZ(GridSize * 2.0, GridSize * 2.0, GridSize * 2.0);
                return new SearchBox(_routingBounds.Min - expand, _routingBounds.Max + expand);
            }
            double margin = GridSize * MarginCells;
            return new SearchBox(
                new XYZ(Math.Min(start.X, end.X) - margin, Math.Min(start.Y, end.Y) - margin, Math.Min(start.Z, end.Z) - margin),
                new XYZ(Math.Max(start.X, end.X) + margin, Math.Max(start.Y, end.Y) + margin, Math.Max(start.Z, end.Z) + margin));
        }

        private ObstacleResult GetFilteredObstacles(SearchBox box)
        {
            var extractor = new GeometryExtractor(_doc);
            var all = new List<BoundingBoxXYZ>();
            AddBoxesSafe(all, extractor, BuiltInCategory.OST_Columns);
            AddBoxesSafe(all, extractor, BuiltInCategory.OST_StructuralColumns);
            AddBoxesSafe(all, extractor, BuiltInCategory.OST_StructuralFraming);

            var result = new ObstacleResult { AllObstaclesCount = all.Count };
            XYZ filterMin = _routingBounds != null ? _routingBounds.Min : box.Min;
            XYZ filterMax = _routingBounds != null ? _routingBounds.Max : box.Max;

            foreach (BoundingBoxXYZ bb in all)
            {
                if (bb == null || !BoxesOverlap(bb, box.Min, box.Max)) continue;
                if (_routingBounds != null)
                {
                    if (GetXYOverlapRatioAgainstObstacle(bb, filterMin, filterMax) < MinOverlapRatioToKeep)
                    { result.IgnoredLowOverlapCount++; continue; }
                    if (IsLikelyBoundaryWall(bb, filterMin, filterMax))
                    { result.IgnoredBoundaryCount++; continue; }
                }
                result.UsedObstacles.Add(bb);
            }
            return result;
        }

        private double GetXYOverlapRatioAgainstObstacle(BoundingBoxXYZ bb, XYZ boxMin, XYZ boxMax)
        {
            double ix = Math.Max(0.0, Math.Min(bb.Max.X, boxMax.X) - Math.Max(bb.Min.X, boxMin.X));
            double iy = Math.Max(0.0, Math.Min(bb.Max.Y, boxMax.Y) - Math.Max(bb.Min.Y, boxMin.Y));
            double obstacleArea = Math.Max(0.000001, Math.Abs(bb.Max.X - bb.Min.X) * Math.Abs(bb.Max.Y - bb.Min.Y));
            return ix * iy / obstacleArea;
        }

        private bool IsLikelyBoundaryWall(BoundingBoxXYZ bb, XYZ boxMin, XYZ boxMax)
        {
            double boxSpanX = Math.Abs(boxMax.X - boxMin.X), boxSpanY = Math.Abs(boxMax.Y - boxMin.Y);
            double bbSpanX = Math.Abs(bb.Max.X - bb.Min.X), bbSpanY = Math.Abs(bb.Max.Y - bb.Min.Y);
            bool longInX = boxSpanX > 0.001 && bbSpanX / boxSpanX > LongBoundaryRatio;
            bool longInY = boxSpanY > 0.001 && bbSpanY / boxSpanY > LongBoundaryRatio;
            double t = BoundaryNearToleranceFt;
            bool nearYBoundary = Math.Abs(bb.Min.Y - boxMin.Y) < t || Math.Abs(bb.Max.Y - boxMax.Y) < t || bb.Max.Y <= boxMin.Y + t || bb.Min.Y >= boxMax.Y - t;
            bool nearXBoundary = Math.Abs(bb.Min.X - boxMin.X) < t || Math.Abs(bb.Max.X - boxMax.X) < t || bb.Max.X <= boxMin.X + t || bb.Min.X >= boxMax.X - t;
            return (longInX && nearYBoundary) || (longInY && nearXBoundary);
        }

        private void AddBoxesSafe(List<BoundingBoxXYZ> boxes, GeometryExtractor extractor, BuiltInCategory category)
        {
            try
            {
                List<BoundingBoxXYZ> result = extractor.GetBoundingBoxes(category);
                if (result != null) boxes.AddRange(result);
            }
            catch { }
        }

        private bool BoxesOverlap(BoundingBoxXYZ bb, XYZ min, XYZ max) =>
            !(bb.Max.X < min.X || bb.Min.X > max.X ||
              bb.Max.Y < min.Y || bb.Min.Y > max.Y ||
              bb.Max.Z < min.Z || bb.Min.Z > max.Z);

        // ================================================================ nodes

        private AStarNode GetOrCreateNode(Dictionary<string, AStarNode> grid, XYZ point,
                                          List<BoundingBoxXYZ> obstacles, XYZ start, XYZ end)
        {
            var (x, y, z) = IndexOf(point);
            string id = GetNodeId(x, y, z);
            if (grid.TryGetValue(id, out AStarNode existing)) return existing;

            XYZ center = NodeCenter(x, y, z);
            var node = new AStarNode(x, y, z, center, id);
            bool outside = _routingBounds != null && !IsInsideRoutingVolume(center);
            node.IsObstacle = outside || CheckIfObstacle(center, obstacles, start, end);
            grid[id] = node;
            return node;
        }

        private List<AStarNode> GetNeighbors(AStarNode node, Dictionary<string, AStarNode> grid, SearchBox box,
                                             List<BoundingBoxXYZ> obstacles, XYZ start, XYZ end)
        {
            var neighbors = new List<AStarNode>();
            foreach ((int dx, int dy, int dz) direction in PathUtils.SixNeighbours)
            {
                int nx = node.X + direction.dx, ny = node.Y + direction.dy, nz = node.Z + direction.dz;

                if (node.Parent == null && _startEndpoint != null &&
                    !AStarDirectionRules.IsAllowedFirstMove(direction, _startEndpoint))
                    continue;

                XYZ center = NodeCenter(nx, ny, nz);
                if (!IsInsideBox(center, box.Min, box.Max)) continue;
                if (_routingBounds != null && !IsInsideRoutingVolume(center)) continue;

                if (_endEndpoint != null && nx == _endIdx.X && ny == _endIdx.Y && nz == _endIdx.Z &&
                    !AStarDirectionRules.IsAllowedGoalEntry(direction, _endEndpoint))
                    continue;

                string id = GetNodeId(nx, ny, nz);
                if (!grid.TryGetValue(id, out AStarNode existing))
                {
                    existing = new AStarNode(nx, ny, nz, center, id)
                    {
                        IsObstacle = CheckIfObstacle(center, obstacles, start, end)
                    };
                    grid[id] = existing;
                }
                neighbors.Add(existing);
            }
            return neighbors;
        }

        private bool CheckIfObstacle(XYZ point, List<BoundingBoxXYZ> obstacles, XYZ start, XYZ end)
        {
            if (point.DistanceTo(start) < GridSize * EscapeZoneCells || point.DistanceTo(end) < GridSize * EscapeZoneCells)
                return false;
            if (_constraints != null && _constraints.IsBlocked(point)) return true;

            double tol = GridSize * ObstacleToleranceFactor;
            foreach (BoundingBoxXYZ bb in obstacles)
            {
                if (bb == null) continue;
                if (point.X >= bb.Min.X - tol && point.X <= bb.Max.X + tol &&
                    point.Y >= bb.Min.Y - tol && point.Y <= bb.Max.Y + tol &&
                    point.Z >= bb.Min.Z - tol && point.Z <= bb.Max.Z + tol)
                    return true;
            }
            return false;
        }

        private bool IsSegmentBlocked(XYZ p1, XYZ p2, List<BoundingBoxXYZ> obstacles, XYZ start, XYZ end)
        {
            if (p1 == null || p2 == null) return true;
            double length = p1.DistanceTo(p2);
            if (length < 1e-9) return false;
            int n = Math.Max(4, (int)Math.Ceiling(length / (GridSize * SegmentSampleFactor)));
            for (int i = 0; i <= n; i++)
            {
                XYZ p = p1 + (p2 - p1) * ((double)i / n);
                if (_routingBounds != null && !IsInsideRoutingVolume(p)) return true;
                if (CheckIfObstacle(p, obstacles, start, end)) return true;
            }
            return false;
        }

        private bool PathHitsObstacle(List<XYZ> path, List<BoundingBoxXYZ> obstacles, XYZ start, XYZ end)
        {
            if (path == null || path.Count < 2) return true;
            for (int i = 0; i < path.Count - 1; i++)
                if (IsSegmentBlocked(path[i], path[i + 1], obstacles, start, end)) return true;
            return false;
        }

        private bool IsInsideRoutingVolume(XYZ p) =>
            _routingBounds == null || IsInsideBox(p, _routingBounds.Min, _routingBounds.Max);

        private static bool IsInsideBox(XYZ p, XYZ min, XYZ max) =>
            p != null && min != null && max != null &&
            p.X >= min.X && p.X <= max.X && p.Y >= min.Y && p.Y <= max.Y && p.Z >= min.Z && p.Z <= max.Z;

        // ================================================================ path

        /// <summary>
        /// Start / end lie exactly on nodes, so the node centres ARE the path.
        /// First and last points are replaced by the exact start / end to kill floating error.
        /// </summary>
        private List<XYZ> RetracePath(AStarNode startNode, AStarNode endNode, XYZ realStart, XYZ realEnd)
        {
            var nodes = new List<XYZ>();
            for (AStarNode n = endNode; n != null && n != startNode; n = n.Parent)
                nodes.Add(n.Center);
            nodes.Reverse();

            var world = new List<XYZ> { realStart };
            world.AddRange(nodes);

            if (world.Count > 1 && world[world.Count - 1].DistanceTo(realEnd) < SnapTolFt * 2)
                world[world.Count - 1] = realEnd;
            else
                world.Add(realEnd);

            return PathUtils.RemoveDuplicates(world);
        }

        private List<XYZ> SimplifyCollinearPoints(List<XYZ> path)
        {
            if (path == null || path.Count < 3) return path;
            var result = new List<XYZ> { path[0] };
            for (int i = 1; i < path.Count - 1; i++)
            {
                XYZ v1 = path[i] - result[result.Count - 1];
                XYZ v2 = path[i + 1] - path[i];
                if (v1.GetLength() < 1e-9 || v2.GetLength() < 1e-9) continue;
                if (!IsSameMajorAxis(NormalizeToMajorAxis(v1), NormalizeToMajorAxis(v2)))
                    result.Add(path[i]);
            }
            result.Add(path[path.Count - 1]);
            return result;
        }

        private double GetHeuristic(XYZ a, XYZ b) =>
            Math.Abs(a.X - b.X) + Math.Abs(a.Y - b.Y) + Math.Abs(a.Z - b.Z) * 3.0;

        private static XYZ NormalizeToMajorAxis(XYZ v)
        {
            if (v == null || v.GetLength() < 1e-9) return XYZ.BasisX;
            double ax = Math.Abs(v.X), ay = Math.Abs(v.Y), az = Math.Abs(v.Z);
            if (ax >= ay && ax >= az) return v.X >= 0 ? XYZ.BasisX : XYZ.BasisX.Negate();
            if (ay >= ax && ay >= az) return v.Y >= 0 ? XYZ.BasisY : XYZ.BasisY.Negate();
            return v.Z >= 0 ? XYZ.BasisZ : XYZ.BasisZ.Negate();
        }

        private static bool IsSameMajorAxis(XYZ a, XYZ b) => a != null && b != null && a.DistanceTo(b) < 0.001;

        private static string GetNodeId(int x, int y, int z) => x + "," + y + "," + z;
    }

    public class ObstacleResult
    {
        public int AllObstaclesCount { get; set; }
        public int IgnoredBoundaryCount { get; set; }
        public int IgnoredLowOverlapCount { get; set; }
        public List<BoundingBoxXYZ> UsedObstacles { get; } = new List<BoundingBoxXYZ>();
    }

    public class SearchBox
    {
        public XYZ Min { get; private set; }
        public XYZ Max { get; private set; }
        public SearchBox(XYZ min, XYZ max) { Min = min; Max = max; }
    }

    public class AStarNode
    {
        public int X { get; private set; }
        public int Y { get; private set; }
        public int Z { get; private set; }
        public XYZ Center { get; private set; }
        public string Id { get; private set; }
        public bool IsObstacle { get; set; }
        public double GCost { get; set; } = double.MaxValue;
        public double HCost { get; set; }
        public AStarNode Parent { get; set; }
        public (int dx, int dy, int dz) Direction { get; set; } = (0, 0, 0);
        public double FCost => GCost + HCost;

        public AStarNode(int x, int y, int z, XYZ center, string id)
        {
            X = x; Y = y; Z = z; Center = center; Id = id;
        }
    }

    public class MinHeap<T>
    {
        private readonly List<(double Priority, T Item)> _items = new List<(double, T)>();
        public int Count => _items.Count;

        public void Push(T item, double priority)
        {
            _items.Add((priority, item));
            int i = _items.Count - 1;
            while (i > 0)
            {
                int p = (i - 1) / 2;
                if (_items[p].Priority <= _items[i].Priority) break;
                (_items[p], _items[i]) = (_items[i], _items[p]);
                i = p;
            }
        }

        public T Pop()
        {
            T root = _items[0].Item;
            int last = _items.Count - 1;
            _items[0] = _items[last];
            _items.RemoveAt(last);
            int i = 0, n = _items.Count;
            while (true)
            {
                int l = i * 2 + 1, r = l + 1, s = i;
                if (l < n && _items[l].Priority < _items[s].Priority) s = l;
                if (r < n && _items[r].Priority < _items[s].Priority) s = r;
                if (s == i) break;
                (_items[s], _items[i]) = (_items[i], _items[s]);
                i = s;
            }
            return root;
        }
    }
}
