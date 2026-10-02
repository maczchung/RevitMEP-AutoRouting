using System;
using System.Collections.Concurrent;
using Autodesk.Revit.UI;

namespace MEPAutoRouting
{
    /// <summary>
    /// Modeless window → Revit API 嘅橋。Route / Pick / Transaction 全部要經呢度。
    /// ⚠ Create() 一定要喺 IExternalCommand.Execute（API context）入面 call。
    /// </summary>
    public sealed class RevitActionQueue : IExternalEventHandler, IDisposable
    {
        private readonly ConcurrentQueue<(string Name, Action<UIApplication> Action)> _queue = new();
        private ExternalEvent _event;

        public event Action<string> Log;

        private RevitActionQueue() { }

        public static RevitActionQueue Create()
        {
            var q = new RevitActionQueue();
            q._event = ExternalEvent.Create(q);
            return q;
        }

        public bool Enqueue(string name, Action<UIApplication> action)
        {
            _queue.Enqueue((name, action));
            ExternalEventRequest r = _event.Raise();
            if (r == ExternalEventRequest.Accepted || r == ExternalEventRequest.Pending) return true;
            Log?.Invoke($"{name}: ExternalEvent {r} (Revit may be in edit mode or showing a dialog).");
            return false;
        }

        public void Execute(UIApplication app)
        {
            while (_queue.TryDequeue(out var item))
            {
                try { item.Action(app); }
                catch (Autodesk.Revit.Exceptions.OperationCanceledException) { Log?.Invoke($"{item.Name}: cancelled."); }
                catch (Exception ex) { Log?.Invoke($"{item.Name} failed – {ex.GetType().Name}: {ex.Message}"); }
            }
        }

        public string GetName() => "MEP Auto Routing";

        public void Dispose() { _event?.Dispose(); _event = null; }
    }
}
