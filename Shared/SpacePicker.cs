using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Architecture;
using Autodesk.Revit.DB.Mechanical;
using Autodesk.Revit.UI;
using Autodesk.Revit.UI.Selection;

namespace MEPAutoRouting
{
    /// <summary>揀 Space / Room（Host 或 Linked model），轉做 SpaceVolume。</summary>
    public static class SpacePicker
    {
        /// <summary>喺 Host model 揀 Space / Room。Cancel 就返回 null。</summary>
        public static SpaceVolume PickHost(UIDocument uidoc)
        {
            try
            {
                Reference r = uidoc.Selection.PickObject(ObjectType.Element,
                    new HostSpatialFilter(), "揀 Space / Room（Host model）");
                var se = (SpatialElement)uidoc.Document.GetElement(r);
                return SpaceVolume.FromSpatialElement(se);
            }
            catch (Autodesk.Revit.Exceptions.OperationCanceledException) { return null; }
        }

        /// <summary>
        /// 喺 Linked model 揀 Space / Room。Cancel 就返回 null。
        /// 注意：要喺 V/G → Revit Links 將 link 嘅 Spaces/Rooms "Interior" + "Reference" 打開先揀到。
        /// </summary>
        public static SpaceVolume PickLinked(UIDocument uidoc)
        {
            Document doc = uidoc.Document;
            try
            {
                Reference r = uidoc.Selection.PickObject(ObjectType.LinkedElement,
                    new LinkedSpatialFilter(doc), "揀 Linked model 入面嘅 Space / Room");

                var li = (RevitLinkInstance)doc.GetElement(r.ElementId);
                var se = (SpatialElement)li.GetLinkDocument().GetElement(r.LinkedElementId);
                return SpaceVolume.FromSpatialElement(se, li.GetTotalTransform());
            }
            catch (Autodesk.Revit.Exceptions.OperationCanceledException) { return null; }
        }

        /// <summary>
        /// 後備方案：列出所有 loaded link 入面嘅 Space / Room（可以放入 ComboBox）。
        /// 揀唔到（V/G 收埋）嘅時候用。
        /// </summary>
        public static List<LinkedSpaceItem> GetAllLinkedSpaces(Document doc)
        {
            var result = new List<LinkedSpaceItem>();
            foreach (RevitLinkInstance li in new FilteredElementCollector(doc)
                         .OfClass(typeof(RevitLinkInstance)).Cast<RevitLinkInstance>())
            {
                Document ld = li.GetLinkDocument();
                if (ld == null) continue;   // Unloaded

                foreach (SpatialElement se in new FilteredElementCollector(ld)
                             .OfClass(typeof(SpatialElement)).Cast<SpatialElement>())
                {
                    if (!(se is Space || se is Room) || se.Area <= 0) continue;
                    result.Add(new LinkedSpaceItem(li, se));
                }
            }
            return result.OrderBy(x => x.Display).ToList();
        }

        private sealed class HostSpatialFilter : ISelectionFilter
        {
            public bool AllowElement(Element e) => e is SpatialElement se && (se is Space || se is Room) && se.Area > 0;
            public bool AllowReference(Reference r, XYZ p) => false;
        }

        private sealed class LinkedSpatialFilter : ISelectionFilter
        {
            private readonly Document _host;
            public LinkedSpatialFilter(Document host) => _host = host;

            public bool AllowElement(Element e) => e is RevitLinkInstance;

            public bool AllowReference(Reference r, XYZ p)
            {
                if (_host.GetElement(r.ElementId) is not RevitLinkInstance li) return false;
                Document ld = li.GetLinkDocument();
                if (ld == null) return false;
                return ld.GetElement(r.LinkedElementId) is SpatialElement se
                       && (se is Space || se is Room) && se.Area > 0;
            }
        }
    }

    public sealed class LinkedSpaceItem
    {
        public RevitLinkInstance Link { get; }
        public SpatialElement Element { get; }
        public string Display { get; }

        public LinkedSpaceItem(RevitLinkInstance link, SpatialElement se)
        {
            Link = link; Element = se;
            string kind = se is Space ? "Space" : "Room";
            Display = $"[{link.Name.Split(':')[0].Trim()}] {kind} {se.Number} - {se.Name} ({se.Level?.Name})";
        }

        public SpaceVolume ToVolume() => SpaceVolume.FromSpatialElement(Element, Link.GetTotalTransform());
        public override string ToString() => Display;
    }
}
