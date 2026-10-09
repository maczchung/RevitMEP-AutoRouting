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
        public double Value { get; set; } = 40;               // default 1:40 – follow project standard
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

        // ------------------------------------------------------------------ v4.8.4 system classification

        private static readonly string[] PressurisedWords =
            { "boosted", "pumped", "pressur", "rising main", "pump", "domestic", "cold water", "hot water", "fire" };

        private static readonly string[] GravityWords =
            { "storm", "rain", "drain", "waste", "soil", "condensate", "sanitary", "foul" };

        private static bool NameHas(string name, string[] words)
        {
            string n = (name ?? "").ToLowerInvariant();
            foreach (string w in words) if (n.Contains(w)) return true;
            return false;
        }

        private static bool IsPressurisedClassification(MEPSystemClassification c) =>
            c == MEPSystemClassification.DomesticColdWater ||
            c == MEPSystemClassification.DomesticHotWater ||
            c == MEPSystemClassification.SupplyHydronic ||
            c == MEPSystemClassification.ReturnHydronic ||
            c == MEPSystemClassification.FireProtectWet ||
            c == MEPSystemClassification.FireProtectDry ||
            c == MEPSystemClassification.FireProtectPreaction ||
            c == MEPSystemClassification.FireProtectOther;

        /// <summary>
        /// v4.8.4 – order of precedence:
        ///  1. Name contains a pressurised word (Boosted, Pumped, Pressur…, Rising main, Pump…) → NOT gravity.
        ///     e.g. "Boosted Rainwater Harvesting" is pumped, not gravity drainage.
        ///  2. SystemClassification = Sanitary → gravity.
        ///  3. Pressurised classification (domestic water, hydronic, fire) → NOT gravity.
        ///  4. Name fallback (OtherPipe etc.): storm / rain / drain / waste / soil / condensate / sanitary / foul.
        /// </summary>
        public static bool IsGravitySystem(PipingSystemType st)
            => Classify(st) == SystemKind.Gravity;

        public static bool IsGravitySystem(PipingSystemType st, out bool isPressurised)
        {
            SystemKind k = Classify(st);
            isPressurised = k == SystemKind.Pressurised;
            return k == SystemKind.Gravity;
        }

        public enum SystemKind { Unknown, Gravity, Pressurised }

        public static SystemKind Classify(PipingSystemType st)
        {
            if (st == null) return SystemKind.Unknown;
            if (NameHas(st.Name, PressurisedWords)) return SystemKind.Pressurised;
            if (st.SystemClassification == MEPSystemClassification.Sanitary) return SystemKind.Gravity;
            if (IsPressurisedClassification(st.SystemClassification)) return SystemKind.Pressurised;
            if (NameHas(st.Name, GravityWords)) return SystemKind.Gravity;
            return SystemKind.Unknown;
        }

        /// <summary>v4.8.4 – "System: {name} – classification {cls} – {gravity|pressurised|unknown}".</summary>
        public static string Describe(PipingSystemType st)
        {
            if (st == null) return "System: none";
            return $"System: {st.Name} – classification {st.SystemClassification} – {Classify(st).ToString().ToLowerInvariant()}";
        }
    }
}
