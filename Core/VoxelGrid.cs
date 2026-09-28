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
