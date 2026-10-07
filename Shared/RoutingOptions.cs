using System;
using System.Globalization;
using System.IO;
using System.Text.Json;

namespace MEPAutoRouting
{
    /// <summary>UI 設定（mm），記低喺 %AppData%\MEPAutoRouting\settings.json。</summary>
    public sealed class RoutingOptions
    {
        public double WallClearanceMm { get; set; } = 50;
        public bool IncludeLinkWalls { get; set; } = true;
        public bool ClearanceOnBoundary { get; set; } = true;
        public double RegionMarginMm { get; set; } = 2000;   // v4.6 – margin around the route region (wall collection + one A* retry)
        public double LeadLengthMm { get; set; } = 150;
        public double PipeSizeMm { get; set; } = 0;      // 0 = 未設定
        public SlopeSettings Slope { get; set; } = new();

        public static bool TryParseMm(string text, double min, double max, string label,
                                      out double mm, out string error)
        {
            error = null;
            if (!double.TryParse(text?.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out mm))
            { error = $"{label} must be a number (mm)."; return false; }
            if (mm < min || mm > max)
            { error = $"{label} must be between {min} and {max} mm."; return false; }
            return true;
        }

        private static string FilePath => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "MEPAutoRouting", "settings.json");

        public static RoutingOptions Load()
        {
            try
            {
                if (File.Exists(FilePath))
                    return JsonSerializer.Deserialize<RoutingOptions>(File.ReadAllText(FilePath)) ?? new();
            }
            catch { }
            return new RoutingOptions();
        }

        public void Save()
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
                File.WriteAllText(FilePath, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
            }
            catch { }
        }
    }
}
