using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Electrical;
using Autodesk.Revit.DB.Mechanical;
using Autodesk.Revit.DB.Plumbing;
using Autodesk.Revit.UI;
using Autodesk.Revit.UI.Selection;
using MEPAutoRouting.Shared;
using MEPAutoRouting.UI.ViewModels;

namespace MEPAutoRouting.Routing
{
    public enum RoutingRequest { None, PickSource, PickTarget, LoadTypes, Preview, Route }

    /// <summary>
    /// All Revit API work from the modeless window goes through here (valid API context).
    /// </summary>
    public class RoutingEventHandler : IExternalEventHandler
    {
        public RoutingRequest Request { get; set; }
        public MainViewModel ViewModel { get; set; }

        public string GetName() => "MEP Auto Routing";

        public void Execute(UIApplication app)
        {
            var vm = ViewModel;
            var req = Request;
            Request = RoutingRequest.None;
            if (vm == null) return;

            UIDocument uidoc = app.ActiveUIDocument;
            if (uidoc == null) { vm.Log(LogLevel.Error, "No active document."); return; }
            Document doc = uidoc.Document;
            vm.DocumentTitle = doc.Title;

            vm.IsBusy = true;
            try
            {
                switch (req)
                {
                    case RoutingRequest.PickSource: Pick(uidoc, vm, true); break;
                    case RoutingRequest.PickTarget: Pick(uidoc, vm, false); break;
                    case RoutingRequest.LoadTypes: LoadTypes(doc, vm); break;
                    case RoutingRequest.Preview: Preview(uidoc, vm); break;
                    case RoutingRequest.Route: Route(uidoc, vm); break;
                }
            }
            catch (Autodesk.Revit.Exceptions.OperationCanceledException)
            {
                vm.Log(LogLevel.Info, "Cancelled by user.");
            }
            catch (Exception ex)
            {
                vm.Log(LogLevel.Error, ex.Message);
            }
            finally
            {
                vm.IsBusy = false;
            }
        }

        // ------------------------------------------------------------------ pick
        private static void Pick(UIDocument uidoc, MainViewModel vm, bool isSource)
        {
            Domain domain = vm.SelectedDiscipline.ToDomain();
            string label = isSource ? "SOURCE" : "TARGET";
            vm.Status = $"Pick {label} element in Revit… (Esc to cancel)";

            Reference r = uidoc.Selection.PickObject(ObjectType.Element,
                new ConnectorSelectionFilter(domain),
                $"MEP Auto Routing: pick {label} ({ConnectorUtils.DomainLabel(domain)} connector)");

            Element e = uidoc.Document.GetElement(r);
            Connector c = ConnectorUtils.GetNearestOpenConnector(e, r.GlobalPoint, domain);
            if (c == null) { vm.Log(LogLevel.Error, "No open connector found on picked element."); return; }

            var info = ConnectorInfo.From(c, e);
            if (isSource) vm.Source = info; else vm.Target = info;
            vm.Log(LogLevel.Info, $"{label}: {info.Title} [{info.IdText}] {info.DetailText} @ {info.OriginText}");

            if (isSource && vm.SelectedLevel == null)
                vm.SelectLevelById(e.LevelId);
        }

        // ------------------------------------------------------------------ types
        private static void LoadTypes(Document doc, MainViewModel vm)
        {
            List<TypeItem> Types<T>() where T : Element =>
                new FilteredElementCollector(doc).OfClass(typeof(T))
                    .Select(x => new TypeItem { Id = x.Id, Name = x.Name, Element = x })
                    .OrderBy(x => x.Name).ToList();

            List<TypeItem> types, systems = new List<TypeItem>();
            switch (vm.SelectedDiscipline)
            {
                case Discipline.Pipe:
                    types = Types<PipeType>(); systems = Types<PipingSystemType>(); break;
                case Discipline.Duct:
                    types = Types<DuctType>(); systems = Types<MechanicalSystemType>(); break;
                case Discipline.Conduit:
                    types = Types<ConduitType>(); break;
                default:
                    types = Types<CableTrayType>(); break;
            }

            var levels = new FilteredElementCollector(doc).OfClass(typeof(Level)).Cast<Level>()
                .OrderBy(l => l.ProjectElevation)
                .Select(l => new TypeItem { Id = l.Id, Name = l.Name, Elevation = l.ProjectElevation, Element = l })
                .ToList();

            vm.SetTypes(types, systems, levels, doc.ActiveView?.GenLevel?.Id);
            vm.Log(LogLevel.Info, $"Loaded {types.Count} {vm.SelectedDiscipline.Label()} types, {systems.Count} system types, {levels.Count} levels.");
        }

