// =====================================================================================
//  MainViewModel.Route.cs  (v4.1)
//  Route / Preview 統一經 RevitActionQueue 行 RouteService，每一步都寫 OUTPUT。
//  依賴：MainViewModel.Constraints.cs
// =====================================================================================
using System;
using System.Collections.Generic;
using System.Linq;
using MEPAutoRouting.Routing;

namespace MEPAutoRouting.UI.ViewModels
{
    public partial class MainViewModel
    {
        /// <summary>用 UI 現有選擇建 RouteRequest。</summary>
        private partial RouteRequest BuildRouteRequest(bool commit, IRoutingBoundary boundary,
                                                       PipeSizeInfo? size, List<RouteProblem> preChecks);

        /// <summary>將結果寫返 UI：PreviewPoints、Problems、Summary。</summary>
        private partial void ApplyRouteResult(RouteResult result);

        private void ExecuteRoute(bool commit)
        {
            string job = commit ? "Route" : "Preview";

            if (ActionQueue == null)
            {
                Log(Routing.LogLevel.Error, $"{job}: Revit action queue is not initialised (ActionQueue is null).");
                return;
            }

            RouteRequest req;
            try
            {
                var (boundary, size, pre) = GetConstraintInputs();
                req = BuildRouteRequest(commit, boundary, size, pre);
            }
            catch (Exception ex)
            {
                Log(Routing.LogLevel.Error, $"{job}: could not build the request – {ex.GetType().Name}: {ex.Message}");
                return;
            }

            IsBusy = true;
            Log(Routing.LogLevel.Info, $"{job} queued…");

            bool queued = ActionQueue.Enqueue(job, app =>
            {
                UI(() => Log(Routing.LogLevel.Info, $"{job}: Revit handler started."));
                RouteResult r = null;
                try
                {
                    if (app.ActiveUIDocument == null)
                        throw new InvalidOperationException("No active Revit document.");
                    r = RouteService.Run(app.ActiveUIDocument, req, m => UI(() => Log(Routing.LogLevel.Info, m)));
                }
                catch (Exception ex)
                {
                    UI(() => Log(Routing.LogLevel.Error, $"{job}: RouteService crashed – {ex.GetType().Name}: {ex.Message}"));
                }
                finally
                {
                    UI(() =>
                    {
                        IsBusy = false;
                        if (r != null)
                        {
                            try { ApplyRouteResult(r); }
                            catch (Exception ex) { Log(Routing.LogLevel.Error, $"{job}: could not update the UI – {ex.Message}"); }
                        }
                        LogOutcome(job, commit, r);
                    });
                }
            });

            if (!queued)
            {
                IsBusy = false;
                Log(Routing.LogLevel.Error, $"{job}: could not queue the job – Revit is busy (edit mode or a dialog is open).");
            }
        }

        private void LogOutcome(string job, bool commit, RouteResult r)
        {
            if (r == null) { Log(Routing.LogLevel.Error, $"{job} aborted – see the error above."); return; }

            int errors = r.Problems.Count(p => p.IsError);
            int warnings = r.Problems.Count - errors;

            if (r.Committed)
                Log(Routing.LogLevel.Success,
                    $"Created {r.CreatedSegments} segments and {r.CreatedElbows} elbows ({warnings} warning(s)).");
            else if (commit)
                Log(Routing.LogLevel.Warn,
                    $"Nothing was created – {errors} error(s), {warnings} warning(s). See PROBLEMS.");
            else
                Log(errors > 0 ? Routing.LogLevel.Warn : Routing.LogLevel.Success,
                    $"Preview ready – {r.Path?.Count ?? 0} points, {errors} error(s), {warnings} warning(s).");

            if (errors > 0) SelectedPanelTab = 0;
        }

        private void UI(Action a) => _uiDispatcher.Invoke(a);
    }
}
