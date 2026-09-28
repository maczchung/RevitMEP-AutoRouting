using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Autodesk.Revit.UI.Selection;
using System;
using System.Collections.Generic;
using MEPAutoRouting.Routing;
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
            XYZ startPoint = null, endPoint = null;
            Connector startConnector = null, endConnector = null;
            Element startElement = null, endElement = null, routingVolumeElement = null;
            BoundingBoxXYZ routingBounds = null;
            List<PipeTypeOption> pipeTypeOptions = BuildPipeTypeOptions(doc);
            ElementId selectedPipeTypeId = pipeTypeOptions.Count > 0 ? pipeTypeOptions[0].Id : ElementId.InvalidElementId;
            double selectedPipeDiameterMm = 100.0;
            SpaceVolume selectedSpace = null;
            RoutingConstraints routingConstraints = null;
            double selectedWallClearanceMm = 0;

            try
            {
                bool keepShowingUI = true;
                while (keepShowingUI)
                {
                    UnifiedRoutingUI ui = new UnifiedRoutingUI();
                    ui.StartPoint = startPoint; ui.EndPoint = endPoint; ui.StartConnector = startConnector; ui.EndConnector = endConnector;
                    ui.StartElement = startElement; ui.EndElement = endElement; ui.RoutingVolumeElement = routingVolumeElement; ui.RoutingBounds = routingBounds;
                    ui.PipeTypeOptions = pipeTypeOptions; ui.SelectedPipeTypeId = selectedPipeTypeId; ui.SelectedPipeDiameterMm = selectedPipeDiameterMm;
                    ui.RefreshUI();
                    bool? dialogResult = ui.ShowDialog();
                    if (dialogResult != true) return Result.Cancelled;
                    selectedPipeTypeId = ui.SelectedPipeTypeId; selectedPipeDiameterMm = ui.SelectedPipeDiameterMm;

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
                        catch (Autodesk.Revit.Exceptions.OperationCanceledException) { }
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
                        catch (Autodesk.Revit.Exceptions.OperationCanceledException) { }
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
                            routingVolumeElement = hostElement; routingBounds = bb;
                        }
                        catch (Autodesk.Revit.Exceptions.OperationCanceledException) { }
                    }
                    else if (ui.ActionRequested == UnifiedRoutingUI.UserAction.Run)
                    {
                        if (startPoint == null || endPoint == null) { TaskDialog.Show("Routing", "Please select both start and end."); continue; }
                        if (startPoint.DistanceTo(endPoint) < 0.01) { TaskDialog.Show("Routing", "Start and End points are too close."); continue; }
                        RoutingOptions options = ui.Options;
                        try
                        {
                            selectedSpace = options.SpaceSource switch
                            {
                                SpaceSource.Host => SpacePicker.PickHost(uidoc),
                                SpaceSource.Link => SpacePicker.PickLinked(uidoc),
                                _ => null
                            };
                        }
                        catch (InvalidOperationException ex)
                        {
                            TaskDialog.Show("Unified Routing", ex.Message);
                            continue;
                        }
                        if (options.SpaceSource != SpaceSource.None && selectedSpace == null) continue;
                        if (routingBounds == null && selectedSpace == null)
                        { TaskDialog.Show("Routing", "Please select a HOST Room / Space / Mass routing volume first."); continue; }

                        XYZ regionMin = selectedSpace != null ? selectedSpace.Min : routingBounds.Min;
                        XYZ regionMax = selectedSpace != null ? selectedSpace.Max : routingBounds.Max;
                        double diameter = UnitUtils.ConvertToInternalUnits(selectedPipeDiameterMm, UnitTypeId.Millimeters);
                        const double cellSize = 0.5;
                        double clearance = options.WallClearanceFt;
                        List<WallObstacle> walls = WallObstacleCollector.Collect(doc, regionMin, regionMax, options.IncludeLinkWalls, clearance + diameter / 2.0);
                        routingConstraints = new RoutingConstraints(selectedSpace, walls, clearance, diameter / 2.0, options.ClearanceOnSpaceBoundary);
                        routingConstraints.AddExemptPoint(startPoint, routingConstraints.SuggestedExemptRadius(cellSize));
                        routingConstraints.AddExemptPoint(endPoint, routingConstraints.SuggestedExemptRadius(cellSize));
                        selectedWallClearanceMm = options.WallClearanceMm;
                        if (selectedSpace != null && (!selectedSpace.Contains(startPoint) || !selectedSpace.Contains(endPoint)))
                        { TaskDialog.Show("Unified Routing", "起點或終點唔喺 Space '" + selectedSpace.Name + "' 入面。"); continue; }
                        routingBounds = CreateBounds(regionMin, regionMax);
                        if (!RoutingVolumeUtils.IsPointInsideBounds(startPoint, routingBounds) || !RoutingVolumeUtils.IsPointInsideBounds(endPoint, routingBounds))
                        { TaskDialog.Show("Routing", "Start or End point is outside the selected routing volume."); continue; }
                        if (CurrentMode == RoutingMode.Pipe && selectedPipeTypeId == ElementId.InvalidElementId)
                        { TaskDialog.Show("Pipe Settings", "Please select a valid pipe type."); continue; }
                        keepShowingUI = false;
                    }
                    else return Result.Cancelled;
                }

                AStarPathfinder finder = new AStarPathfinder(doc, routingBounds, routingConstraints);
                XYZ startDir = XYZ.BasisX;
                if (startConnector != null && startConnector.CoordinateSystem != null) startDir = startConnector.CoordinateSystem.BasisZ;
                List<XYZ> path = finder.FindPath(startPoint, endPoint, startDir);

                if (CurrentMode == RoutingMode.Pipe)
                    CreatePipe(doc, path, startConnector, endConnector, selectedPipeTypeId, selectedPipeDiameterMm, selectedSpace, selectedWallClearanceMm);
                else
                {
                    if (!PathUtils.IsValid(path))
                    {
                        TaskDialog.Show("Unified Routing", "搵唔到可行路徑，冇建立 conduit。");
                        return Result.Cancelled;
                    }
                    ConduitCreator creator = new ConduitCreator(doc);
                    List<string> failures = new List<string>();
                    int createdCount = creator.Create(path, failures);
                    string failureText = failures.Count == 0 ? "None" : string.Join(Environment.NewLine, failures);
                    string spaceText = selectedSpace == null ? "None" : selectedSpace.Name;
                    TaskDialog.Show("Routing Success", "Conduit routing completed." + Environment.NewLine + "Points: " + path.Count + Environment.NewLine + "Created conduits: " + createdCount + Environment.NewLine + "Space: " + spaceText + Environment.NewLine + "Wall clearance(mm): " + selectedWallClearanceMm + Environment.NewLine + "Elbow failures:" + Environment.NewLine + failureText);
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
            FilteredElementCollector collector = new FilteredElementCollector(doc).OfClass(typeof(Autodesk.Revit.DB.Plumbing.PipeType));
            foreach (Element elem in collector)
            {
                if (elem == null) continue;
                string name = elem.Name;
                if (string.IsNullOrEmpty(name)) name = "PipeType " + elem.Id.Value;
                options.Add(new PipeTypeOption(elem.Id, name));
            }
            return options;
        }

        private void CreatePipe(Document doc, List<XYZ> path, Connector startConnector, Connector endConnector, ElementId selectedPipeTypeId, double selectedPipeDiameterMm, SpaceVolume selectedSpace = null, double wallClearanceMm = 0)
        {
            if (path == null || path.Count < 2) { TaskDialog.Show("Pipe Debug", "Path is null or has less than 2 points."); return; }
            path = PreparePathForConnectorConnection(path, startConnector, endConnector);
            if (path == null || path.Count < 2) { TaskDialog.Show("Pipe Debug", "Prepared path is null or has less than 2 points."); return; }

            ElementId levelId = ElementId.InvalidElementId;
            ViewPlan activePlan = doc.ActiveView as ViewPlan;
            if (activePlan != null && activePlan.GenLevel != null) levelId = activePlan.GenLevel.Id;
            if (levelId == ElementId.InvalidElementId) levelId = RoutingUtils.GetFirstLevelId(doc);
            ElementId pipeTypeId = selectedPipeTypeId != ElementId.InvalidElementId ? selectedPipeTypeId : RoutingUtils.GetFirstPipeTypeId(doc);
            ElementId systemTypeId = RoutingUtils.GetFirstPipingSystemTypeId(doc);
            if (levelId == ElementId.InvalidElementId || pipeTypeId == ElementId.InvalidElementId || systemTypeId == ElementId.InvalidElementId)
            { TaskDialog.Show("Pipe Error", "Missing Level, PipeType, or PipingSystemType."); return; }

            int createdPipeCount = 0, createdFittingCount = 0, connectedEndCount = 0;
            List<SegmentInfo> segments = new List<SegmentInfo>();
            List<string> failures = new List<string>();
            using (Transaction transaction = new Transaction(doc, "Pipe Routing With Horizontal Path Fix"))
            {
                transaction.Start();
                for (int i = 0; i < path.Count - 1; i++)
                {
                    XYZ p1 = path[i]; XYZ p2 = path[i + 1];
                    if (p1 == null || p2 == null || p1.DistanceTo(p2) < 0.001) continue;
                    Autodesk.Revit.DB.Plumbing.Pipe pipe = Autodesk.Revit.DB.Plumbing.Pipe.Create(doc, systemTypeId, pipeTypeId, levelId, p1, p2);
                    if (pipe != null)
                    {
                        createdPipeCount++;
                        Parameter dia = pipe.get_Parameter(BuiltInParameter.RBS_PIPE_DIAMETER_PARAM);
                        if (dia != null && !dia.IsReadOnly && selectedPipeDiameterMm > 0)
                            dia.Set(UnitUtils.ConvertToInternalUnits(selectedPipeDiameterMm, UnitTypeId.Millimeters));
                        segments.Add(new SegmentInfo(pipe, p1, p2));
                    }
                }
                doc.Regenerate();
                List<MEPCurve> fittingSegments = new List<MEPCurve>();
                foreach (SegmentInfo segment in segments) fittingSegments.Add(segment.Pipe);
                createdFittingCount = MepFittingUtils.CreateElbows(doc, fittingSegments, path, failures);
                doc.Regenerate();
                if (segments.Count > 0)
                {
                    if (TryConnectPipeEnd(segments[0].Pipe, path[0], startConnector)) connectedEndCount++;
                    if (TryConnectPipeEnd(segments[segments.Count - 1].Pipe, path[path.Count - 1], endConnector)) connectedEndCount++;
                }
                transaction.Commit();
            }
            string failureText = failures.Count == 0 ? "None" : string.Join(Environment.NewLine, failures);
            string spaceText = selectedSpace == null ? "None" : selectedSpace.Name;
            TaskDialog.Show("Pipe Debug", "Pipe routing completed." + Environment.NewLine + "Path points: " + path.Count + Environment.NewLine + "Created pipes: " + createdPipeCount + Environment.NewLine + "Created elbows: " + createdFittingCount + Environment.NewLine + "Connected equipment ends: " + connectedEndCount + " / 2" + Environment.NewLine + "Pipe size(mm): " + selectedPipeDiameterMm + Environment.NewLine + "Space: " + spaceText + Environment.NewLine + "Wall clearance(mm): " + wallClearanceMm + Environment.NewLine + "Elbow failures:" + Environment.NewLine + failureText);
        }

        private BoundingBoxXYZ CreateBounds(XYZ min, XYZ max)
        {
            BoundingBoxXYZ bounds = new BoundingBoxXYZ();
            bounds.Min = min;
            bounds.Max = max;
            return bounds;
        }

        private List<XYZ> PreparePathForConnectorConnection(List<XYZ> originalPath, Connector startConnector, Connector endConnector)
        {
            List<XYZ> path = new List<XYZ>();
            foreach (XYZ p in originalPath)
            {
                if (p == null) continue;
                if (path.Count == 0 || path[path.Count - 1].DistanceTo(p) > 0.001) path.Add(p);
            }
            if (path.Count < 2) return path;
            if (startConnector != null) path[0] = startConnector.Origin;
            if (endConnector != null) path[path.Count - 1] = endConnector.Origin;
            List<XYZ> axisPath = new List<XYZ>();
            axisPath.Add(path[0]);
            for (int i = 1; i < path.Count; i++) AddAxisAlignedTransition(axisPath, path[i]);
            return RemoveDuplicateAndShortPoints(axisPath);
        }

        private void AddAxisAlignedTransition(List<XYZ> path, XYZ target)
        {
            if (path == null || target == null) return;
            if (path.Count == 0) { path.Add(target); return; }
            XYZ last = path[path.Count - 1];
            if (last.DistanceTo(target) < 0.001) return;
            bool sameX = Math.Abs(last.X - target.X) < 0.0001;
            bool sameY = Math.Abs(last.Y - target.Y) < 0.0001;
            bool sameZ = Math.Abs(last.Z - target.Z) < 0.0001;
            if ((sameX && sameY) || (sameX && sameZ) || (sameY && sameZ)) { path.Add(target); return; }
            XYZ pX = new XYZ(target.X, last.Y, last.Z);
            XYZ pY = new XYZ(target.X, target.Y, last.Z);
            if (last.DistanceTo(pX) > 0.001) path.Add(pX);
            if (pX.DistanceTo(pY) > 0.001) path.Add(pY);
            if (pY.DistanceTo(target) > 0.001) path.Add(target);
        }

        private List<XYZ> RemoveDuplicateAndShortPoints(List<XYZ> path)
        {
            if (path == null || path.Count < 2) return path;
            List<XYZ> cleaned = new List<XYZ>();
            cleaned.Add(path[0]);
            for (int i = 1; i < path.Count; i++)
                if (cleaned[cleaned.Count - 1].DistanceTo(path[i]) > 0.01) cleaned.Add(path[i]);
            return cleaned;
        }

        private class SegmentInfo
        {
            public Autodesk.Revit.DB.Plumbing.Pipe Pipe { get; private set; }
            public XYZ Start { get; private set; }
            public XYZ End { get; private set; }
            public SegmentInfo(Autodesk.Revit.DB.Plumbing.Pipe pipe, XYZ start, XYZ end) { Pipe = pipe; Start = start; End = end; }
        }

        private bool CreatePipeFittingOrConnection(Document doc, Connector c1, Connector c2, XYZ aStart, XYZ aEnd, XYZ bStart, XYZ bEnd)
        {
            if (doc == null || c1 == null || c2 == null) return false;
            XYZ v1 = aEnd - aStart; XYZ v2 = bEnd - bStart;
            if (v1.GetLength() < 1e-9 || v2.GetLength() < 1e-9) return false;
            XYZ d1 = v1.Normalize(); XYZ d2 = v2.Normalize();
            double directionDiff = d1.DistanceTo(d2); double oppositeDiff = d1.DistanceTo(d2.Negate());
            try
            {
                if (directionDiff < 0.01 || oppositeDiff < 0.01)
                {
                    if (!c1.IsConnectedTo(c2)) { c1.ConnectTo(c2); return true; }
                    return false;
                }
                doc.Create.NewElbowFitting(c1, c2);
                return true;
            }
            catch
            {
                try { if (!c1.IsConnectedTo(c2)) { c1.ConnectTo(c2); return true; } } catch { }
            }
            return false;
        }

        private Connector GetClosestUnusedConnector(Autodesk.Revit.DB.Plumbing.Pipe pipe, XYZ point)
        {
            if (pipe == null || point == null) return null;
            Connector best = null; double bestDistance = double.MaxValue;
            foreach (Connector connector in pipe.ConnectorManager.Connectors)
            {
                if (connector == null || connector.IsConnected) continue;
                double distance = connector.Origin.DistanceTo(point);
                if (distance < bestDistance) { bestDistance = distance; best = connector; }
            }
            return best;
        }

        private Connector GetClosestConnector(Autodesk.Revit.DB.Plumbing.Pipe pipe, XYZ point)
        {
            if (pipe == null || point == null) return null;
            Connector best = null; double bestDistance = double.MaxValue;
            foreach (Connector connector in pipe.ConnectorManager.Connectors)
            {
                if (connector == null) continue;
                double distance = connector.Origin.DistanceTo(point);
                if (distance < bestDistance) { bestDistance = distance; best = connector; }
            }
            return best;
        }

        private bool TryConnectPipeEnd(Autodesk.Revit.DB.Plumbing.Pipe pipe, XYZ point, Connector targetConnector)
        {
            if (pipe == null || point == null || targetConnector == null) return false;
            Connector bestPipeConnector = GetClosestUnusedConnector(pipe, point) ?? GetClosestConnector(pipe, point);
            if (bestPipeConnector == null) return false;
            double tolerance = UnitUtils.ConvertToInternalUnits(80.0, UnitTypeId.Millimeters);
            if (bestPipeConnector.Origin.DistanceTo(targetConnector.Origin) > tolerance) return false;
            try
            {
                if (!bestPipeConnector.IsConnectedTo(targetConnector)) { bestPipeConnector.ConnectTo(targetConnector); return true; }
                return true;
            }
            catch { return false; }
        }
    }
}
