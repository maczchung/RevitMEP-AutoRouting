using System;
using System.Collections.Concurrent;
<<<<<<< HEAD
using System.IO;
=======
>>>>>>> 6dd4d98ac757c36819d8fe104c6a91a62610d7a2
using Autodesk.Revit.UI;

namespace MEPAutoRouting
{
    /// <summary>
    /// Modeless window → Revit API 嘅橋。Route / Pick / Transaction 全部要經呢度。
    /// ⚠ Create() 一定要喺 IExternalCommand.Execute（API context）入面 call。
<<<<<<< HEAD
    /// v4.2：任何 exception（包括寫 log 本身）都唔會走出 Execute，唔會再令 Revit crash；
    ///       失敗會寫入 %AppData%\MEPAutoRouting\error.log（有完整 stack trace）。
=======
>>>>>>> 6dd4d98ac757c36819d8fe104c6a91a62610d7a2
    /// </summary>
    public sealed class RevitActionQueue : IExternalEventHandler, IDisposable
    {
        private readonly ConcurrentQueue<(string Name, Action<UIApplication> Action)> _queue = new();
        private ExternalEvent _event;

<<<<<<< HEAD
        /// <summary>Log 訂閱者：請用 Dispatcher.BeginInvoke 更新 UI，唔好同步改 ObservableCollection。</summary>
        public event Action<string> Log;

        public static string ErrorLogPath => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "MEPAutoRouting", "error.log");

=======
        public event Action<string> Log;

>>>>>>> 6dd4d98ac757c36819d8fe104c6a91a62610d7a2
        private RevitActionQueue() { }

        public static RevitActionQueue Create()
        {
            var q = new RevitActionQueue();
            q._event = ExternalEvent.Create(q);
            return q;
        }

        public bool Enqueue(string name, Action<UIApplication> action)
        {
<<<<<<< HEAD
            if (_event == null) { SafeLog($"{name}: queue has been disposed."); return false; }
            _queue.Enqueue((name, action));
            ExternalEventRequest r = _event.Raise();
            if (r == ExternalEventRequest.Accepted || r == ExternalEventRequest.Pending) return true;
            SafeLog($"{name}: ExternalEvent {r} (Revit may be in edit mode or showing a dialog).");
=======
            _queue.Enqueue((name, action));
            ExternalEventRequest r = _event.Raise();
            if (r == ExternalEventRequest.Accepted || r == ExternalEventRequest.Pending) return true;
            Log?.Invoke($"{name}: ExternalEvent {r} (Revit may be in edit mode or showing a dialog).");
>>>>>>> 6dd4d98ac757c36819d8fe104c6a91a62610d7a2
            return false;
        }

        public void Execute(UIApplication app)
        {
            while (_queue.TryDequeue(out var item))
            {
                try { item.Action(app); }
<<<<<<< HEAD
                catch (Autodesk.Revit.Exceptions.OperationCanceledException) { SafeLog($"{item.Name}: cancelled."); }
                catch (Exception ex) { SafeLog($"{item.Name} failed – {ex.GetType().Name}: {ex.Message}", ex); }
            }
        }

        /// <summary>寫檔 + 通知 UI；兩者失敗都唔會 throw。</summary>
        public void SafeLog(string msg, Exception ex = null)
        {
            WriteErrorFile(msg, ex);
            try { Log?.Invoke(msg); }
            catch (Exception uiEx) { WriteErrorFile("UI log handler failed – " + uiEx.Message, uiEx); }
        }

        public static void WriteErrorFile(string msg, Exception ex = null)
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(ErrorLogPath)!);
                File.AppendAllText(ErrorLogPath,
                    $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} {msg}{Environment.NewLine}" +
                    (ex != null ? ex + Environment.NewLine : "") +
                    Environment.NewLine);
            }
            catch { /* 寫唔到檔都唔可以 throw */ }
        }

=======
                catch (Autodesk.Revit.Exceptions.OperationCanceledException) { Log?.Invoke($"{item.Name}: cancelled."); }
                catch (Exception ex) { Log?.Invoke($"{item.Name} failed – {ex.GetType().Name}: {ex.Message}"); }
            }
        }

>>>>>>> 6dd4d98ac757c36819d8fe104c6a91a62610d7a2
        public string GetName() => "MEP Auto Routing";

        public void Dispose() { _event?.Dispose(); _event = null; }
    }
}
