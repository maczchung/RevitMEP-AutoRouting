using Autodesk.Revit.DB;
using Autodesk.Revit.UI.Selection;
using System.Linq;

namespace MEPAutoRouting.Shared
{
    /// <summary>
    /// v4.8.4 – allows elements that have at least one End connector in the required domain,
    /// open OR connected. The pick handler then reports clearly when the picked connector is
    /// already connected, instead of the element being unselectable or another connector being used.
    /// </summary>
    public class ConnectorSelectionFilter : ISelectionFilter
    {
        private readonly Domain? _domain;
        public ConnectorSelectionFilter(Domain? domain) { _domain = domain; }

        public bool AllowElement(Element elem)
            => ConnectorUtils.GetEndConnectors(elem, _domain).Any();

        public bool AllowReference(Reference reference, XYZ position) => true;
    }
}