        // ------------------------------------------------------------------ plan
        private static RoutePlan PlanPath(UIDocument uidoc, MainViewModel vm, RouteOptions o)
        {
            Document doc = uidoc.Document;
            if (vm.Source == null || vm.Target == null)
                throw new InvalidOperationException("Pick both SOURCE and TARGET connectors first.");
            if (vm.Source.OwnerId == vm.Target.OwnerId && vm.Source.ConnectorId == vm.Target.ConnectorId)
                throw new InvalidOperationException("Source and target are the same connector.");

            var level = doc.GetElement(o.LevelId) as Level;
            double levelZ = level?.ProjectElevation ?? 0;
            var inputs = vm.GetConstraintInputs();
            RouteRequest request = new RouteRequest
            {
                Source = vm.Source,
                Target = vm.Target,
                Boundary = inputs.Boundary,
                Options = vm.ConstraintOptions,
                Size = inputs.Size,
                PreChecks = inputs.PreChecks,
                MinSegmentMm = vm.MinSegmentMm
            };
            o.Size = request.Size;
            RoutePlan plan = RouteService.Plan(doc, request.Source, request.Target, o, levelZ, request.Options, request.Boundary, request.Size, request.MinSegmentMm);
            plan.Problems.InsertRange(0, request.PreChecks);
            return plan;
        }

        private static void Preview(UIDocument uidoc, MainViewModel vm)
        {
            var o = vm.BuildOptions();
            RoutePlan plan = PlanPath(uidoc, vm, o);
            vm.SetPreview(plan.Points, plan.Problems);
            vm.Log(LogLevel.Info, $"Preview: {plan.Points.Count - 1} segments, {plan.Problems.Count} problem(s).");
        }

        // ------------------------------------------------------------------ route
        private static void Route(UIDocument uidoc, MainViewModel vm)
        {
            Document doc = uidoc.Document;
            var o = vm.BuildOptions();

            if (o.TypeId == ElementId.InvalidElementId) throw new InvalidOperationException("Select a type.");
            if (o.Discipline.HasSystemType() && o.SystemTypeId == ElementId.InvalidElementId)
                throw new InvalidOperationException("Select a system type.");
            if (o.LevelId == ElementId.InvalidElementId) throw new InvalidOperationException("Select a reference level.");

            RoutePlan plan = PlanPath(uidoc, vm, o);
            vm.SetPreview(plan.Points, plan.Problems);
            if (PathValidator.HasErrors(plan.Problems))
            {
                vm.Log(LogLevel.Error, "Route blocked: resolve the Error problems first.");
                vm.Status = "Route blocked";
                return;
            }

            using var tx = new Transaction(doc, "MEP Auto Route");
            var swallower = new WarningSwallower();
            if (o.SuppressWarnings)
            {
                var fho = tx.GetFailureHandlingOptions();
                fho.SetFailuresPreprocessor(swallower);
                tx.SetFailureHandlingOptions(fho);
            }
            tx.Start();

            RouteResult res;
            try
            {
                res = RouteBuilder.Build(doc, vm.Source, vm.Target, plan.Points, o, vm.Log);
            }
            catch
            {
                tx.RollBack();
                throw;
            }

            if (tx.Commit() != TransactionStatus.Committed)
            {
                vm.Log(LogLevel.Error, "Transaction was not committed.");
                return;
            }

            if (vm.SelectAfterRoute && res.Created.Count > 0)
                uidoc.Selection.SetElementIds(res.Created);

            var level = res.FittingFailures == 0 ? LogLevel.Success : LogLevel.Warn;
            vm.Log(level, $"Routed {res.Segments} segment(s), {res.Fittings} elbow(s), " +
                          $"{res.FittingFailures} elbow failure(s), {swallower.Suppressed} warning(s) suppressed.");
            vm.Status = "Route complete";

            // connectors are now used – clear so the next pick is fresh
            vm.Source = null;
            vm.Target = null;
        }
    }
}
