using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using System;
using System.Collections.Generic;
using MEPAutoRouting.Routing;
using MEPAutoRouting.Plumbing;
using MEPAutoRouting.Electrical;
using MEPAutoRouting.Core;

namespace MEPAutoRouting
{
    [Transaction(TransactionMode.Manual)]
    public class UnifiedRoutingCommand : IExternalCommand
    {
        public static RoutingMode CurrentMode = RoutingMode.Conduit;

        public Result Execute(
            ExternalCommandData commandData,
            ref string message,
            ElementSet elements)
        {
            UIDocument uidoc = commandData.Application.ActiveUIDocument;
            Document doc = uidoc.Document;

            try
            {
                UnifiedRoutingUI ui = new UnifiedRoutingUI(uidoc);
                bool? result = ui.ShowDialog();

                XYZ start = ui.StartPoint;
                XYZ end = ui.EndPoint;

                TaskDialog.Show(
                    "Command Debug",
                    "ShowDialog returned." +
                    "\nResult: " + result +
                    "\nCurrentMode: " + CurrentMode +
                    "\nStart null: " + (start == null) +
                    "\nEnd null: " + (end == null));

                if (result != true)
                    return Result.Cancelled;

                if (start == null || end == null)
                {
                    TaskDialog.Show("Routing Error", "Start or End point is null.");
                    return Result.Failed;
                }

                if (CurrentMode == RoutingMode.Pipe)
                {
                    PipeRoutingEngine engine = new PipeRoutingEngine(doc);
                    List<XYZ> path = engine.GeneratePath(start, end);

                    CreatePipe(doc, path, ui.StartConnector, ui.EndConnector);
                }
                else
                {
                    ConduitRoutingEngine engine = new ConduitRoutingEngine(doc);
                    List<XYZ> path = engine.GeneratePath(start, end);

                    ConduitCreator creator = new ConduitCreator(doc);
                    creator.Create(path);

                    TaskDialog.Show(
                        "Conduit Debug",
                        "Conduit routing completed." +
                        "\nPath points: " + (path == null ? 0 : path.Count) +
                        "\nStart: " + FormatXYZ(start) +
                        "\nEnd: " + FormatXYZ(end));
                }

                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                TaskDialog.Show("Routing Error", ex.ToString());
                return Result.Failed;
            }
        }

        private void CreatePipe(
            Document doc,
            List<XYZ> path,
            Connector startConnector,
            Connector endConnector)
        {
            if (path == null || path.Count < 2)
            {
                TaskDialog.Show("Pipe Debug", "Path is null or has less than 2 points.");
                return;
            }

            ElementId levelId = ElementId.InvalidElementId;

            ViewPlan activePlan = doc.ActiveView as ViewPlan;
            if (activePlan != null && activePlan.GenLevel != null)
                levelId = activePlan.GenLevel.Id;

            if (levelId == ElementId.InvalidElementId)
                levelId = RoutingUtils.GetFirstLevelId(doc);

            ElementId pipeTypeId = RoutingUtils.GetFirstPipeTypeId(doc);
            ElementId systemTypeId = RoutingUtils.GetFirstPipingSystemTypeId(doc);

            if (levelId == ElementId.InvalidElementId)
            {
                TaskDialog.Show("Pipe Debug", "No Level found.");
                return;
            }

            if (pipeTypeId == ElementId.InvalidElementId)
            {
                TaskDialog.Show("Pipe Debug", "No PipeType found.");
                return;
            }

            if (systemTypeId == ElementId.InvalidElementId)
            {
                TaskDialog.Show("Pipe Debug", "No PipingSystemType found.");
                return;
            }

            int createdCount = 0;

            List<Autodesk.Revit.DB.Plumbing.Pipe> createdPipes =
                new List<Autodesk.Revit.DB.Plumbing.Pipe>();

            using (Transaction t = new Transaction(doc, "Pipe Routing"))
            {
                t.Start();

                for (int i = 0; i < path.Count - 1; i++)
                {
                    XYZ p1 = path[i];
                    XYZ p2 = path[i + 1];

                    if (p1 == null || p2 == null)
                        continue;

                    if (p1.DistanceTo(p2) < 0.001)
                        continue;

                    Autodesk.Revit.DB.Plumbing.Pipe pipe =
                        Autodesk.Revit.DB.Plumbing.Pipe.Create(
                            doc,
                            systemTypeId,
                            pipeTypeId,
                            levelId,
                            p1,
                            p2);

                    if (pipe != null)
                    {
                        createdPipes.Add(pipe);
                        createdCount++;

                        Parameter dia = pipe.get_Parameter(
                            BuiltInParameter.RBS_PIPE_DIAMETER_PARAM);

                        if (dia != null && !dia.IsReadOnly)
                        {
                            double diameter = UnitUtils.ConvertToInternalUnits(
                                100,
                                UnitTypeId.Millimeters);

                            dia.Set(diameter);
                        }
                    }
                }

                if (createdPipes.Count > 0)
                {
                    TryConnectPipeEnd(
                        createdPipes[0],
                        path[0],
                        startConnector);

                    TryConnectPipeEnd(
                        createdPipes[createdPipes.Count - 1],
                        path[path.Count - 1],
                        endConnector);
                }

                t.Commit();
            }

            TaskDialog.Show(
                "Pipe Debug",
                "Pipe routing completed." +
                "\nPath points: " + path.Count +
                "\nCreated pipes: " + createdCount +
                "\nStart: " + FormatXYZ(path[0]) +
                "\nEnd: " + FormatXYZ(path[path.Count - 1]) +
                "\nLevelId: " + levelId.Value +
                "\nPipeTypeId: " + pipeTypeId.Value +
                "\nSystemTypeId: " + systemTypeId.Value);
        }

        private void TryConnectPipeEnd(
            Autodesk.Revit.DB.Plumbing.Pipe pipe,
            XYZ point,
            Connector targetConnector)
        {
            if (pipe == null || point == null || targetConnector == null)
                return;

            Connector bestPipeConnector = null;
            double bestDistance = double.MaxValue;

            foreach (Connector pipeConnector in pipe.ConnectorManager.Connectors)
            {
                double distance = pipeConnector.Origin.DistanceTo(point);

                if (distance < bestDistance)
                {
                    bestDistance = distance;
                    bestPipeConnector = pipeConnector;
                }
            }

            if (bestPipeConnector == null)
                return;

            double tolerance = UnitUtils.ConvertToInternalUnits(
                5.0,
                UnitTypeId.Millimeters);

            if (bestPipeConnector.Origin.DistanceTo(targetConnector.Origin) > tolerance)
                return;

            try
            {
                bestPipeConnector.ConnectTo(targetConnector);
            }
            catch
            {
                // Ignore connection failure. Pipe geometry is still created.
            }
        }

        private string FormatXYZ(XYZ point)
        {
            if (point == null)
                return "(null)";

            return "(" +
                   point.X.ToString("F3") + ", " +
                   point.Y.ToString("F3") + ", " +
                   point.Z.ToString("F3") + ")";
        }
    }
}