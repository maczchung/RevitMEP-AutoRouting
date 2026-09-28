using System;
using System.Globalization;
using System.IO;
using System.Text.Json;

namespace MEPAutoRouting
{
    public enum SpaceSource { None, Host, Link }

    /// <summary>UI 設定（mm）。會記低上次輸入，放喺 %AppData%\MEPAutoRouting\settings.json。</summary>
    public sealed class RoutingOptions
    {
        public double WallClearanceMm { get; set; } = 50;
        public bool IncludeLinkWalls { get; set; } = true;
        public bool ClearanceOnSpaceBoundary { get; set; } = true;
        public SpaceSource SpaceSource { get; set; } = SpaceSource.None;

        public double WallClearanceFt => RoutingConstraints.MmToFeet(WallClearanceMm);

        public static bool TryParseMm(string text, out double mm, out string error)
        {
            error = null;
            if (!double.TryParse(text?.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out mm))
            { error = "牆間距要輸入數字 (mm)。"; return false; }
            if (mm < 0 || mm > 2000)
            { error = "牆間距要喺 0 – 2000 mm 之間。"; return false; }
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
            catch { /* 壞咗就用預設 */ }
            return new RoutingOptions();
        }

        public void Save()
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
                File.WriteAllText(FilePath, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
            }
            catch { /* 寫唔到唔影響 routing */ }
        }
    }
}
