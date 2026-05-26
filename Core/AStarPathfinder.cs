using System;
using System.Collections.Generic;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;

namespace MEPAutoRouting.Core
{
    /// <summary>
    /// Tuned A* for MEP routing inside a selected host Room / Space / Mass / Generic Model volume.
    /// Goals:
    /// 1. Route must stay inside routingBounds when routingBounds is provided.
    /// 2. Prefer horizontal movement and avoid unnecessary Z movement.
    /// 3. Reduce zig-zag by adding turn penalty and centerline bias.
    /// 4. Avoid host/link walls/columns/framing from GeometryExtractor.
    /// </summary>
    public class AStarPathfinder
    {
        private readonly Document _doc;
        private readonly BoundingBoxXYZ _routingBounds;

        // Revit internal unit = feet.
        private const double GridSize = 1.0;              // 1 ft gives better routing than 2 ft but still safe.
        private const int MarginCells = 8;                // fallback only when no routing volume is selected.
        private const int MaxIterations = 90000;          // fail-safe.
        private const double TurnPenalty = 8.0;           // stronger straight-line preference.
        private const double ZMovePenalty = 30.0;         // strongly avoid vertical movement.
        private const double CenterBiasFactor = 0.15;     // small cost to keep route near volume center.
        private const double ObstacleToleranceFactor = 0.25;
        private const double EscapeZoneCells = 2.0;

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
                return MEPAutoRouting.Shared.SimplePath.GenerateLShape(start, end);

            if (start.DistanceTo(end) < 0.001)
                return new List<XYZ> { start, end };

            try
            {
                SearchBox box = BuildSearchBox(start, end);

                if (_routingBounds != null)
                {
                    if (!IsInsideBox(start, box.Min, box.Max) || !IsInsideBox(end, box.Min, box.Max))
                    {
                        TaskDialog.Show("A* Debug", "Start or End is outside routing volume. Fallback to L-shape.");
                        return MEPAutoRouting.Shared.SimplePath.GenerateLShape(start, end);
                    }
                }

                List<BoundingBoxXYZ> obstacles = GetObstacleBoxes();

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
                            "A* Debug",
                            "A* reached max iterations. Fallback to L-shape." +
                            Environment.NewLine + "Iterations: " + iterations +
                            Environment.NewLine + "GridSize(ft): " + GridSize +
                            Environment.NewLine + "RoutingBounds: " + (_routingBounds != null) +
                            Environment.NewLine + "Obstacles: " + obstacles.Count);

                        return MEPAutoRouting.Shared.SimplePath.GenerateLShape(start, end);
                    }

                    AStarNode current = openHeap.Pop();

                    if (closedSet.Contains(current.Id))
                        continue;

                    if (current.X == endNode.X && current.Y == endNode.Y && current.Z == endNode.Z)
                    {
                        List<XYZ> result = RetracePath(startNode, current, start, end);
                        return SimplifyPath(result);
                    }

                    closedSet.Add(current.Id);

                    foreach (AStarNode neighbor in GetNeighbors(current, grid, box, obstacles, start, end))
                    {
                        if (neighbor.IsObstacle || closedSet.Contains(neighbor.Id))
                            continue;

                        double movementCost = GridSize;

                        // Strongly prefer staying on the same elevation.
                        if (neighbor.Z != current.Z)
                            movementCost += ZMovePenalty;

                        // Penalize turns to avoid zig-zag path.
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

                        // Small bias towards routing volume center, useful for corridor-like routing volumes.
                        if (_routingBounds != null)
                            movementCost += GetCenterBiasCost(neighbor.Center, box);

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
                    "A* Debug",
                    "A* could not find a path. Fallback to L-shape." +
                    Environment.NewLine + "RoutingBounds: " + (_routingBounds != null) +
                    Environment.NewLine + "Obstacles: " + obstacles.Count);

