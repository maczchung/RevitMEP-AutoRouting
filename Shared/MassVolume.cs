using System;
using Autodesk.Revit.DB;

namespace MEPAutoRouting
{
    /// <summary>Mass（Conceptual / In-place）做計算範圍，Host / Linked 都得。</summary>
    public sealed class MassVolume : IRoutingBoundary
    {
        private readonly SolidVolume _solid;

        public string Name { get; }
        public string Kind => "Mass";
        public bool IsFromLink { get; }
        public XYZ Min => _solid.Min;
        public XYZ Max => _solid.Max;

        private MassVolume(SolidVolume solid, string name, bool fromLink)
        {
            _solid = solid; Name = name; IsFromLink = fromLink;
        }

        public static MassVolume FromElement(Element e, Transform linkTransform = null)
        {
            if (e == null) throw new ArgumentNullException(nameof(e));
            SolidVolume sv = SolidVolume.FromElement(e, linkTransform)
                ?? throw new InvalidOperationException($"Mass '{e.Name}' has no solid geometry.");
            return new MassVolume(sv, $"{e.Name} (Id {e.Id.Value})", linkTransform != null);
        }

        public bool Contains(XYZ p, double insetXY = 0, double insetZ = 0) => _solid.Contains(p, insetXY, insetZ);
    }
}
