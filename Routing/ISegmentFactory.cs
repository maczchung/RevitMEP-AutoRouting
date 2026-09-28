using Autodesk.Revit.DB;

namespace MEPAutoRouting.Routing
{
    public interface ISegmentFactory
    {
        MEPCurve Create(Document doc, XYZ start, XYZ end);
        void ApplySize(MEPCurve curve, ConnectorInfo source);
    }

    public static class SegmentFactory
    {
        public static ISegmentFactory For(RouteOptions o) => o.Discipline switch
        {
            Discipline.Pipe => new Plumbing.PipeSegmentFactory(o),
            Discipline.Duct => new Mechanical.DuctSegmentFactory(o),
            Discipline.Conduit => new Electrical.ConduitSegmentFactory(o),
            Discipline.CableTray => new Electrical.CableTraySegmentFactory(o),
            _ => throw new System.NotSupportedException(o.Discipline.ToString())
        };

        internal static void SetParam(Element e, BuiltInParameter bip, double value)
        {
            var p = e.get_Parameter(bip);
            if (p != null && !p.IsReadOnly && value > 0) p.Set(value);
        }
    }
}