                return MEPAutoRouting.Shared.SimplePath.GenerateLShape(start, end);
            }
            catch (Exception ex)
            {
                TaskDialog.Show("A* Error", ex.ToString());
                return MEPAutoRouting.Shared.SimplePath.GenerateLShape(start, end);
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

                min = new XYZ(
                    Math.Min(start.X, end.X) - margin,
                    Math.Min(start.Y, end.Y) - margin,
                    Math.Min(start.Z, end.Z) - margin);

                max = new XYZ(
                    Math.Max(start.X, end.X) + margin,
                    Math.Max(start.Y, end.Y) + margin,
                    Math.Max(start.Z, end.Z) + margin);
            }

            return new SearchBox(min, max);
        }

        private List<BoundingBoxXYZ> GetObstacleBoxes()
        {
            GeometryExtractor extractor = new GeometryExtractor(_doc);
            List<BoundingBoxXYZ> obstacles = new List<BoundingBoxXYZ>();

            // For routing inside a Room/Space/Mass volume, do NOT include Floors/Ceilings by default,
            // otherwise the selected volume may be fully blocked vertically.
            AddBoxesSafe(obstacles, extractor, BuiltInCategory.OST_Walls);
            AddBoxesSafe(obstacles, extractor, BuiltInCategory.OST_Columns);
            AddBoxesSafe(obstacles, extractor, BuiltInCategory.OST_StructuralColumns);
            AddBoxesSafe(obstacles, extractor, BuiltInCategory.OST_StructuralFraming);

            // Enable these later only if performance is acceptable.
            // AddBoxesSafe(obstacles, extractor, BuiltInCategory.OST_DuctCurves);
            // AddBoxesSafe(obstacles, extractor, BuiltInCategory.OST_PipeCurves);
            // AddBoxesSafe(obstacles, extractor, BuiltInCategory.OST_CableTray);
            // AddBoxesSafe(obstacles, extractor, BuiltInCategory.OST_Conduit);

            return obstacles;
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

        private AStarNode GetOrCreateNode(
            Dictionary<string, AStarNode> grid,
            XYZ point,
            SearchBox box,
            List<BoundingBoxXYZ> obstacles,
            XYZ start,
            XYZ end)
        {
            int x = (int)Math.Round((point.X - box.Min.X) / GridSize);
            int y = (int)Math.Round((point.Y - box.Min.Y) / GridSize);
            int z = (int)Math.Round((point.Z - box.Min.Z) / GridSize);

            string id = GetNodeId(x, y, z);

            AStarNode existing;
            if (grid.TryGetValue(id, out existing))
                return existing;

            XYZ center = new XYZ(
                box.Min.X + x * GridSize,
                box.Min.Y + y * GridSize,
                box.Min.Z + z * GridSize);

            AStarNode node = new AStarNode(x, y, z, center, id);
            node.IsObstacle = !IsInsideBox(center, box.Min, box.Max) || CheckIfObstacle(center, obstacles, start, end);

            grid[id] = node;
            return node;
        }

        private List<AStarNode> GetNeighbors(
            AStarNode node,
            Dictionary<string, AStarNode> grid,
            SearchBox box,
            List<BoundingBoxXYZ> obstacles,
            XYZ start,
            XYZ end)
        {
            List<AStarNode> neighbors = new List<AStarNode>();

            // XY directions first. Z directions are available but expensive.
            int[,] dirs = new int[,]
            {
                {  1,  0,  0 },
                { -1,  0,  0 },
                {  0,  1,  0 },
                {  0, -1,  0 },
                {  0,  0,  1 },
                {  0,  0, -1 }
            };

            for (int i = 0; i < 6; i++)
            {
                int nx = node.X + dirs[i, 0];
                int ny = node.Y + dirs[i, 1];
                int nz = node.Z + dirs[i, 2];

                XYZ center = new XYZ(
                    box.Min.X + nx * GridSize,
                    box.Min.Y + ny * GridSize,
                    box.Min.Z + nz * GridSize);

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

                if (point.X >= bb.Min.X - tolerance &&
                    point.X <= bb.Max.X + tolerance &&
                    point.Y >= bb.Min.Y - tolerance &&
                    point.Y <= bb.Max.Y + tolerance &&
                    point.Z >= bb.Min.Z - tolerance &&
                    point.Z <= bb.Max.Z + tolerance)
                {
                    return true;
                }
            }

            return false;
        }

        private bool IsInsideBox(XYZ point, XYZ min, XYZ max)
        {
            if (point == null || min == null || max == null)
                return false;

            return point.X >= min.X && point.X <= max.X &&
                   point.Y >= min.Y && point.Y <= max.Y &&
                   point.Z >= min.Z && point.Z <= max.Z;
        }

        private double GetCenterBiasCost(XYZ point, SearchBox box)
        {
            XYZ center = new XYZ(
                (box.Min.X + box.Max.X) / 2.0,
                (box.Min.Y + box.Max.Y) / 2.0,
                point.Z);

            // Small cost only. This helps avoid hugging wall/edge of routing volume.
            return point.DistanceTo(center) * CenterBiasFactor;
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
            return result;
        }

        private double GetHeuristic(XYZ a, XYZ b)
        {
            // Manhattan works better for orthogonal routing.
            return Math.Abs(a.X - b.X) + Math.Abs(a.Y - b.Y) + Math.Abs(a.Z - b.Z) * 2.0;
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
