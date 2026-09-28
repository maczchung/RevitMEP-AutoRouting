using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Plumbing;
using MEPAutoRouting.Routing;

namespace MEPAutoRouting.Plumbing
{
    public class PipeSegmentFactory : ISegmentFactory
    {
        private readonly RouteOptions _o;
        public PipeSegmentFactory(RouteOptions o) { _o = o; }

        public MEPCurve Create(Document doc, XYZ start, XYZ end)
            => Pipe.Create(doc, _o.SystemTypeId, _o.TypeId, _o.LevelId, start, end);

        public void ApplySize(MEPCurve curve, ConnectorInfo src)
        {
            if (src.Shape == ConnectorProfileType.Round)
                SegmentFactory.SetParam(curve, BuiltInParameter.RBS_PIPE_DIAMETER_PARAM, src.Radius * 2);
        }
    }
}
