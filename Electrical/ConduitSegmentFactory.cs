using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Electrical;
using MEPAutoRouting.Routing;

namespace MEPAutoRouting.Electrical
{
    public class ConduitSegmentFactory : ISegmentFactory
    {
        private readonly RouteOptions _o;
        public ConduitSegmentFactory(RouteOptions o) { _o = o; }

        public MEPCurve Create(Document doc, XYZ start, XYZ end)
            => Conduit.Create(doc, _o.TypeId, start, end, _o.LevelId);

        public void ApplySize(MEPCurve curve, ConnectorInfo src)
        {
            if (src.Shape == ConnectorProfileType.Round)
                SegmentFactory.SetParam(curve, BuiltInParameter.RBS_CONDUIT_DIAMETER_PARAM, src.Radius * 2);
        }
    }

    public class CableTraySegmentFactory : ISegmentFactory
    {
        private readonly RouteOptions _o;
        public CableTraySegmentFactory(RouteOptions o) { _o = o; }

        public MEPCurve Create(Document doc, XYZ start, XYZ end)
            => CableTray.Create(doc, _o.TypeId, start, end, _o.LevelId);

        public void ApplySize(MEPCurve curve, ConnectorInfo src)
        {
            if (src.Shape == ConnectorProfileType.Rectangular)
            {
                SegmentFactory.SetParam(curve, BuiltInParameter.RBS_CABLETRAY_WIDTH_PARAM, src.Width);
                SegmentFactory.SetParam(curve, BuiltInParameter.RBS_CABLETRAY_HEIGHT_PARAM, src.Height);
            }
        }
    }
}
