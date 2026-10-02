using System.Globalization;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Plumbing;

namespace MEPAutoRouting
{
    public enum SlopeUnit { Ratio, Percent }                  // Ratio = 1:N
    public enum FlowDirection { SourceToTarget, TargetToSource }

    public sealed class SlopeSettings
    {
        public bool Enabled { get; set; } = false;
        public double Value { get; set; } = 40;               // 1:40 預設，請按項目規範
        public SlopeUnit Unit { get; set; } = SlopeUnit.Ratio;
        public FlowDirection Flow { get; set; } = FlowDirection.SourceToTarget;
        public double MinVerticalMm { get; set; } = 50;

        public double Gradient => Unit == SlopeUnit.Percent ? Value / 100.0 : (Value > 0 ? 1.0 / Value : 0);

        public string Display => !Enabled ? "None"
            : Unit == SlopeUnit.Percent ? $"{Value:0.##} %  (1:{(Gradient > 0 ? 1 / Gradient : 0):0.#})"
            : $"1:{Value:0.#}  ({Gradient * 100:0.##} %)";

        public static bool TryParse(string text, SlopeUnit unit, out double value, out string error)
        {
            error = null;
            if (!double.TryParse(text?.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out value))
            { error = "Slope must be a number."; return false; }
            if (unit == SlopeUnit.Percent && (value <= 0 || value > 20))
            { error = "Slope in % must be between 0 and 20."; return false; }
            if (unit == SlopeUnit.Ratio && (value < 5 || value > 1000))
            { error = "Slope 1:N – N must be between 5 and 1000."; return false; }
            return true;
        }

        public static bool IsGravitySystem(PipingSystemType st)
        {
            if (st == null) return false;
            if (st.SystemClassification == MEPSystemClassification.Sanitary) return true;
            string n = (st.Name ?? "").ToLowerInvariant();
            return n.Contains("storm") || n.Contains("rain") || n.Contains("drain")
                || n.Contains("waste") || n.Contains("soil") || n.Contains("condensate");
        }
    }
}
