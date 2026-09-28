# MEPAutoRouting – Bug Fix 套用指引 (Revit 2025 / net8.0-windows)

## Step 0 – Copy 檔案入 project
| 呢個 package | 放去 project |
|---|---|
| `MEPAutoRouting.addin` | 覆蓋 root 嘅 `MEPAutoRouting.addin` |
| `Shared/RoutingFixes/*.cs` | `Shared/RoutingFixes/` (新 folder) |
| `Deploy-Revit2025.ps1` | project root |
| `README_BUILD.txt` | 覆蓋 root 嘅 `README_BUILD.txt` |

SDK-style csproj 會自動 include 新 `.cs`，唔使改 csproj。
三個新 class 都係 `namespace MEPAutoRouting`，唔使加 `using`。

---

## Fix 1 – `.addin` (已完成)
路徑改 Revit 2025、`<Name>` → `<Text>`、相對 Assembly 路徑。
Deploy script 會自動刪走 `Addins\2024\MEPAutoRouting.addin`。

---

## Fix 2 – Pipe elbow connector 配錯 (Pipe creator)
搵 create elbow 嗰段 loop（通常有 `NewElbowFitting`），成段換做：

```csharp
// segments = List<MEPCurve>，由 path[0..n] 依次 create 出嚟
var failures = new List<string>();
int elbows = MepFittingUtils.CreateElbows(doc, segments, path, failures);
```
刪除舊有「用上一段端點搵下一段 connector」嘅 code。
如果 segments 係 `List<Pipe>`，用 `segments.Cast<MEPCurve>().ToList()`。

---

## Fix 3 – Conduit 空路徑顯示成功 (`UnifiedRoutingCommand.cs` ~L111-118)
喺 call `ConduitCreator.Create(path)` 之前加：

```csharp
if (!PathUtils.IsValid(path))
{
    TaskDialog.Show("Unified Routing", "搵唔到可行路徑，冇建立 conduit。");
    return Result.Cancelled;
}
```
`ConduitCreator.Create` 開頭都加 guard（避免開空 transaction）：
```csharp
if (!PathUtils.IsValid(path)) return 0;
```

---

## Fix 4 – Conduit 冇 elbow (`ConduitCreator.cs` L22-31)
參考寫法（保留你原本嘅 conduitTypeId / levelId / diameter 變數名）：

```csharp
public static int Create(Document doc, IList<XYZ> rawPath, ElementId conduitTypeId,
                         ElementId levelId, double diameter, IList<string> failures)
{
    List<XYZ> path = PathUtils.MergeCollinear(rawPath);
    if (!PathUtils.IsValid(path)) return 0;

    using var tx = new Transaction(doc, "Auto Route Conduit");
    tx.Start();

    var segments = new List<MEPCurve>();
    for (int i = 0; i < path.Count - 1; i++)
    {
        Conduit c = Conduit.Create(doc, conduitTypeId, path[i], path[i + 1], levelId);
        c.get_Parameter(BuiltInParameter.RBS_CONDUIT_DIAMETER_PARAM)?.Set(diameter);
        segments.Add(c);
    }

    MepFittingUtils.CreateElbows(doc, segments, path, failures);
    tx.Commit();
    return segments.Count;
}
```
Conduit Type 要喺 Type Properties 設定 Elbow。

---

## Fix 5 – A* 拆 X→Y→Z 產生未驗證線段 (`AStarPathfinder.cs`)
**5a. Neighbour 改 6 方向** – 搵生成 neighbour 嘅地方（通常係三層 `for dx/dy/dz` -1..1），換成：
```csharp
foreach (var (dx, dy, dz) in PathUtils.SixNeighbours)
{
    int nx = cur.X + dx, ny = cur.Y + dy, nz = cur.Z + dz;
    // ... 原本嘅 bounds / obstacle / cost 邏輯
}
```
**5b. (可選) Turn penalty** – g-cost 加：
```csharp
double g = cur.G + stepCost + PathUtils.TurnPenalty(cur.Dir, (dx, dy, dz));
```
（Node 要多一個 `Dir` 欄位記低上一步方向；起點用 `(0,0,0)`。）

**5c. 路徑重建 (L390-425)** – 刪除「每個格點拆成 X→Y→Z」嗰段，改做：
```csharp
List<XYZ> world = cells.Select(grid.CellToWorld).ToList();  // 用你原本嘅轉換 function
return PathUtils.MergeCollinear(world);
```
6 方向下每步已經係單軸，唔使再拆；`PathHitsObstacle` 可以保留做 double-check。

---

## Fix 6 – BoundingBox Transform (`GeometryExtractor.cs` L34-58, `RoutingVolumeUtils.cs` L18-30)
將直接用 `bb.Min` / `bb.Max` 嘅地方改做：
```csharp
var (min, max) = BoundingBoxUtils.GetWorldBounds(bb);                 // host model
var (min, max) = BoundingBoxUtils.GetWorldBounds(bb, linkTransform);  // linked model
```
Linked model 要刪走原本自己套 link transform 嘅 code，否則會 transform 兩次。

---

## Fix 7 – `DetailAPI.dll` warning (`MEPAutoRouting.csproj`)
全個 solution 搜尋 `DetailAPI`：如果冇 code 用到，刪除：
```xml
<Reference Include="DetailAPI"> ... </Reference>
```
順手確認 Revit reference：
```xml
<Reference Include="RevitAPI">
  <HintPath>C:\Program Files\Autodesk\Revit 2025\RevitAPI.dll</HintPath>
  <Private>False</Private>
</Reference>
<Reference Include="RevitAPIUI">
  <HintPath>C:\Program Files\Autodesk\Revit 2025\RevitAPIUI.dll</HintPath>
  <Private>False</Private>
</Reference>
```

---

## 驗證
1. `dotnet build -c Release` → 0 error / 0 warning
2. `.\Deploy-Revit2025.ps1`
3. Revit 2025 測試：
   - [ ] Pipe：有轉角 → elbow 全部接好
   - [ ] Conduit：有轉角 → elbow 接好
   - [ ] 起點/終點被完全包住 → 顯示「搵唔到可行路徑」，冇 create 任何嘢
   - [ ] 旋轉咗嘅 Generic Model / 柱做障礙 → 唔會穿過
   - [ ] Linked model 障礙物位置正確
