using System;
using Autodesk.Revit.DB;
using MEPAutoRouting;

namespace MEPAutoRouting.Core
{
    public class VoxelGrid
    {
        private readonly XYZ _min;
        private readonly double _cellSize;
        public int NX { get; private set; }
        public int NY { get; private set; }
        public int NZ { get; private set; }
        public bool[,,] Blocked { get; private set; }

        public VoxelGrid(XYZ min, XYZ max, double cellSize)
        {
            _min = min;
            _cellSize = cellSize;
            NX = Math.Max(1, (int)Math.Ceiling((max.X - min.X) / cellSize));
            NY = Math.Max(1, (int)Math.Ceiling((max.Y - min.Y) / cellSize));
            NZ = Math.Max(1, (int)Math.Ceiling((max.Z - min.Z) / cellSize));
            Blocked = new bool[NX, NY, NZ];
        }

        public XYZ CellToWorld(int ix, int iy, int iz)
        {
            return new XYZ(_min.X + (ix + 0.5) * _cellSize, _min.Y + (iy + 0.5) * _cellSize, _min.Z + (iz + 0.5) * _cellSize);
        }

        public int ApplyConstraints(RoutingConstraints constraints)
        {
            if (constraints == null) return 0;
            int blocked = 0;
            for (int ix = 0; ix < NX; ix++)
            for (int iy = 0; iy < NY; iy++)
            for (int iz = 0; iz < NZ; iz++)
            {
                if (Blocked[ix, iy, iz]) continue;
                if (constraints.IsBlocked(CellToWorld(ix, iy, iz)))
                {
                    Blocked[ix, iy, iz] = true;
                    blocked++;
                }
            }
            return blocked;
        }

        public void BlockSolid(Solid solid)
        {
            if (solid == null) return;
            BoundingBoxXYZ bounds = solid.GetBoundingBox();
            if (bounds == null) return;
            BlockSolid(bounds);
        }

        public void BlockSolid(BoundingBoxXYZ bounds)
        {
            if (bounds == null) return;
            GetIndexRange(bounds.Min, bounds.Max, out int minX, out int maxX, out int minY, out int maxY, out int minZ, out int maxZ);
            for (int ix = minX; ix <= maxX; ix++)
            for (int iy = minY; iy <= maxY; iy++)
            for (int iz = minZ; iz <= maxZ; iz++)
                Blocked[ix, iy, iz] = true;
        }

        public void UnblockCorridor(XYZ start, XYZ end, double radius)
        {
            if (start == null || end == null) return;
            XYZ min = new XYZ(Math.Min(start.X, end.X) - radius, Math.Min(start.Y, end.Y) - radius, Math.Min(start.Z, end.Z) - radius);
            XYZ max = new XYZ(Math.Max(start.X, end.X) + radius, Math.Max(start.Y, end.Y) + radius, Math.Max(start.Z, end.Z) + radius);
            GetIndexRange(min, max, out int minX, out int maxX, out int minY, out int maxY, out int minZ, out int maxZ);
            for (int ix = minX; ix <= maxX; ix++)
            for (int iy = minY; iy <= maxY; iy++)
            for (int iz = minZ; iz <= maxZ; iz++)
            {
                XYZ p = CellToWorld(ix, iy, iz);
                if (DistanceToSegment(p, start, end) <= radius) Blocked[ix, iy, iz] = false;
            }
        }

        public bool IsBlockedWorld(XYZ point)
        {
            if (point == null) return true;
            int ix = (int)Math.Floor((point.X - _min.X) / _cellSize);
            int iy = (int)Math.Floor((point.Y - _min.Y) / _cellSize);
            int iz = (int)Math.Floor((point.Z - _min.Z) / _cellSize);
            return ix < 0 || ix >= NX || iy < 0 || iy >= NY || iz < 0 || iz >= NZ || Blocked[ix, iy, iz];
        }

        private void GetIndexRange(XYZ min, XYZ max, out int minX, out int maxX, out int minY, out int maxY, out int minZ, out int maxZ)
        {
            minX = Math.Max(0, (int)Math.Floor((min.X - _min.X) / _cellSize));
            maxX = Math.Min(NX - 1, (int)Math.Floor((max.X - _min.X) / _cellSize));
            minY = Math.Max(0, (int)Math.Floor((min.Y - _min.Y) / _cellSize));
            maxY = Math.Min(NY - 1, (int)Math.Floor((max.Y - _min.Y) / _cellSize));
            minZ = Math.Max(0, (int)Math.Floor((min.Z - _min.Z) / _cellSize));
            maxZ = Math.Min(NZ - 1, (int)Math.Floor((max.Z - _min.Z) / _cellSize));
        }

        private static double DistanceToSegment(XYZ point, XYZ start, XYZ end)
        {
            XYZ delta = end - start;
            double lengthSquared = delta.DotProduct(delta);
            double t = lengthSquared < 1e-12 ? 0 : (point - start).DotProduct(delta) / lengthSquared;
            t = Math.Max(0, Math.Min(1, t));
            return point.DistanceTo(start + delta * t);
        }
    }

    public class Voxel
    {
        public int X { get; private set; }
        public int Y { get; private set; }
        public int Z { get; private set; }
        public XYZ Center { get; private set; }
        public bool IsObstacle { get; set; }

        public Voxel(int x, int y, int z, XYZ center)
        {
            X = x;
            Y = y;
            Z = z;
            Center = center;
            IsObstacle = false;
        }
    }
}
