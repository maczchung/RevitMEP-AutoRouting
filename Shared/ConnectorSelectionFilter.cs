using Autodesk.Revit.DB;
using Autodesk.Revit.UI.Selection;
using System.Linq;

namespace MEPAutoRouting.Shared
{
    /// <summary>Only allows elements that have at least one open connector in the required domain.</summary>
    public class ConnectorSelectionFilter : ISelectionFilter
    {
        private readonly Domain? _domain;
        public ConnectorSelectionFilter(Domain? domain) { _domain = domain; }

        public bool AllowElement(Element elem)
            => ConnectorUtils.GetOpenConnectors(elem, _domain).Any();

        public bool AllowReference(Reference reference, XYZ position) => true;
    }
}
