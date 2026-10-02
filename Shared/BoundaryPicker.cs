using System;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Architecture;
using Autodesk.Revit.DB.Mechanical;
using Autodesk.Revit.UI;
using Autodesk.Revit.UI.Selection;

namespace MEPAutoRouting
{
    /// <summary>
    /// 手動 Pick 計算範圍：直接喺 model 揀 Space / Room / Mass，類型自動判斷。
    /// 一定要喺 Revit API context 入面 call（經 RevitActionQueue）。
    /// </summary>
    public static class BoundaryPicker
    {
        public static IRoutingBoundary PickHost(UIDocument uidoc)
        {
            try
            {
                Reference r = uidoc.Selection.PickObject(ObjectType.Element, new HostFilter(),
                    "Pick a Space, Room or Mass (host model). Press Esc to cancel.");
                return Create(uidoc.Document.GetElement(r), null);
            }
            catch (Autodesk.Revit.Exceptions.OperationCanceledException) { return null; }
        }

        public static IRoutingBoundary PickLinked(UIDocument uidoc)
        {
            Document doc = uidoc.Document;
            try
            {
                Reference r = uidoc.Selection.PickObject(ObjectType.LinkedElement, new LinkedFilter(doc),
                    "Pick a Space, Room or Mass in a linked model. Press Esc to cancel.");
                var li = (RevitLinkInstance)doc.GetElement(r.ElementId);
                Document ld = li.GetLinkDocument()
                    ?? throw new InvalidOperationException($"Link '{li.Name}' is not loaded.");
                return Create(ld.GetElement(r.LinkedElementId), li.GetTotalTransform());
            }
            catch (Autodesk.Revit.Exceptions.OperationCanceledException) { return null; }
        }

        public static BoundaryKind Detect(Element e) =>
            e is Space s && s.Area > 0 ? BoundaryKind.Space :
            e is Room r && r.Area > 0 ? BoundaryKind.Room :
            e?.Category?.BuiltInCategory == BuiltInCategory.OST_Mass ? BoundaryKind.Mass :
            BoundaryKind.None;

        public static IRoutingBoundary Create(Element e, Transform linkTransform) => Detect(e) switch
        {
            BoundaryKind.Space or BoundaryKind.Room => SpaceVolume.FromSpatialElement((SpatialElement)e, linkTransform),
            BoundaryKind.Mass => MassVolume.FromElement(e, linkTransform),
            _ => throw new InvalidOperationException(
                     "The picked element is not a placed Space, Room or Mass. Unplaced or unenclosed spaces cannot be used.")
        };

        private sealed class HostFilter : ISelectionFilter
        {
            public bool AllowElement(Element e) => Detect(e) != BoundaryKind.None;
            public bool AllowReference(Reference r, XYZ p) => false;
        }

        private sealed class LinkedFilter : ISelectionFilter
        {
            private readonly Document _host;
            public LinkedFilter(Document host) => _host = host;
            public bool AllowElement(Element e) => e is RevitLinkInstance;
            public bool AllowReference(Reference r, XYZ p)
            {
                if (_host.GetElement(r.ElementId) is not RevitLinkInstance li) return false;
                Document ld = li.GetLinkDocument();
                return ld != null && Detect(ld.GetElement(r.LinkedElementId)) != BoundaryKind.None;
            }
        }
    }
}
