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
                var queue = RevitActionQueue.Create();
                var vm = new MainViewModel(handler, queue,
                                           uiapp.ActiveUIDocument?.Document?.Title ?? "-",
                                           uiapp.Application.VersionNumber);
                handler.ViewModel = vm;
<<<<<<< HEAD
=======
                queue.Log += text => vm.Log(LogLevel.Error, text);

>>>>>>> 6dd4d98ac757c36819d8fe104c6a91a62610d7a2
                _window = new MainWindow(vm);
                Action<string> onLog = text =>
                {
                    try
                    {
                        _window.Dispatcher.BeginInvoke(new Action(() =>
                        {
                            try { vm.Log(LogLevel.Error, text); }
                            catch (Exception ex) { RevitActionQueue.WriteErrorFile("VM log handler failed – " + ex.Message, ex); }
                        }));
                    }
                    catch (Exception ex) { RevitActionQueue.WriteErrorFile("Could not post queue log to UI – " + ex.Message, ex); }
                };
                queue.Log += onLog;
                new WindowInteropHelper(_window) { Owner = uiapp.MainWindowHandle };
                _window.Closed += (s, e) =>
                {
                    queue.Log -= onLog;
                    vm.SaveSettings();
                    queue.Dispose();
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
