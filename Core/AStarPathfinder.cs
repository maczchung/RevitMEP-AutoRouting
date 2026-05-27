using System;
using System.Collections.Generic;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;

namespace MEPAutoRouting.Core
{
    /// <summary>
    /// GapFix A* pathfinder.
    /// Purpose:
    /// - Keep route inside Host routing volume.
    /// - Fixed Z / horizontal routing.
    /// - Avoid wall/partition obstacles.
    /// - Use smaller grid and smaller obstacle expansion so the algorithm can pass through real gaps.
    /// - No fallback L-shape. If A* fails, no wrong straight pipe will be created.
    /// </summary>
    public class AStarPathfinder
    {
        private readonly Document _doc;
        private readonly BoundingBoxXYZ _routingBounds;

        private const double GridSize = 0.5;                 // 0.5 ft approx 152 mm. Better for narrow gaps.
        private const int MarginCells = 12;
        private const int MaxIterations = 250000;
        private const double TurnPenalty = 8.0;
        private const double ObstacleToleranceFactor = 0.20; // Lower than 0.75 so gaps do not get closed.
        private const double EscapeZoneCells = 1.0;
        private const double SegmentSampleStepFactor = 0.50;

        public AStarPathfinder(Document doc)
        {
            _doc = doc;
            _routingBounds = null;
        }

        public AStarPathfinder(Document doc, BoundingBoxXYZ routingBounds)
        {
            _doc = doc;
            _routingBounds = routingBounds;
        }

        public List<XYZ> FindPath(XYZ start, XYZ end)
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
                    if (!IsInsideBox(start, box.Min, box.Max) || !IsInsideBox(end, box.Min, box.Max))
                    {
                        TaskDialog.Show("A* GapFix", "Start or End is outside routing volume. No pipe was created.");
                        return new List<XYZ>();
                    }
                }

                List<BoundingBoxXYZ> obstacles = GetObstacleBoxes(box);

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
                        TaskDialog.Show(
                            "A* GapFix",
                            "A* reached max iterations. No fallback pipe was created." +
                            Environment.NewLine + "Iterations: " + iterations +
                            Environment.NewLine + "GridSize(ft): " + GridSize +
                            Environment.NewLine + "RoutingBounds: " + (_routingBounds != null) +
                            Environment.NewLine + "Obstacle boxes: " + obstacles.Count +
                            Environment.NewLine + "Tip: reduce obstacles/tolerance or enlarge routing volume gap.");

