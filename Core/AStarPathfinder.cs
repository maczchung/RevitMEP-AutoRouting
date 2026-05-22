using System;
using System.Collections.Generic;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;

namespace MEPAutoRouting.Core
{
    public class AStarPathfinder
    {
        private readonly Document _doc;

        // Performance-safe settings. Revit internal unit = feet.
        private const double GridSize = 2.0;          // 2 ft. Reduce to 1.0 later if performance is OK.
        private const int MarginCells = 10;           // Search margin = 20 ft.
        private const int MaxIterations = 60000;      // Prevent Revit from freezing.
        private const double TurnPenalty = 4.0;       // Prefer straighter routing.
        private const double ZMovePenalty = 8.0;      // Prefer horizontal routing.
        private const double ObstacleToleranceFactor = 0.45;
        private const double EscapeZoneCells = 2.5;   // Start/end safety zone.

        public AStarPathfinder(Document doc)
        {
            _doc = doc;
        }

        public List<XYZ> FindPath(XYZ start, XYZ end)
        {
            if (start == null || end == null)
                return MEPAutoRouting.Shared.SimplePath.GenerateLShape(start, end);

            if (start.DistanceTo(end) < 0.001)
                return new List<XYZ> { start, end };

            try
            {
                List<BoundingBoxXYZ> obstacles = GetObstacleBoxes();

                double margin = GridSize * MarginCells;
                XYZ min = new XYZ(
                    Math.Min(start.X, end.X) - margin,
                    Math.Min(start.Y, end.Y) - margin,
                    Math.Min(start.Z, end.Z) - margin);

                XYZ max = new XYZ(
                    Math.Max(start.X, end.X) + margin,
                    Math.Max(start.Y, end.Y) + margin,
                    Math.Max(start.Z, end.Z) + margin);

                Dictionary<string, AStarNode> grid = new Dictionary<string, AStarNode>();

                AStarNode startNode = GetOrCreateNode(grid, start, min, obstacles, start, end);
                AStarNode endNode = GetOrCreateNode(grid, end, min, obstacles, start, end);

                startNode.IsObstacle = false;
                endNode.IsObstacle = false;
                startNode.GCost = 0.0;
                startNode.HCost = GetManhattanDistance(startNode.Center, endNode.Center);

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

                    foreach (AStarNode neighbor in GetNeighbors(current, grid, min, max, obstacles, start, end))
                    {
                        if (neighbor.IsObstacle || closedSet.Contains(neighbor.Id))
                            continue;

                        double movementCost = GridSize;

                        if (neighbor.Z != current.Z)
                            movementCost += ZMovePenalty;

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
                            neighbor.HCost = GetManhattanDistance(neighbor.Center, endNode.Center);
                            openHeap.Push(neighbor, neighbor.FCost);
                        }
                    }
                }

                TaskDialog.Show(
                    "A* Debug",
                    "A* could not find a path. Fallback to L-shape." +
                    Environment.NewLine + "GridSize(ft): " + GridSize +
                    Environment.NewLine + "Obstacles: " + obstacles.Count);

                return MEPAutoRouting.Shared.SimplePath.GenerateLShape(start, end);
            }
            catch (Exception ex)
            {
                TaskDialog.Show("A* Error", ex.ToString());
                return MEPAutoRouting.Shared.SimplePath.GenerateLShape(start, end);
            }
        }

        private List<BoundingBoxXYZ> GetObstacleBoxes()
        {
            GeometryExtractor extractor = new GeometryExtractor(_doc);
            List<BoundingBoxXYZ> obstacles = new List<BoundingBoxXYZ>();

            AddBoxesSafe(obstacles, extractor, BuiltInCategory.OST_Walls);
            AddBoxesSafe(obstacles, extractor, BuiltInCategory.OST_Columns);
            AddBoxesSafe(obstacles, extractor, BuiltInCategory.OST_StructuralColumns);
            AddBoxesSafe(obstacles, extractor, BuiltInCategory.OST_StructuralFraming);
            AddBoxesSafe(obstacles, extractor, BuiltInCategory.OST_Floors);
            AddBoxesSafe(obstacles, extractor, BuiltInCategory.OST_Ceilings);

            // Keep MEP categories disabled first for performance testing.
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
            XYZ min,
            List<BoundingBoxXYZ> obstacles,
            XYZ start,
            XYZ end)
        {
            int x = (int)Math.Round((point.X - min.X) / GridSize);
            int y = (int)Math.Round((point.Y - min.Y) / GridSize);
            int z = (int)Math.Round((point.Z - min.Z) / GridSize);

            string id = GetNodeId(x, y, z);

            AStarNode existing;
            if (grid.TryGetValue(id, out existing))
                return existing;

            XYZ center = new XYZ(min.X + x * GridSize, min.Y + y * GridSize, min.Z + z * GridSize);
            AStarNode node = new AStarNode(x, y, z, center, id);
            node.IsObstacle = CheckIfObstacle(center, obstacles, start, end);
            grid[id] = node;
            return node;
        }

        private List<AStarNode> GetNeighbors(
            AStarNode node,
            Dictionary<string, AStarNode> grid,
            XYZ min,
            XYZ max,
            List<BoundingBoxXYZ> obstacles,
            XYZ start,
            XYZ end)
        {
            List<AStarNode> neighbors = new List<AStarNode>();
            int[,] dirs = new int[,]
            {
                { 1, 0, 0 },
                { -1, 0, 0 },
                { 0, 1, 0 },
                { 0, -1, 0 },
                { 0, 0, 1 },
                { 0, 0, -1 }
            };

            for (int i = 0; i < 6; i++)
            {
                int nx = node.X + dirs[i, 0];
                int ny = node.Y + dirs[i, 1];
                int nz = node.Z + dirs[i, 2];

                XYZ center = new XYZ(min.X + nx * GridSize, min.Y + ny * GridSize, min.Z + nz * GridSize);

                if (center.X < min.X || center.X > max.X ||
                    center.Y < min.Y || center.Y > max.Y ||
                    center.Z < min.Z || center.Z > max.Z)
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

        private double GetManhattanDistance(XYZ a, XYZ b)
        {
            return Math.Abs(a.X - b.X) + Math.Abs(a.Y - b.Y) + Math.Abs(a.Z - b.Z);
        }

        private string GetNodeId(int x, int y, int z)
        {
            return x + "," + y + "," + z;
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
