using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Autodesk.Revit.UI.Selection;
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

        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            UIDocument uidoc = commandData.Application.ActiveUIDocument;
            Document doc = uidoc.Document;

            XYZ startPoint = null;
            XYZ endPoint = null;
            Connector startConnector = null;
            Connector endConnector = null;
            Element startElement = null;
            Element endElement = null;
            Element routingVolumeElement = null;
            BoundingBoxXYZ routingBounds = null;

            List<PipeTypeOption> pipeTypeOptions = BuildPipeTypeOptions(doc);
            ElementId selectedPipeTypeId = pipeTypeOptions.Count > 0 ? pipeTypeOptions[0].Id : ElementId.InvalidElementId;
            double selectedPipeDiameterMm = 100.0;

            try
            {
                bool keepShowingUI = true;

                while (keepShowingUI)
                {
                    UnifiedRoutingUI ui = new UnifiedRoutingUI();

                    ui.StartPoint = startPoint;
                    ui.EndPoint = endPoint;
                    ui.StartConnector = startConnector;
                    ui.EndConnector = endConnector;
                    ui.StartElement = startElement;
                    ui.EndElement = endElement;
                    ui.RoutingVolumeElement = routingVolumeElement;
                    ui.RoutingBounds = routingBounds;
                    ui.PipeTypeOptions = pipeTypeOptions;
                    ui.SelectedPipeTypeId = selectedPipeTypeId;
                    ui.SelectedPipeDiameterMm = selectedPipeDiameterMm;
                    ui.RefreshUI();

                    bool? dialogResult = ui.ShowDialog();

                    if (dialogResult != true)
                        return Result.Cancelled;

                    selectedPipeTypeId = ui.SelectedPipeTypeId;
                    selectedPipeDiameterMm = ui.SelectedPipeDiameterMm;

                    if (ui.ActionRequested == UnifiedRoutingUI.UserAction.PickStart)
                    {
                        try
                        {
                            Reference reference = uidoc.Selection.PickObject(ObjectType.Element, "Select start element / connector");
                            startElement = doc.GetElement(reference);
                            XYZ pickPoint = reference.GlobalPoint;

                            bool preferPipe = CurrentMode == RoutingMode.Pipe;
                            bool preferElectrical = CurrentMode == RoutingMode.Conduit;

                            startConnector = ConnectorUtils.GetClosestConnector(startElement, pickPoint, preferPipe, preferElectrical);
                            startPoint = startConnector != null ? startConnector.Origin : ConnectorUtils.GetFallbackPoint(startElement);
                        }
                        catch (Autodesk.Revit.Exceptions.OperationCanceledException)
                        {
                            // Keep UI loop running.
                        }
                    }
                    else if (ui.ActionRequested == UnifiedRoutingUI.UserAction.PickEnd)
                    {
                        try
                        {
                            Reference reference = uidoc.Selection.PickObject(ObjectType.Element, "Select end element / connector");
                            endElement = doc.GetElement(reference);
                            XYZ pickPoint = reference.GlobalPoint;

                            bool preferPipe = CurrentMode == RoutingMode.Pipe;
                            bool preferElectrical = CurrentMode == RoutingMode.Conduit;

                            endConnector = ConnectorUtils.GetClosestConnector(endElement, pickPoint, preferPipe, preferElectrical);
                            endPoint = endConnector != null ? endConnector.Origin : ConnectorUtils.GetFallbackPoint(endElement);
                        }
                        catch (Autodesk.Revit.Exceptions.OperationCanceledException)
                        {
                            // Keep UI loop running.
                        }
                    }
                    else if (ui.ActionRequested == UnifiedRoutingUI.UserAction.PickVolume)
                    {
                        try
                        {
                            Reference reference = uidoc.Selection.PickObject(ObjectType.Element, "Select HOST Room / Space / Mass / Generic routing volume");
                            Element hostElement = doc.GetElement(reference);

                            if (!RoutingVolumeUtils.IsSupportedRoutingVolume(hostElement))
                            {
                                TaskDialog.Show("Routing Volume", "Please select a HOST Room, Space, Mass, or Generic Model volume. Linked volumes are not allowed.");
                                continue;
                            }

                            BoundingBoxXYZ bb = RoutingVolumeUtils.GetHostElementBounds(doc, hostElement);
                            if (bb == null)
                            {
                                TaskDialog.Show("Routing Volume", "Cannot read bounding box from selected HOST routing volume.");
                                continue;
                            }

                            routingVolumeElement = hostElement;
                            routingBounds = bb;
                        }
                        catch (Autodesk.Revit.Exceptions.OperationCanceledException)
                        {
                            // Keep UI loop running.
                        }
                    }
                    else if (ui.ActionRequested == UnifiedRoutingUI.UserAction.Run)
                    {
                        if (startPoint == null || endPoint == null)
                        {
                            TaskDialog.Show("Routing", "Please select both start and end.");
                            continue;
                        }

                        if (startPoint.DistanceTo(endPoint) < 0.01)
                        {
                            TaskDialog.Show("Routing", "Start and End points are too close.");
                            continue;
                        }

                        if (routingBounds == null)
                        {
                            TaskDialog.Show("Routing", "Please select a HOST Room / Space / Mass routing volume first.");
                            continue;
                        }

                        if (!RoutingVolumeUtils.IsPointInsideBounds(startPoint, routingBounds) ||
                            !RoutingVolumeUtils.IsPointInsideBounds(endPoint, routingBounds))
                        {
                            TaskDialog.Show("Routing", "Start or End point is outside the selected HOST routing volume.");
                            continue;
                        }

                        if (CurrentMode == RoutingMode.Pipe && selectedPipeTypeId == ElementId.InvalidElementId)
                        {
                            TaskDialog.Show("Pipe Settings", "Please select a valid pipe type.");
                            continue;
                        }

                        keepShowingUI = false;
                    }
                    else
                    {
                        return Result.Cancelled;
                    }
                }

                if (CurrentMode == RoutingMode.Pipe)
                {
                    PipeRoutingEngine engine = new PipeRoutingEngine(doc, routingBounds);
                    List<XYZ> path = engine.GeneratePath(startPoint, endPoint);
                    CreatePipe(doc, path, startConnector, endConnector, selectedPipeTypeId, selectedPipeDiameterMm);
                }
                else
                {
                    ConduitRoutingEngine engine = new ConduitRoutingEngine(doc, routingBounds);
                    List<XYZ> path = engine.GeneratePath(startPoint, endPoint);
                    ConduitCreator creator = new ConduitCreator(doc);
                    creator.Create(path);
                    TaskDialog.Show("Routing Success", "Conduit routing completed." + Environment.NewLine + "Points: " + (path == null ? 0 : path.Count));
                }

                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                TaskDialog.Show("Routing Error", ex.ToString());
                return Result.Failed;
            }
        }

        private List<PipeTypeOption> BuildPipeTypeOptions(Document doc)
        {
            List<PipeTypeOption> options = new List<PipeTypeOption>();

            FilteredElementCollector collector = new FilteredElementCollector(doc)
                .OfClass(typeof(Autodesk.Revit.DB.Plumbing.PipeType));

            foreach (Element elem in collector)
            {
                if (elem == null)
                    continue;

                string name = elem.Name;
                if (string.IsNullOrEmpty(name))
                    name = "PipeType " + elem.Id.Value;

                options.Add(new PipeTypeOption(elem.Id, name));
            }

            return options;
        }

        private void CreatePipe(
            Document doc,
            List<XYZ> path,
            Connector startConnector,
            Connector endConnector,
            ElementId selectedPipeTypeId,
            double selectedPipeDiameterMm)
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

            ElementId pipeTypeId = selectedPipeTypeId;
            if (pipeTypeId == ElementId.InvalidElementId)
                pipeTypeId = RoutingUtils.GetFirstPipeTypeId(doc);

            ElementId systemTypeId = RoutingUtils.GetFirstPipingSystemTypeId(doc);

            if (levelId == ElementId.InvalidElementId || pipeTypeId == ElementId.InvalidElementId || systemTypeId == ElementId.InvalidElementId)
            {
                TaskDialog.Show("Pipe Error", "Missing Level, PipeType, or PipingSystemType.");
                return;
            }

            int createdPipeCount = 0;
            int createdFittingCount = 0;
            List<SegmentInfo> segments = new List<SegmentInfo>();

            using (Transaction transaction = new Transaction(doc, "Pipe Routing With Fittings"))
            {
                transaction.Start();

                for (int i = 0; i < path.Count - 1; i++)
                {
                    XYZ p1 = path[i];
                    XYZ p2 = path[i + 1];

                    if (p1 == null || p2 == null || p1.DistanceTo(p2) < 0.001)
                        continue;

                    Autodesk.Revit.DB.Plumbing.Pipe pipe = Autodesk.Revit.DB.Plumbing.Pipe.Create(doc, systemTypeId, pipeTypeId, levelId, p1, p2);

                    if (pipe != null)
                    {
                        createdPipeCount++;

                        Parameter dia = pipe.get_Parameter(BuiltInParameter.RBS_PIPE_DIAMETER_PARAM);
                        if (dia != null && !dia.IsReadOnly && selectedPipeDiameterMm > 0)
                        {
                            double diameter = UnitUtils.ConvertToInternalUnits(selectedPipeDiameterMm, UnitTypeId.Millimeters);
                            dia.Set(diameter);
                        }

                        segments.Add(new SegmentInfo(pipe, p1, p2));
                    }
                }

                for (int i = 0; i < segments.Count - 1; i++)
                {
                    SegmentInfo a = segments[i];
                    SegmentInfo b = segments[i + 1];
                    XYZ jointPoint = a.End;

                    Connector ca = GetClosestConnector(a.Pipe, jointPoint);
                    Connector cb = GetClosestConnector(b.Pipe, jointPoint);

                    if (ca == null || cb == null)
                        continue;

                    bool created = CreatePipeFittingOrConnection(doc, ca, cb, a.Start, a.End, b.Start, b.End);
                    if (created)
                        createdFittingCount++;
                }

                if (segments.Count > 0)
                {
                    TryConnectPipeEnd(segments[0].Pipe, path[0], startConnector);
                    TryConnectPipeEnd(segments[segments.Count - 1].Pipe, path[path.Count - 1], endConnector);
                }

                transaction.Commit();
            }

            TaskDialog.Show(
                "Pipe Debug",
                "Pipe routing completed." +
                Environment.NewLine + "Path points: " + path.Count +
                Environment.NewLine + "Created pipes: " + createdPipeCount +
                Environment.NewLine + "Created fittings/connections: " + createdFittingCount +
                Environment.NewLine + "Pipe size(mm): " + selectedPipeDiameterMm);
        }

        private class SegmentInfo
        {
            public Autodesk.Revit.DB.Plumbing.Pipe Pipe { get; private set; }
            public XYZ Start { get; private set; }
            public XYZ End { get; private set; }

            public SegmentInfo(Autodesk.Revit.DB.Plumbing.Pipe pipe, XYZ start, XYZ end)
            {
                Pipe = pipe;
                Start = start;
                End = end;
            }
        }

        private bool CreatePipeFittingOrConnection(Document doc, Connector c1, Connector c2, XYZ aStart, XYZ aEnd, XYZ bStart, XYZ bEnd)
        {
            if (doc == null || c1 == null || c2 == null)
                return false;

            XYZ v1 = aEnd - aStart;
            XYZ v2 = bEnd - bStart;

            if (v1.GetLength() < 1e-9 || v2.GetLength() < 1e-9)
                return false;

            XYZ d1 = v1.Normalize();
            XYZ d2 = v2.Normalize();

            double directionDiff = d1.DistanceTo(d2);
            double oppositeDiff = d1.DistanceTo(d2.Negate());

            try
            {
                if (directionDiff < 0.01 || oppositeDiff < 0.01)
                {
                    if (!c1.IsConnectedTo(c2))
                    {
                        c1.ConnectTo(c2);
                        return true;
                    }

                    return false;
                }

                doc.Create.NewElbowFitting(c1, c2);
                return true;
            }
            catch
            {
                try
                {
                    if (!c1.IsConnectedTo(c2))
                    {
                        c1.ConnectTo(c2);
                        return true;
                    }
                }
                catch
                {
                    // Ignore fitting failure.
                }
            }

            return false;
        }

        private Connector GetClosestConnector(Autodesk.Revit.DB.Plumbing.Pipe pipe, XYZ point)
        {
            if (pipe == null || point == null)
                return null;

            Connector best = null;
            double bestDistance = double.MaxValue;

            foreach (Connector connector in pipe.ConnectorManager.Connectors)
            {
                double distance = connector.Origin.DistanceTo(point);

                if (distance < bestDistance)
                {
                    bestDistance = distance;
                    best = connector;
                }
            }

            return best;
        }

        private void TryConnectPipeEnd(Autodesk.Revit.DB.Plumbing.Pipe pipe, XYZ point, Connector targetConnector)
        {
            if (pipe == null || point == null || targetConnector == null)
                return;

            Connector bestPipeConnector = GetClosestConnector(pipe, point);

            if (bestPipeConnector == null)
                return;

            double tolerance = UnitUtils.ConvertToInternalUnits(5.0, UnitTypeId.Millimeters);

            if (bestPipeConnector.Origin.DistanceTo(targetConnector.Origin) > tolerance)
                return;

            try
            {
                if (!bestPipeConnector.IsConnectedTo(targetConnector))
                    bestPipeConnector.ConnectTo(targetConnector);
            }
            catch
            {
                // Ignore connection failure. Pipe geometry is still created.
            }
        }
    }
}
