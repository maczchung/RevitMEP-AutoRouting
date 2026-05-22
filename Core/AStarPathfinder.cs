using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;

namespace MEPAutoRouting.Core
{
    public class AStarPathfinder
    {
        private readonly Document _doc;
        // 定義網格大小：1.0 英呎 (約 304.8 mm)，適合 MEP 管線走直角的網格解析度
        private const double GridSize = 1.0; 

        // 建構子必須傳入 Document 才能獲取模型中的障礙物
        public AStarPathfinder(Document doc)
        {
            _doc = doc;
        }

        public List<XYZ> FindPath(XYZ start, XYZ end)
        {
            // 1. 獲取可能成為障礙物的元件 BoundingBox
            GeometryExtractor extractor = new GeometryExtractor(_doc);
            List<BoundingBoxXYZ> obstacles = new List<BoundingBoxXYZ>();
            
            obstacles.AddRange(extractor.GetBoundingBoxes(BuiltInCategory.OST_Walls));
            obstacles.AddRange(extractor.GetBoundingBoxes(BuiltInCategory.OST_StructuralColumns));
            obstacles.AddRange(extractor.GetBoundingBoxes(BuiltInCategory.OST_DuctCurves));
            obstacles.AddRange(extractor.GetBoundingBoxes(BuiltInCategory.OST_PipeCurves));
            obstacles.AddRange(extractor.GetBoundingBoxes(BuiltInCategory.OST_CableTray));

            // 2. 設定搜尋邊界 (避免演算法迷失在無限空間)
            double margin = GridSize * 10; // 起終點外推 10 呎作為極限範圍
            XYZ min = new XYZ(Math.Min(start.X, end.X) - margin, Math.Min(start.Y, end.Y) - margin, Math.Min(start.Z, end.Z) - margin);
            XYZ max = new XYZ(Math.Max(start.X, end.X) + margin, Math.Max(start.Y, end.Y) + margin, Math.Max(start.Z, end.Z) + margin);

            // 3. 初始化稀疏網格 (Sparse Grid) 與節點
            Dictionary<string, AStarNode> grid = new Dictionary<string, AStarNode>();
            AStarNode startNode = GetOrCreateNode(grid, start, min, obstacles);
            AStarNode endNode = GetOrCreateNode(grid, end, min, obstacles);

            // 強制起點與終點不被視為障礙物
            startNode.IsObstacle = false;
            endNode.IsObstacle = false;

            // 4. A* 演算法主迴圈
            List<AStarNode> openSet = new List<AStarNode>();
            HashSet<string> closedSet = new HashSet<string>();
            openSet.Add(startNode);

            while (openSet.Count > 0)
            {
                // 取出 FCost 最小的節點
                AStarNode current = openSet.OrderBy(n => n.FCost).ThenBy(n => n.HCost).First();

                // 抵達終點
                if (current.X == endNode.X && current.Y == endNode.Y && current.Z == endNode.Z)
                {
                    return RetracePath(startNode, current, start, end);
                }

                openSet.Remove(current);
                closedSet.Add(current.Id);

                // 獲取相鄰節點 (上下左右前後 6 個方向)
                foreach (AStarNode neighbor in GetNeighbors(current, grid, min, max, obstacles))
                {
                    if (neighbor.IsObstacle || closedSet.Contains(neighbor.Id)) continue;

                    double newMovementCostToNeighbor = current.GCost + GridSize;
                    if (newMovementCostToNeighbor < neighbor.GCost || !openSet.Contains(neighbor))
                    {
                        neighbor.GCost = newMovementCostToNeighbor;
                        neighbor.HCost = neighbor.Center.DistanceTo(endNode.Center);
                        neighbor.Parent = current;

                        if (!openSet.Contains(neighbor))
                            openSet.Add(neighbor);
                    }
                }
            }

            // 如果找不到路徑 (例如被完全包圍)，退回使用簡單的 L 型生成
            TaskDialog.Show("A* Fallback", "No clear path found around obstacles. Falling back to L-Shape.");
            return MEPAutoRouting.Shared.SimplePath.GenerateLShape(start, end);
        }

        private AStarNode GetOrCreateNode(Dictionary<string, AStarNode> grid, XYZ pt, XYZ min, List<BoundingBoxXYZ> obstacles)
        {
            int x = (int)Math.Round((pt.X - min.X) / GridSize);
            int y = (int)Math.Round((pt.Y - min.Y) / GridSize);
            int z = (int)Math.Round((pt.Z - min.Z) / GridSize);
            string id = $"{x},{y},{z}";

            if (!grid.ContainsKey(id))
            {
                XYZ center = new XYZ(min.X + x * GridSize, min.Y + y * GridSize, min.Z + z * GridSize);
                AStarNode node = new AStarNode(x, y, z, center, id);
                node.IsObstacle = CheckIfObstacle(center, obstacles);
                grid[id] = node;
            }
            return grid[id];
        }

