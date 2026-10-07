// =====================================================================================
//  MainViewModel.Route.cs  (v4.2)
//  Route / Preview 統一經 RevitActionQueue 行 RouteService，每一步都寫 OUTPUT。
//  v4.2：Revit thread → UI 一律用 PostToUi（BeginInvoke），唔再同步 Invoke。
// =====================================================================================
using System;
using System.Collections.Generic;
using System.Linq;
using MEPAutoRouting.Routing;

namespace MEPAutoRouting.UI.ViewModels
{
    public partial class MainViewModel
    {
        private partial RouteRequest BuildRouteRequest(bool commit, IRoutingBoundary boundary,
                                                       PipeSizeInfo? size, List<RouteProblem> preChecks);

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
                RevitActionQueue.WriteErrorFile($"{job}: build request failed – {ex.Message}", ex);
                Log(Routing.LogLevel.Error, $"{job}: could not build the request – {ex.GetType().Name}: {ex.Message}");
                return;
            }

            IsBusy = true;
            PreviewPoints.Clear();   // v4.6 – clear any stale Path Preview at the start of every run
            Log(Routing.LogLevel.Info, $"{job} queued…");

            bool queued = ActionQueue.Enqueue(job, app =>
            {
                PostToUi(() => Log(Routing.LogLevel.Info, $"{job}: Revit handler started."), job);
                RouteResult r = null;
                try
                {
                    if (app.ActiveUIDocument == null)
                        throw new InvalidOperationException("No active Revit document.");
                    r = RouteService.Run(app.ActiveUIDocument, req,
                        m => PostToUi(() => Log(Routing.LogLevel.Info, m), job));
                }
                catch (Exception ex)
                {
                    RevitActionQueue.WriteErrorFile($"{job}: RouteService crashed – {ex.Message}", ex);
                    PostToUi(() => Log(Routing.LogLevel.Error,
                        $"{job}: RouteService crashed – {ex.GetType().Name}: {ex.Message}"), job);
                }
                finally
                {
                    RouteResult result = r;
                    PostToUi(() =>
                    {
                        IsBusy = false;
                        if (result != null) ApplyRouteResult(result);
                        else PreviewPoints.Clear();   // v4.6 – on failure the Path Preview stays empty
                        LogOutcome(job, commit, result);
                    }, job);
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
    }
}
