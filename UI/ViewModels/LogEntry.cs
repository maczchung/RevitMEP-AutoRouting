using System;
using MEPAutoRouting.Routing;

namespace MEPAutoRouting.UI.ViewModels
{
    public class LogEntry
    {
        public DateTime Time { get; } = DateTime.Now;
        public LogLevel Level { get; set; }
        public string Message { get; set; }

        public string TimeText => Time.ToString("yyyy-MM-dd HH:mm:ss.fff");
        public string LevelText => Level switch
        {
            LogLevel.Success => "[done]",
            LogLevel.Warn => "[warn]",
            LogLevel.Error => "[error]",
            _ => "[info]"
        };
        public override string ToString() => $"{TimeText} {LevelText} {Message}";
    }

    public class PathPointItem
    {
        public int Index { get; set; }
        public string X { get; set; }
        public string Y { get; set; }
        public string Z { get; set; }
        public string Segment { get; set; }
        public string Direction { get; set; }
    }

    public class Option<T>
    {
        public T Value { get; set; }
        public string Label { get; set; }
        public string Description { get; set; }
        public override string ToString() => Label;
    }
}
