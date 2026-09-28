using Autodesk.Revit.DB;

namespace MEPAutoRouting.Shared
{
    public static class UnitConv
    {
        public static double MmToFt(double mm) => UnitUtils.ConvertToInternalUnits(mm, UnitTypeId.Millimeters);
        public static double FtToMm(double ft) => UnitUtils.ConvertFromInternalUnits(ft, UnitTypeId.Millimeters);
        public static string Mm(double ft) => FtToMm(ft).ToString("0");
    }
}
