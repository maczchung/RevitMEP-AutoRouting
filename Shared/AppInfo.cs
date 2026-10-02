using System.Reflection;

namespace MEPAutoRouting
{
    /// <summary>Publisher 資料：About pane、status bar、log 都用呢度。</summary>
    public static class AppInfo
    {
        public const string Publisher = "Cundall HK";
        public const string Author = "Matthew Kwok";
        public const string PublisherDisplay = "Cundall HK · Matthew Kwok";
        public const string ProductName = "MEP Auto Routing";

        public static string Version =>
            Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "1.0.0";
    }
}