                        return new List<XYZ>();
                    }

                    AStarNode current = openHeap.Pop();
                    if (closedSet.Contains(current.Id))
                        continue;

                    if (current.X == endNode.X && current.Y == endNode.Y && current.Z == endNode.Z)
                    {
                        List<XYZ> result = RetracePath(startNode, current, start, end);
                        result = SimplifyPath(result);

                        if (PathHitsObstacle(result, obstacles, start, end))
                        {
                            TaskDialog.Show(
                                "A* GapFix",
                                "A* found a path but final simplified path intersects an obstacle. No pipe was created." +
                                Environment.NewLine + "Obstacle boxes: " + obstacles.Count);
                            return new List<XYZ>();
                        }

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

                        if (current.Parent != null)
                        {
                            int dx1 = current.X - current.Parent.X;
                            int dy1 = current.Y - current.Parent.Y;
                            int dz1 = current.Z - current.Parent.Z;

                            int dx2 = neighbor.X - current.X;
                            int dy2 = neighbor.Y - current.Y;
                            int dz2 = neighbor.Z - current.Z;

                            if (dx1 != dx2 || dy1 != dy2 || dz1 != dz2)
                                movementCost += TurnPenalty;
                        }

                        double tentativeG = current.GCost + movementCost;

                        if (tentativeG < neighbor.GCost)
                        {
                            neighbor.Parent = current;
                            neighbor.GCost = tentativeG;
                            neighbor.HCost = GetHeuristic(neighbor.Center, endNode.Center);
                            openHeap.Push(neighbor, neighbor.FCost);
                        }
                    }
                }

                TaskDialog.Show(
                    "A* GapFix",
                    "A* could not find a valid obstacle-free path. No fallback pipe was created." +
                    Environment.NewLine + "RoutingBounds: " + (_routingBounds != null) +
                    Environment.NewLine + "Obstacle boxes: " + obstacles.Count +
                    Environment.NewLine + "Likely cause: selected volume gaps are closed by obstacle tolerance or physical partition layout.");

                return new List<XYZ>();
            }
            catch (Exception ex)
            {
                TaskDialog.Show("A* GapFix Error", ex.ToString());
                return new List<XYZ>();
            }
        }

        private SearchBox BuildSearchBox(XYZ start, XYZ end)
        {
            XYZ min;
            XYZ max;

            if (_routingBounds != null)
            {
                min = _routingBounds.Min;
                max = _routingBounds.Max;
            }
            else
            {
                double margin = GridSize * MarginCells;
                double zMin = Math.Min(start.Z, end.Z) - GridSize;
                double zMax = Math.Max(start.Z, end.Z) + GridSize;

                min = new XYZ(Math.Min(start.X, end.X) - margin, Math.Min(start.Y, end.Y) - margin, zMin);
                max = new XYZ(Math.Max(start.X, end.X) + margin, Math.Max(start.Y, end.Y) + margin, zMax);
            }

            return new SearchBox(min, max);
        }

        private List<BoundingBoxXYZ> GetObstacleBoxes(SearchBox box)
        {
            GeometryExtractor extractor = new GeometryExtractor(_doc);
            List<BoundingBoxXYZ> all = new List<BoundingBoxXYZ>();

            AddBoxesSafe(all, extractor, BuiltInCategory.OST_Walls);
            AddBoxesSafe(all, extractor, BuiltInCategory.OST_Columns);
            AddBoxesSafe(all, extractor, BuiltInCategory.OST_StructuralColumns);
            AddBoxesSafe(all, extractor, BuiltInCategory.OST_StructuralFraming);

            List<BoundingBoxXYZ> filtered = new List<BoundingBoxXYZ>();
            foreach (BoundingBoxXYZ bb in all)
            {
                if (bb == null)
                    continue;

                if (BoxesOverlap(bb, box.Min, box.Max))
                    filtered.Add(bb);
            }

            return filtered;
        }

        private void AddBoxesSafe(List<BoundingBoxXYZ> boxes, GeometryExtractor extractor, BuiltInCategory category)
        {
            try
            {
                List<BoundingBoxXYZ> result = extractor.GetBoundingBoxes(category);
                if (result != null)
                    boxes.AddRange(result);
            }
            catch
            {
                // Ignore category extraction failure.
            }
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
            int z = (int)Math.Round((start.Z - box.Min.Z) / GridSize);

            string id = GetNodeId(x, y, z);

            AStarNode existing;
            if (grid.TryGetValue(id, out existing))
                return existing;

            XYZ center = new XYZ(box.Min.X + x * GridSize, box.Min.Y + y * GridSize, box.Min.Z + z * GridSize);
            AStarNode node = new AStarNode(x, y, z, center, id);
            node.IsObstacle = !IsInsideBox(center, box.Min, box.Max) || CheckIfObstacle(center, obstacles, start, end);
            grid[id] = node;
            return node;
        }

        private List<AStarNode> GetNeighbors(AStarNode node, Dictionary<string, AStarNode> grid, SearchBox box, List<BoundingBoxXYZ> obstacles, XYZ start, XYZ end)
        {
            List<AStarNode> neighbors = new List<AStarNode>();

            int[,] dirs = new int[,]
            {
                { 1, 0, 0 },
                { -1, 0, 0 },
                { 0, 1, 0 },
                { 0, -1, 0 }
            };

            for (int i = 0; i < 4; i++)
            {
                int nx = node.X + dirs[i, 0];
                int ny = node.Y + dirs[i, 1];
                int nz = node.Z;

                XYZ center = new XYZ(box.Min.X + nx * GridSize, box.Min.Y + ny * GridSize, box.Min.Z + nz * GridSize);

                if (!IsInsideBox(center, box.Min, box.Max))
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
            if (point.DistanceTo(start) < GridSize * EscapeZoneCells ||
                point.DistanceTo(end) < GridSize * EscapeZoneCells)
                return false;

            double tolerance = GridSize * ObstacleToleranceFactor;

            foreach (BoundingBoxXYZ bb in obstacles)
            {
                if (bb == null)
                    continue;

                if (point.X >= bb.Min.X - tolerance && point.X <= bb.Max.X + tolerance &&
                    point.Y >= bb.Min.Y - tolerance && point.Y <= bb.Max.Y + tolerance &&
                    point.Z >= bb.Min.Z - tolerance && point.Z <= bb.Max.Z + tolerance)
                    return true;
            }

            return false;
        }

        private bool IsSegmentBlocked(XYZ p1, XYZ p2, List<BoundingBoxXYZ> obstacles, XYZ start, XYZ end)
        {
            if (p1 == null || p2 == null)
                return true;

            double length = p1.DistanceTo(p2);
            if (length < 1e-9)
                return false;

            double step = GridSize * 0.25;
            int sampleCount = Math.Max(4, (int)Math.Ceiling(length / step));

            for (int i = 0; i <= sampleCount; i++)
            {
                double t = (double)i / (double)sampleCount;
                XYZ p = p1 + (p2 - p1) * t;

                if (CheckIfObstacle(p, obstacles, start, end))
                    return true;
            }

            return false;
        }

        private bool PathHitsObstacle(List<XYZ> path, List<BoundingBoxXYZ> obstacles, XYZ start, XYZ end)
        {
            if (path == null || path.Count < 2)
                return true;

            for (int i = 0; i < path.Count - 1; i++)
            {
                if (IsSegmentBlocked(path[i], path[i + 1], obstacles, start, end))
                    return true;
            }

            return false;
        }

        private bool IsInsideBox(XYZ point, XYZ min, XYZ max)
        {
            if (point == null || min == null || max == null)
                return false;

            return point.X >= min.X && point.X <= max.X && point.Y >= min.Y && point.Y <= max.Y && point.Z >= min.Z && point.Z <= max.Z;
        }

        private List<XYZ> RetracePath(AStarNode startNode, AStarNode endNode, XYZ realStart, XYZ realEnd)
        {
            List<XYZ> path = new List<XYZ>();
            AStarNode current = endNode;

            while (current != null && current != startNode)
            {
                path.Add(current.Center);
                current = current.Parent;
            }

            path.Reverse();

            List<XYZ> finalPath = new List<XYZ>();
            finalPath.Add(realStart);
            finalPath.AddRange(path);
            finalPath.Add(realEnd);
            return finalPath;
        }

        private List<XYZ> SimplifyPath(List<XYZ> path)
        {
            if (path == null || path.Count < 3)
                return path;

            List<XYZ> result = new List<XYZ>();
            result.Add(path[0]);

            for (int i = 1; i < path.Count - 1; i++)
            {
                XYZ previous = result[result.Count - 1];
                XYZ current = path[i];
                XYZ next = path[i + 1];

                XYZ v1 = current - previous;
                XYZ v2 = next - current;

                if (v1.GetLength() < 1e-9 || v2.GetLength() < 1e-9)
                    continue;

                XYZ d1 = v1.Normalize();
                XYZ d2 = v2.Normalize();

                if (d1.DistanceTo(d2) > 0.01)
                    result.Add(current);
            }

            result.Add(path[path.Count - 1]);
            return RemoveVeryShortSegments(result);
        }

        private List<XYZ> RemoveVeryShortSegments(List<XYZ> path)
        {
            if (path == null || path.Count < 2)
                return path;

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
            return Math.Abs(a.X - b.X) + Math.Abs(a.Y - b.Y);
        }

        private string GetNodeId(int x, int y, int z)
        {
            return x + "," + y + "," + z;
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

        public double FCost
        {
            get { return GCost + HCost; }
        }

        public AStarNode(int x, int y, int z, XYZ center, string id)
        {
            X = x;
            Y = y;
            Z = z;
            Center = center;
            Id = id;
            IsObstacle = false;
            GCost = double.MaxValue;
            HCost = 0.0;
            Parent = null;
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

        public int Count
        {
            get { return _items.Count; }
        }

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

                if (_items[parent].Priority <= _items[index].Priority)
                    break;

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

                if (left < count && _items[left].Priority < _items[smallest].Priority)
                    smallest = left;

                if (right < count && _items[right].Priority < _items[smallest].Priority)
                    smallest = right;

                if (smallest == index)
                    break;

                HeapNode temp = _items[index];
                _items[index] = _items[smallest];
                _items[smallest] = temp;
                index = smallest;
            }
        }
    }
}
