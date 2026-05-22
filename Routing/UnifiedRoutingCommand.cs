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

            // 紀錄使用者的選擇狀態
            XYZ startPoint = null;
            XYZ endPoint = null;
            Connector startConnector = null;
            Connector endConnector = null;
            Element startElement = null;
            Element endElement = null;

            bool keepShowingUI = true;

            try
            {
                // UI 迴圈：只要沒按 Run 或 Cancel，選完東西就重開視窗
                while (keepShowingUI)
                {
                    UnifiedRoutingUI ui = new UnifiedRoutingUI();
                    
                    // 把先前的資料倒回 UI 介面
                    ui.StartPoint = startPoint;
                    ui.EndPoint = endPoint;
                    ui.StartConnector = startConnector;
                    ui.EndConnector = endConnector;
                    ui.StartElement = startElement;
                    ui.EndElement = endElement;
                    ui.RefreshUI();

                    bool? dialogResult = ui.ShowDialog(); // 開啟視窗，卡在這裡等使用者操作

                    if (dialogResult == true)
                    {
                        // 判斷使用者按了哪一顆按鈕
                        if (ui.ActionRequested == UnifiedRoutingUI.UserAction.PickStart)
                        {
                            try
                            {
                                // 安全地在 Revit 畫面中執行選取
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
                                // 使用者按了 ESC 取消選取，回到迴圈頂端重開視窗
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
                                // 使用者按了 ESC 取消選取
                            }
                        }
                        else if (ui.ActionRequested == UnifiedRoutingUI.UserAction.Run)
                        {
                            // 防呆檢查
                            if (startPoint == null || endPoint == null)
                            {
                                TaskDialog.Show("Routing", "Please select both start and end.");
                                continue; // 回到迴圈，繼續顯示視窗
                            }
                            if (startPoint.DistanceTo(endPoint) < 0.01)
                            {
                                TaskDialog.Show("Routing", "Start and End points are too close.");
                                continue;
                            }

                            // 跳出迴圈，準備生成管線
                            keepShowingUI = false; 
                        }
                    }
                    else
                    {
                        // 使用者按了 Cancel 或直接關閉視窗右上角的 X
                        return Result.Cancelled;
                    }
                }

                // ==========================================
                // 以下為正式執行生成管件的邏輯 (已脫離 UI 迴圈)
                // ==========================================
                if (CurrentMode == RoutingMode.Pipe)
                {
                    PipeRoutingEngine engine = new PipeRoutingEngine(doc);
                    List<XYZ> path = engine.GeneratePath(startPoint, endPoint);
                    CreatePipe(doc, path, startConnector, endConnector);
                }
                else
                {
                    ConduitRoutingEngine engine = new ConduitRoutingEngine(doc);
                    List<XYZ> path = engine.GeneratePath(startPoint, endPoint);

                    ConduitCreator creator = new ConduitCreator(doc);
                    creator.Create(path);

                    TaskDialog.Show("Routing Success", "Conduit routing completed.\nPoints: " + (path?.Count ?? 0));
                }

                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                TaskDialog.Show("Routing Error", ex.ToString());
                return Result.Failed;
            }
        }

        private void CreatePipe(Document doc, List<XYZ> path, Connector startConnector, Connector endConnector)
        {
            if (path == null || path.Count < 2) return;

            ElementId levelId = ElementId.InvalidElementId;
            ViewPlan activePlan = doc.ActiveView as ViewPlan;
            if (activePlan != null && activePlan.GenLevel != null) levelId = activePlan.GenLevel.Id;
            if (levelId == ElementId.InvalidElementId) levelId = RoutingUtils.GetFirstLevelId(doc);

            ElementId pipeTypeId = RoutingUtils.GetFirstPipeTypeId(doc);
            ElementId systemTypeId = RoutingUtils.GetFirstPipingSystemTypeId(doc);

            if (levelId == ElementId.InvalidElementId || pipeTypeId == ElementId.InvalidElementId || systemTypeId == ElementId.InvalidElementId)
            {
                TaskDialog.Show("Pipe Error", "Missing Level, PipeType, or PipingSystemType.");
                return;
            }

            int createdCount = 0;
            List<Autodesk.Revit.DB.Plumbing.Pipe> createdPipes = new List<Autodesk.Revit.DB.Plumbing.Pipe>();

            using (Transaction t = new Transaction(doc, "Pipe Routing"))
            {
                t.Start();

                for (int i = 0; i < path.Count - 1; i++)
                {
                    XYZ p1 = path[i];
                    XYZ p2 = path[i + 1];

                    if (p1 == null || p2 == null || p1.DistanceTo(p2) < 0.001) continue;

                    Autodesk.Revit.DB.Plumbing.Pipe pipe = Autodesk.Revit.DB.Plumbing.Pipe.Create(doc, systemTypeId, pipeTypeId, levelId, p1, p2);

                    if (pipe != null)
                    {
                        createdPipes.Add(pipe);
                        createdCount++;

                        Parameter dia = pipe.get_Parameter(BuiltInParameter.RBS_PIPE_DIAMETER_PARAM);
                        if (dia != null && !dia.IsReadOnly)
                        {
                            double diameter = UnitUtils.ConvertToInternalUnits(100, UnitTypeId.Millimeters);
                            dia.Set(diameter);
                        }
                    }
                }

                if (createdPipes.Count > 0)
                {
                    TryConnectPipeEnd(createdPipes[0], path[0], startConnector);
                    TryConnectPipeEnd(createdPipes[createdPipes.Count - 1], path[path.Count - 1], endConnector);
                }

                t.Commit();
            }
        }

        private void TryConnectPipeEnd(Autodesk.Revit.DB.Plumbing.Pipe pipe, XYZ point, Connector targetConnector)
        {
            if (pipe == null || point == null || targetConnector == null) return;

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

            if (bestPipeConnector == null) return;

            double tolerance = UnitUtils.ConvertToInternalUnits(5.0, UnitTypeId.Millimeters);
            if (bestPipeConnector.Origin.DistanceTo(targetConnector.Origin) > tolerance) return;

            try
            {
                bestPipeConnector.ConnectTo(targetConnector);
            }
            catch { }
        }
    }
}