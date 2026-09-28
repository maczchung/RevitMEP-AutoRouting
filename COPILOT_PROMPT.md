# 貼入 VS Code GitHub Copilot（Agent mode）

```
我已加入 Shared/RoutingFixes/ 同 Shared/Constraints/ 入面嘅 helper class
(MepFittingUtils, BoundingBoxUtils, PathUtils, SpaceVolume, SpacePicker,
WallObstacle, WallObstacleCollector, RoutingConstraints, RoutingOptions)，
全部係 namespace MEPAutoRouting。唔好改呢啲 helper。

請跟 APPLY_GUIDE.md 修改現有 code：

Part A (Bug fix)
1. Pipe creator：elbow loop 換成 MepFittingUtils.CreateElbows。
2. UnifiedRoutingCommand.cs：Conduit mode 用 PathUtils.IsValid 檢查空路徑。
3. ConduitCreator.cs：guard 空路徑、MergeCollinear、同一 transaction CreateElbows。
4. AStarPathfinder.cs：用 PathUtils.SixNeighbours、加 TurnPenalty、
   刪除 X→Y→Z 拆段改用 MergeCollinear。
5. GeometryExtractor.cs / RoutingVolumeUtils.cs：用 BoundingBoxUtils.GetWorldBounds。
6. csproj：冇用就刪 DetailAPI；RevitAPI/RevitAPIUI Private=False。

Part B (新功能)
7. UnifiedRoutingUI.xaml / .xaml.cs：套用 UI/UnifiedRoutingUI_Constraints.*.snippet。
8. UnifiedRoutingCommand.cs：套用 UI/UnifiedRoutingCommand_Integration.cs.snippet，
   將 placeholder (volumeMin, volumeMax, diameter, cellSize, startPoint, endPoint)
   對返現有變數；PickObject 要喺 dialog 關咗之後。
9. VoxelGrid.cs：套用 UI/VoxelGrid_ApplyConstraints.cs.snippet，對返欄位名；
   原本用 bbox block 牆嘅 code 跳過 Wall。
10. 完成訊息顯示：elbow 失敗清單、Space 名、牆間距 mm。

保留原本變數名同 public API。改完跑 dotnet build -c Release，
要求 0 error 0 warning，列出每個改動嘅 file 同行號。
```
