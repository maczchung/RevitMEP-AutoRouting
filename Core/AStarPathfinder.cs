using System;
using System.Collections.Generic;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using MEPAutoRouting;

namespace MEPAutoRouting.Core
{
    /// <summary>
    /// BetterRoute Horizontal A* pathfinder.
    /// - 3D A* is allowed, but Z movement has a high penalty.
    /// - Routing volume is the allowed space.
    /// - Likely long corridor boundary walls are filtered out.
    /// - Internal partitions / columns remain obstacles.
    /// - Final path is rebuilt as axis-aligned horizontal / vertical segments only.
    /// - No fallback L-shape. If no valid path is found, returns an empty path.
    /// </summary>
    public class AStarPathfinder
    {
        private readonly Document _doc;
        private readonly BoundingBoxXYZ _routingBounds;
        private readonly RoutingConstraints _constraints;

        private const double GridSize = 0.5;                 // feet, approx. 152 mm
        private const int MarginCells = 12;
        private const int MaxIterations = 400000;

        private const double TurnPenalty = 22.0;
        private const double ZMovePenalty = 600.0;           // strongly keep horizontal
        private const double ReversePenalty = 40.0;
        private const double ContinueStraightBonus = 0.20;
        private const double StartDirectionBonus = 0.15;

        private const double ObstacleToleranceFactor = 0.10;
        private const double EscapeZoneCells = 2.5;
        private const double SegmentSampleFactor = 0.25;

        private const double MinOverlapRatioToKeep = 0.18;
        private const double LongBoundaryRatio = 0.55;
        private const double BoundaryNearToleranceFt = 0.75;

        public AStarPathfinder(Document doc)
        {
            _doc = doc;
            _routingBounds = null;
            _constraints = null;
        }

        public AStarPathfinder(Document doc, BoundingBoxXYZ routingBounds)
        {
            _doc = doc;
            _routingBounds = routingBounds;
            _constraints = null;
        }

        public AStarPathfinder(Document doc, BoundingBoxXYZ routingBounds, RoutingConstraints constraints)
        {
            _doc = doc;
            _routingBounds = routingBounds;
            _constraints = constraints;
        }

        public List<XYZ> FindPath(XYZ start, XYZ end)
        {
            return FindPath(start, end, XYZ.BasisX);
        }

