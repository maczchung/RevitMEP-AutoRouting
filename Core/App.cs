using System;
using System.Reflection;
using System.Windows.Media.Imaging;
using Autodesk.Revit.UI;

namespace MEPAutoRouting.Core
{
    public class App : IExternalApplication
    {
        public const string TabName = "MEP Tools";
        public const string PanelName = "Auto Routing";

        public Result OnStartup(UIControlledApplication app)
        {
            try { app.CreateRibbonTab(TabName); } catch { /* tab already exists (shared with other add-ins) */ }

            RibbonPanel panel = app.CreateRibbonPanel(TabName, PanelName);
            string asm = Assembly.GetExecutingAssembly().Location;

            var data = new PushButtonData("MEPAutoRouting_Open", "Auto\nRouting", asm,
                                          typeof(AutoRoutingCommand).FullName)
            {
                ToolTip = "Route Pipe / Duct / Conduit / Cable Tray between two connectors.",
                LongDescription = "Pick a source and target connector, preview the orthogonal path, then create segments and elbows.",
                LargeImage = LoadImage("route_32.png"),
                Image = LoadImage("route_16.png")
            };
            panel.AddItem(data);
            return Result.Succeeded;
        }

        public Result OnShutdown(UIControlledApplication app)
        {
            AutoRoutingCommand.CloseWindow();
            return Result.Succeeded;
        }

        private static BitmapImage LoadImage(string name)
        {
            try
            {
                return new BitmapImage(new Uri($"pack://application:,,,/MEPAutoRouting;component/Resources/{name}"));
            }
            catch { return null; }
        }
    }
}