        private List<AStarNode> GetNeighbors(AStarNode node, Dictionary<string, AStarNode> grid, XYZ min, XYZ max, List<BoundingBoxXYZ> obstacles)
        {
            List<AStarNode> neighbors = new List<AStarNode>();
            // 只允許 90 度垂直/水平移動 (符合 MEP 佈管邏輯)
            int[][] dirs = new int[][] {
                new int[]{1,0,0}, new int[]{-1,0,0},
                new int[]{0,1,0}, new int[]{0,-1,0},
                new int[]{0,0,1}, new int[]{0,0,-1}
            };

            foreach (var dir in dirs)
            {
                int nx = node.X + dir[0];
                int ny = node.Y + dir[1];
                int nz = node.Z + dir[2];
                
                XYZ nCenter = new XYZ(min.X + nx * GridSize, min.Y + ny * GridSize, min.Z + nz * GridSize);
                
                // 檢查是否超出邊界
                if (nCenter.X < min.X || nCenter.X > max.X || 
                    nCenter.Y < min.Y || nCenter.Y > max.Y || 
                    nCenter.Z < min.Z || nCenter.Z > max.Z) continue;

                string id = $"{nx},{ny},{nz}";
                if (!grid.ContainsKey(id))
                {
                    AStarNode nNode = new AStarNode(nx, ny, nz, nCenter, id);
                    nNode.IsObstacle = CheckIfObstacle(nCenter, obstacles);
                    grid[id] = nNode;
                }
                neighbors.Add(grid[id]);
            }
            return neighbors;
        }

        private bool CheckIfObstacle(XYZ pt, List<BoundingBoxXYZ> obstacles)
        {
            // 將判斷點擴張半個網格，確保管線與牆壁有安全距離
            double tol = GridSize * 0.5; 
            foreach (var bb in obstacles)
            {
                if (pt.X >= bb.Min.X - tol && pt.X <= bb.Max.X + tol &&
                    pt.Y >= bb.Min.Y - tol && pt.Y <= bb.Max.Y + tol &&
                    pt.Z >= bb.Min.Z - tol && pt.Z <= bb.Max.Z + tol)
                {
                    return true;
                }
            }
            return false;
        }

        private List<XYZ> RetracePath(AStarNode startNode, AStarNode endNode, XYZ realStart, XYZ realEnd)
        {
            List<XYZ> path = new List<XYZ>();
            AStarNode currentNode = endNode;

            while (currentNode != startNode)
            {
                path.Add(currentNode.Center);
                currentNode = currentNode.Parent;
            }
            
            path.Reverse();
            
            // 結合真正的起終點
            List<XYZ> finalPath = new List<XYZ>();
            finalPath.Add(realStart);
            finalPath.AddRange(path);
            finalPath.Add(realEnd);

            return SimplifyPath(finalPath);
        }

        // 將同直線的連續點刪除，保留轉角點，這對於後續生成管件非常重要
        private List<XYZ> SimplifyPath(List<XYZ> path)
        {
            if (path.Count < 3) return path;
            List<XYZ> simplified = new List<XYZ>();
            simplified.Add(path[0]);
            
            for (int i = 1; i < path.Count - 1; i++)
            {
                XYZ prev = simplified.Last();
                XYZ curr = path[i];
                XYZ next = path[i + 1];
                
                XYZ dir1 = (curr - prev).Normalize();
                XYZ dir2 = (next - curr).Normalize();

                // 如果方向改變 (非共線)，則保留該轉角點
                if (dir1.DistanceTo(dir2) > 0.01) 
                {
                    simplified.Add(curr);
                }
            }
            simplified.Add(path.Last());
            return simplified;
        }
    }

    // A* 內部專用的節點類別
    public class AStarNode
    {
        public int X { get; set; }
        public int Y { get; set; }
        public int Z { get; set; }
        public XYZ Center { get; set; }
        public string Id { get; set; }
        public bool IsObstacle { get; set; }

        public double GCost { get; set; }
        public double HCost { get; set; }
        public double FCost => GCost + HCost;

        public AStarNode Parent { get; set; }

        public AStarNode(int x, int y, int z, XYZ center, string id)
        {
            X = x;
            Y = y;
            Z = z;
            Center = center;
            Id = id;
        }
    }
}