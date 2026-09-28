using System;
using System.Windows.Interop;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using MEPAutoRouting.Routing;
using MEPAutoRouting.UI;
using MEPAutoRouting.UI.ViewModels;

namespace MEPAutoRouting.Core
{
    /// <summary>Opens the modeless Auto Routing window (single instance).</summary>
    [Transaction(TransactionMode.Manual)]
    public class AutoRoutingCommand : IExternalCommand
    {
        private static MainWindow _window;

        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            try
            {
                if (_window != null)
                {
                    if (_window.WindowState == System.Windows.WindowState.Minimized)
                        _window.WindowState = System.Windows.WindowState.Normal;
                    _window.Activate();
                    return Result.Succeeded;
                }

                var uiapp = commandData.Application;
                var handler = new RoutingEventHandler();
                var exEvent = ExternalEvent.Create(handler);
                var vm = new MainViewModel(handler, exEvent,
                                           uiapp.ActiveUIDocument?.Document?.Title ?? "-",
                                           uiapp.Application.VersionNumber);
                handler.ViewModel = vm;

                _window = new MainWindow(vm);
                new WindowInteropHelper(_window) { Owner = uiapp.MainWindowHandle };
                _window.Closed += (s, e) =>
                {
                    vm.SaveSettings();
                    exEvent.Dispose();
                    _window = null;
                };
                _window.Show();

                vm.RequestLoadTypes();
                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                message = ex.Message;
                return Result.Failed;
            }
        }

        public static void CloseWindow() => _window?.Close();
    }
}
