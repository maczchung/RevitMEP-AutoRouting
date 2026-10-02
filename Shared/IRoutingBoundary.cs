using Autodesk.Revit.DB;

namespace MEPAutoRouting
{
    /// <summary>計算範圍（Space / Room / Mass，Host 或 Linked）。全部係 world 座標、feet。</summary>
    public interface IRoutingBoundary
    {
        string Name { get; }
        string Kind { get; }          // "Space" / "Room" / "Mass"
        bool IsFromLink { get; }
        XYZ Min { get; }
        XYZ Max { get; }

        bool Contains(XYZ p, double insetXY = 0, double insetZ = 0);
    }

    public enum BoundaryKind { None, Space, Room, Mass }
}
