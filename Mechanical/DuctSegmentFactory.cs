using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Mechanical;
using MEPAutoRouting.Routing;

namespace MEPAutoRouting.Mechanical
{
    public class DuctSegmentFactory : ISegmentFactory
    {
        private readonly RouteOptions _o;
        public DuctSegmentFactory(RouteOptions o) { _o = o; }

        public MEPCurve Create(Document doc, XYZ start, XYZ end)
            => Duct.Create(doc, _o.SystemTypeId, _o.TypeId, _o.LevelId, start, end);

        public void ApplySize(MEPCurve curve, ConnectorInfo src)
        {
            if (src.Shape == ConnectorProfileType.Round)
            {
                SegmentFactory.SetParam(curve, BuiltInParameter.RBS_CURVE_DIAMETER_PARAM, src.Radius * 2);
            }
            else
            {
                SegmentFactory.SetParam(curve, BuiltInParameter.RBS_CURVE_WIDTH_PARAM, src.Width);
                SegmentFactory.SetParam(curve, BuiltInParameter.RBS_CURVE_HEIGHT_PARAM, src.Height);
            }
        }
    }
}
