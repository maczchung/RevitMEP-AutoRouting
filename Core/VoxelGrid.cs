using Autodesk.Revit.DB;

namespace MEPAutoRouting.Core
{
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