        public List<XYZ> FindPath(XYZ start, XYZ end, XYZ startDir)
        {
            if (start == null || end == null)
                return new List<XYZ>();

            if (start.DistanceTo(end) < 0.001)
                return new List<XYZ>();

            try
            {
                SearchBox box = BuildSearchBox(start, end);

                if (_routingBounds != null)
                {
                    if (!IsInsideRoutingVolume(start) || !IsInsideRoutingVolume(end))
                    {
                        TaskDialog.Show("A* BetterRoute-Horizontal", "Start or End is outside the selected routing volume. No pipe was created.");
                        return new List<XYZ>();
                    }
                }

                XYZ normalizedStartDir = NormalizeToMajorAxis(startDir);
                ObstacleResult obstacleResult = GetFilteredObstacles(box);
                List<BoundingBoxXYZ> obstacles = obstacleResult.UsedObstacles;

                Dictionary<string, AStarNode> grid = new Dictionary<string, AStarNode>();
                AStarNode startNode = GetOrCreateNode(grid, start, box, obstacles, start, end);
                AStarNode endNode = GetOrCreateNode(grid, end, box, obstacles, start, end);

                startNode.IsObstacle = false;
                endNode.IsObstacle = false;
                startNode.GCost = 0.0;
                startNode.HCost = GetHeuristic(startNode.Center, endNode.Center);

                MinHeap<AStarNode> openHeap = new MinHeap<AStarNode>();
                HashSet<string> closedSet = new HashSet<string>();
                openHeap.Push(startNode, startNode.FCost);

                int iterations = 0;

                while (openHeap.Count > 0)
                {
                    iterations++;
                    if (iterations > MaxIterations)
                    {
                        ShowFailureDebug("A* reached max iterations. No fallback pipe was created.", obstacleResult, iterations);
                        return new List<XYZ>();
                    }

                    AStarNode current = openHeap.Pop();
                    if (closedSet.Contains(current.Id))
                        continue;

                    if (current.X == endNode.X && current.Y == endNode.Y && current.Z == endNode.Z)
                    {
                        List<XYZ> result = RetracePath(startNode, current, start, end);
                        result = CleanAxisAlignedPath(result);

                        if (PathHitsObstacle(result, obstacles, start, end))
                        {
                            ShowFailureDebug("A* found a path but final cleaned path intersects an obstacle. No pipe was created.", obstacleResult, iterations);
                            return new List<XYZ>();
                        }

                        TaskDialog.Show(
                            "A* BetterRoute-Horizontal",
                            "Path found." +
                            Environment.NewLine + "Path points: " + result.Count +
                            Environment.NewLine + "Iterations: " + iterations +
                            Environment.NewLine + "All obstacle boxes: " + obstacleResult.AllObstaclesCount +
                            Environment.NewLine + "Used obstacle boxes: " + obstacleResult.UsedObstacles.Count +
                            Environment.NewLine + "Ignored boundary boxes: " + obstacleResult.IgnoredBoundaryCount +
                            Environment.NewLine + "Ignored low-overlap boxes: " + obstacleResult.IgnoredLowOverlapCount);

                        return result;
                    }

                    closedSet.Add(current.Id);

                    foreach (AStarNode neighbor in GetNeighbors(current, grid, box, obstacles, start, end))
                    {
                        if (neighbor.IsObstacle || closedSet.Contains(neighbor.Id))
                            continue;

                        if (IsSegmentBlocked(current.Center, neighbor.Center, obstacles, start, end))
                            continue;

                        double movementCost = GridSize;

                        int dx2 = neighbor.X - current.X;
                        int dy2 = neighbor.Y - current.Y;
                        int dz2 = neighbor.Z - current.Z;

                        if (dz2 != 0)
                            movementCost += ZMovePenalty;

                        if (current.Parent != null)
                        {
                            movementCost += PathUtils.TurnPenalty(current.Direction, (dx2, dy2, dz2), TurnPenalty);
                            if (current.Direction == (-dx2, -dy2, -dz2))
                                movementCost += ReversePenalty;
                            else if (current.Direction == (dx2, dy2, dz2))
                                movementCost -= ContinueStraightBonus;
                        }
                        else
                        {
                            XYZ stepDir = new XYZ(dx2, dy2, dz2);
                            if (IsSameMajorAxis(stepDir, normalizedStartDir))
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

        private void ShowFailureDebug(string title, ObstacleResult obstacleResult, int iterations)
        {
            TaskDialog.Show(
                "A* BetterRoute-Horizontal",
                title +
                Environment.NewLine + "Iterations: " + iterations +
                Environment.NewLine + "GridSize(ft): " + GridSize +
                Environment.NewLine + "RoutingBounds: " + (_routingBounds != null) +
                Environment.NewLine + "All obstacle boxes: " + obstacleResult.AllObstaclesCount +
                Environment.NewLine + "Used obstacle boxes: " + obstacleResult.UsedObstacles.Count +
                Environment.NewLine + "Ignored boundary boxes: " + obstacleResult.IgnoredBoundaryCount +
                Environment.NewLine + "Ignored low-overlap boxes: " + obstacleResult.IgnoredLowOverlapCount +
                Environment.NewLine + "Tip: if no path is found, lower ZMovePenalty or check routing volume height.");
        }

        private SearchBox BuildSearchBox(XYZ start, XYZ end)
        {
            XYZ min;
            XYZ max;

            if (_routingBounds != null)
            {
                XYZ expand = new XYZ(GridSize * 2.0, GridSize * 2.0, GridSize * 2.0);
                min = _routingBounds.Min - expand;
                max = _routingBounds.Max + expand;
            }
            else
            {
                double margin = GridSize * MarginCells;
                min = new XYZ(Math.Min(start.X, end.X) - margin, Math.Min(start.Y, end.Y) - margin, Math.Min(start.Z, end.Z) - margin);
                max = new XYZ(Math.Max(start.X, end.X) + margin, Math.Max(start.Y, end.Y) + margin, Math.Max(start.Z, end.Z) + margin);
            }

            return new SearchBox(min, max);
        }

        private ObstacleResult GetFilteredObstacles(SearchBox box)
        {
            GeometryExtractor extractor = new GeometryExtractor(_doc);
            List<BoundingBoxXYZ> all = new List<BoundingBoxXYZ>();

            AddBoxesSafe(all, extractor, BuiltInCategory.OST_Columns);
            AddBoxesSafe(all, extractor, BuiltInCategory.OST_StructuralColumns);
            AddBoxesSafe(all, extractor, BuiltInCategory.OST_StructuralFraming);

            ObstacleResult result = new ObstacleResult();
            result.AllObstaclesCount = all.Count;

            XYZ filterMin = _routingBounds != null ? _routingBounds.Min : box.Min;
            XYZ filterMax = _routingBounds != null ? _routingBounds.Max : box.Max;

            foreach (BoundingBoxXYZ bb in all)
            {
                if (bb == null)
                    continue;

                if (!BoxesOverlap(bb, box.Min, box.Max))
                    continue;

                if (_routingBounds != null)
                {
                    double overlapRatio = GetXYOverlapRatioAgainstObstacle(bb, filterMin, filterMax);
                    if (overlapRatio < MinOverlapRatioToKeep)
                    {
                        result.IgnoredLowOverlapCount++;
                        continue;
                    }

                    if (IsLikelyBoundaryWall(bb, filterMin, filterMax))
                    {
                        result.IgnoredBoundaryCount++;
                        continue;
                    }
                }

                result.UsedObstacles.Add(bb);
            }

            return result;
        }

        private double GetXYOverlapRatioAgainstObstacle(BoundingBoxXYZ bb, XYZ boxMin, XYZ boxMax)
        {
            double ixMin = Math.Max(bb.Min.X, boxMin.X);
            double ixMax = Math.Min(bb.Max.X, boxMax.X);
            double iyMin = Math.Max(bb.Min.Y, boxMin.Y);
            double iyMax = Math.Min(bb.Max.Y, boxMax.Y);
            double ix = Math.Max(0.0, ixMax - ixMin);
            double iy = Math.Max(0.0, iyMax - iyMin);
            double intersectionArea = ix * iy;
            double obstacleArea = Math.Max(0.000001, Math.Abs(bb.Max.X - bb.Min.X) * Math.Abs(bb.Max.Y - bb.Min.Y));
            return intersectionArea / obstacleArea;
        }

        private bool IsLikelyBoundaryWall(BoundingBoxXYZ bb, XYZ boxMin, XYZ boxMax)
        {
            double boxSpanX = Math.Abs(boxMax.X - boxMin.X);
            double boxSpanY = Math.Abs(boxMax.Y - boxMin.Y);
            double bbSpanX = Math.Abs(bb.Max.X - bb.Min.X);
            double bbSpanY = Math.Abs(bb.Max.Y - bb.Min.Y);
            bool longInX = boxSpanX > 0.001 && bbSpanX / boxSpanX > LongBoundaryRatio;
            bool longInY = boxSpanY > 0.001 && bbSpanY / boxSpanY > LongBoundaryRatio;
            bool nearYBoundary = Math.Abs(bb.Min.Y - boxMin.Y) < BoundaryNearToleranceFt || Math.Abs(bb.Max.Y - boxMax.Y) < BoundaryNearToleranceFt || bb.Max.Y <= boxMin.Y + BoundaryNearToleranceFt || bb.Min.Y >= boxMax.Y - BoundaryNearToleranceFt;
            bool nearXBoundary = Math.Abs(bb.Min.X - boxMin.X) < BoundaryNearToleranceFt || Math.Abs(bb.Max.X - boxMax.X) < BoundaryNearToleranceFt || bb.Max.X <= boxMin.X + BoundaryNearToleranceFt || bb.Min.X >= boxMax.X - BoundaryNearToleranceFt;
            return (longInX && nearYBoundary) || (longInY && nearXBoundary);
        }

        private void AddBoxesSafe(List<BoundingBoxXYZ> boxes, GeometryExtractor extractor, BuiltInCategory category)
        {
            try
            {
                List<BoundingBoxXYZ> result = extractor.GetBoundingBoxes(category);
                if (result != null)
                    boxes.AddRange(result);
            }
            catch { }
        }

        private bool BoxesOverlap(BoundingBoxXYZ bb, XYZ min, XYZ max)
        {
            if (bb.Max.X < min.X || bb.Min.X > max.X) return false;
            if (bb.Max.Y < min.Y || bb.Min.Y > max.Y) return false;
            if (bb.Max.Z < min.Z || bb.Min.Z > max.Z) return false;
            return true;
        }

        private AStarNode GetOrCreateNode(Dictionary<string, AStarNode> grid, XYZ point, SearchBox box, List<BoundingBoxXYZ> obstacles, XYZ start, XYZ end)
        {
            int x = (int)Math.Round((point.X - box.Min.X) / GridSize);
            int y = (int)Math.Round((point.Y - box.Min.Y) / GridSize);
            int z = (int)Math.Round((point.Z - box.Min.Z) / GridSize);
            string id = GetNodeId(x, y, z);
            AStarNode existing;
            if (grid.TryGetValue(id, out existing))
                return existing;

            XYZ center = new XYZ(box.Min.X + x * GridSize, box.Min.Y + y * GridSize, box.Min.Z + z * GridSize);
            AStarNode node = new AStarNode(x, y, z, center, id);
            bool outsideRoutingVolume = _routingBounds != null && !IsInsideRoutingVolume(center);
            node.IsObstacle = outsideRoutingVolume || CheckIfObstacle(center, obstacles, start, end);
            grid[id] = node;
            return node;
        }

        private List<AStarNode> GetNeighbors(AStarNode node, Dictionary<string, AStarNode> grid, SearchBox box, List<BoundingBoxXYZ> obstacles, XYZ start, XYZ end)
        {
            List<AStarNode> neighbors = new List<AStarNode>();
            foreach ((int dx, int dy, int dz) direction in PathUtils.SixNeighbours)
            {
                int nx = node.X + direction.dx;
                int ny = node.Y + direction.dy;
                int nz = node.Z + direction.dz;
                XYZ center = new XYZ(box.Min.X + nx * GridSize, box.Min.Y + ny * GridSize, box.Min.Z + nz * GridSize);
                if (!IsInsideBox(center, box.Min, box.Max))
                    continue;
                if (_routingBounds != null && !IsInsideRoutingVolume(center))
                    continue;

                string id = GetNodeId(nx, ny, nz);
                AStarNode existing;
                if (!grid.TryGetValue(id, out existing))
                {
                    existing = new AStarNode(nx, ny, nz, center, id);
                    existing.IsObstacle = CheckIfObstacle(center, obstacles, start, end);
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
            if (_constraints != null && _constraints.IsBlocked(point))
                return true;
            double tolerance = GridSize * ObstacleToleranceFactor;
            foreach (BoundingBoxXYZ bb in obstacles)
            {
                if (bb == null) continue;
                if (point.X >= bb.Min.X - tolerance && point.X <= bb.Max.X + tolerance && point.Y >= bb.Min.Y - tolerance && point.Y <= bb.Max.Y + tolerance && point.Z >= bb.Min.Z - tolerance && point.Z <= bb.Max.Z + tolerance)
                    return true;
            }
            return false;
        }

        private bool IsSegmentBlocked(XYZ p1, XYZ p2, List<BoundingBoxXYZ> obstacles, XYZ start, XYZ end)
        {
            if (p1 == null || p2 == null) return true;
            double length = p1.DistanceTo(p2);
            if (length < 1e-9) return false;
            double step = GridSize * SegmentSampleFactor;
            int sampleCount = Math.Max(4, (int)Math.Ceiling(length / step));
            for (int i = 0; i <= sampleCount; i++)
            {
                double t = (double)i / (double)sampleCount;
                XYZ p = p1 + (p2 - p1) * t;
                if (_routingBounds != null && !IsInsideRoutingVolume(p)) return true;
                if (CheckIfObstacle(p, obstacles, start, end)) return true;
            }
            return false;
        }

        private bool PathHitsObstacle(List<XYZ> path, List<BoundingBoxXYZ> obstacles, XYZ start, XYZ end)
        {
            if (path == null || path.Count < 2) return true;
            for (int i = 0; i < path.Count - 1; i++)
            {
                if (IsSegmentBlocked(path[i], path[i + 1], obstacles, start, end)) return true;
            }
            return false;
        }

        private bool IsInsideRoutingVolume(XYZ point)
        {
            if (_routingBounds == null) return true;
            return IsInsideBox(point, _routingBounds.Min, _routingBounds.Max);
        }

        private bool IsInsideBox(XYZ point, XYZ min, XYZ max)
        {
            if (point == null || min == null || max == null) return false;
            return point.X >= min.X && point.X <= max.X && point.Y >= min.Y && point.Y <= max.Y && point.Z >= min.Z && point.Z <= max.Z;
        }

        private List<XYZ> RetracePath(AStarNode startNode, AStarNode endNode, XYZ realStart, XYZ realEnd)
        {
            List<XYZ> gridPath = new List<XYZ>();
            AStarNode current = endNode;
            while (current != null && current != startNode)
            {
                gridPath.Add(current.Center);
                current = current.Parent;
            }
            gridPath.Reverse();
            List<XYZ> worldPath = new List<XYZ> { realStart };
            worldPath.AddRange(gridPath);
            worldPath.Add(realEnd);
            return PathUtils.MergeCollinear(worldPath);
        }

        private List<XYZ> CleanAxisAlignedPath(List<XYZ> path)
        {
            if (path == null || path.Count < 2) return path;
            List<XYZ> rounded = RoundPath(path);
            List<XYZ> simplified = SimplifyCollinearPoints(rounded);
            return RemoveVeryShortSegments(simplified);
        }

        private List<XYZ> RoundPath(List<XYZ> path)
        {
            List<XYZ> result = new List<XYZ>();
            foreach (XYZ p in path)
            {
                if (p == null) continue;
                double x = Math.Round(p.X / 0.0001) * 0.0001;
                double y = Math.Round(p.Y / 0.0001) * 0.0001;
                double z = Math.Round(p.Z / 0.0001) * 0.0001;
                result.Add(new XYZ(x, y, z));
            }
            return result;
        }

        private List<XYZ> SimplifyCollinearPoints(List<XYZ> path)
        {
            if (path == null || path.Count < 3) return path;
            List<XYZ> result = new List<XYZ>();
            result.Add(path[0]);
            for (int i = 1; i < path.Count - 1; i++)
            {
                XYZ previous = result[result.Count - 1];
                XYZ current = path[i];
                XYZ next = path[i + 1];
                XYZ v1 = current - previous;
                XYZ v2 = next - current;
                if (v1.GetLength() < 1e-9 || v2.GetLength() < 1e-9) continue;
                XYZ d1 = NormalizeToMajorAxis(v1);
                XYZ d2 = NormalizeToMajorAxis(v2);
                if (!IsSameMajorAxis(d1, d2)) result.Add(current);
            }
            result.Add(path[path.Count - 1]);
            return result;
        }

        private List<XYZ> RemoveVeryShortSegments(List<XYZ> path)
        {
            if (path == null || path.Count < 2) return path;
            List<XYZ> cleaned = new List<XYZ>();
            cleaned.Add(path[0]);
            for (int i = 1; i < path.Count; i++)
            {
                if (cleaned[cleaned.Count - 1].DistanceTo(path[i]) >= 0.05)
                    cleaned.Add(path[i]);
            }
            return cleaned;
        }

        private double GetHeuristic(XYZ a, XYZ b)
        {
            return Math.Abs(a.X - b.X) + Math.Abs(a.Y - b.Y) + Math.Abs(a.Z - b.Z) * 3.0;
        }

        private XYZ NormalizeToMajorAxis(XYZ vector)
        {
            if (vector == null || vector.GetLength() < 1e-9) return XYZ.BasisX;
            double ax = Math.Abs(vector.X);
            double ay = Math.Abs(vector.Y);
            double az = Math.Abs(vector.Z);
            if (ax >= ay && ax >= az) return vector.X >= 0 ? XYZ.BasisX : XYZ.BasisX.Negate();
            if (ay >= ax && ay >= az) return vector.Y >= 0 ? XYZ.BasisY : XYZ.BasisY.Negate();
            return vector.Z >= 0 ? XYZ.BasisZ : XYZ.BasisZ.Negate();
        }

        private bool IsSameMajorAxis(XYZ a, XYZ b)
        {
            if (a == null || b == null) return false;
            return a.DistanceTo(b) < 0.001;
        }

        private string GetNodeId(int x, int y, int z)
        {
            return x + "," + y + "," + z;
        }
    }

    public class ObstacleResult
    {
        public int AllObstaclesCount { get; set; }
        public int IgnoredBoundaryCount { get; set; }
        public int IgnoredLowOverlapCount { get; set; }
        public List<BoundingBoxXYZ> UsedObstacles { get; private set; }
        public ObstacleResult()
        {
            AllObstaclesCount = 0;
            IgnoredBoundaryCount = 0;
            IgnoredLowOverlapCount = 0;
            UsedObstacles = new List<BoundingBoxXYZ>();
        }
    }

    public class SearchBox
    {
        public XYZ Min { get; private set; }
        public XYZ Max { get; private set; }
        public SearchBox(XYZ min, XYZ max)
        {
            Min = min;
            Max = max;
        }
    }

    public class AStarNode
    {
        public int X { get; private set; }
        public int Y { get; private set; }
        public int Z { get; private set; }
        public XYZ Center { get; private set; }
        public string Id { get; private set; }
        public bool IsObstacle { get; set; }
        public double GCost { get; set; }
        public double HCost { get; set; }
        public AStarNode Parent { get; set; }
        public (int dx, int dy, int dz) Direction { get; set; }
        public double FCost { get { return GCost + HCost; } }
        public AStarNode(int x, int y, int z, XYZ center, string id)
        {
            X = x; Y = y; Z = z; Center = center; Id = id;
            IsObstacle = false; GCost = double.MaxValue; HCost = 0.0; Parent = null; Direction = (0, 0, 0);
        }
    }

    public class MinHeap<T>
    {
        private class HeapNode
        {
            public double Priority;
            public T Item;
            public HeapNode(double priority, T item)
            {
                Priority = priority;
                Item = item;
            }
        }
        private readonly List<HeapNode> _items = new List<HeapNode>();
        public int Count { get { return _items.Count; } }
        public void Push(T item, double priority)
        {
            _items.Add(new HeapNode(priority, item));
            SiftUp(_items.Count - 1);
        }
        public T Pop()
        {
            HeapNode root = _items[0];
            HeapNode last = _items[_items.Count - 1];
            _items.RemoveAt(_items.Count - 1);
            if (_items.Count > 0)
            {
                _items[0] = last;
                SiftDown(0);
            }
            return root.Item;
        }
        private void SiftUp(int index)
        {
            while (index > 0)
            {
                int parent = (index - 1) / 2;
                if (_items[parent].Priority <= _items[index].Priority) break;
                HeapNode temp = _items[parent];
                _items[parent] = _items[index];
                _items[index] = temp;
                index = parent;
            }
        }
        private void SiftDown(int index)
        {
            int count = _items.Count;
            while (true)
            {
                int left = index * 2 + 1;
                int right = left + 1;
                int smallest = index;
                if (left < count && _items[left].Priority < _items[smallest].Priority) smallest = left;
                if (right < count && _items[right].Priority < _items[smallest].Priority) smallest = right;
                if (smallest == index) break;
                HeapNode temp = _items[index];
                _items[index] = _items[smallest];
                _items[smallest] = temp;
                index = smallest;
            }
        }
    }
}
