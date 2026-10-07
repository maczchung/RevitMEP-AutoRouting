using System;
using System.IO;
using System.Text.Json;
using MEPAutoRouting.Routing;

namespace MEPAutoRouting.UI.ViewModels
{
    /// <summary>Persisted to %AppData%\MEPAutoRouting\settings.json</summary>
    public class UserSettings
    {
        public Discipline Discipline { get; set; } = Discipline.Pipe;
        public double LeadMm { get; set; } = 150;
        public double MinSegmentMm { get; set; } = 100;
        public bool MatchSize { get; set; } = true;
        public bool AddFittings { get; set; } = true;
        public bool ConnectEnds { get; set; } = true;
        public bool SuppressWarnings { get; set; } = true;
        public bool SelectAfterRoute { get; set; } = true;
        public bool Topmost { get; set; } = false;

        private static string FilePath => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "MEPAutoRouting", "settings.json");

        public static UserSettings Load()
        {
            try
            {
                if (File.Exists(FilePath))
                    return JsonSerializer.Deserialize<UserSettings>(File.ReadAllText(FilePath)) ?? new UserSettings();
            }
            catch { /* corrupt file – use defaults */ }
            return new UserSettings();
        }

        public void Save()
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(FilePath));
                File.WriteAllText(FilePath, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
            }
            catch { /* non-critical */ }
        }
    }
}
