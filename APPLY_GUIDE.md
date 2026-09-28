# MEPAutoRouting v2 – 套用指引 (Revit 2025 / net8.0-windows)

## 檔案放邊度
| Package | Project |
|---|---|
| `MEPAutoRouting.addin`, `README_BUILD.txt`, `Deploy-Revit2025.ps1` | root（覆蓋） |
| `Shared/RoutingFixes/*.cs` | `Shared/RoutingFixes/`（Bug fix helper） |
| `Shared/Constraints/*.cs` | `Shared/Constraints/`（新功能） |
| `UI/*.snippet` | **唔好 copy 入 project**，係要貼入現有檔案嘅 code |

全部 class 都係 `namespace MEPAutoRouting`。SDK-style csproj 會自動 include 新 `.cs`。

---

# Part A – Bug Fix（上次 7 項）

| # | 檔案 | 改法 |
|---|---|---|
| 1 | `.addin` | 已改好：Revit 2025、`<Text>`、相對路徑 |
| 2 | Pipe creator | elbow loop 換成 `MepFittingUtils.CreateElbows(doc, segments, path, failures)` |
| 3 | `UnifiedRoutingCommand.cs` ~L111 | Conduit mode 加 `if (!PathUtils.IsValid(path)) { TaskDialog...; return Result.Cancelled; }` |
| 4 | `ConduitCreator.cs` L22-31 | `MergeCollinear` → create 所有段 → 同一 transaction `CreateElbows` |
| 5 | `AStarPathfinder.cs` | neighbour 改 `PathUtils.SixNeighbours`；g-cost 加 `TurnPenalty`；L390-425 刪 X→Y→Z 拆段，改 `MergeCollinear` |
| 6 | `GeometryExtractor.cs` L34-58、`RoutingVolumeUtils.cs` L18-30 | 改用 `BoundingBoxUtils.GetWorldBounds(bb, linkTransform)`；刪除重複套 link transform |
| 7 | `.csproj` | 冇用到就刪 `DetailAPI` Reference；RevitAPI/RevitAPIUI `Private=False` |

---

# Part B – 新功能

## 功能 1：牆間距 (Wall clearance)
- 淨距定義：**牆面 → 管外皮**
- 內部計法：cell 中心去牆中心線距離 < `牆厚/2 + 間距 + 管半徑` 就 block
- 用中心線計，所以**斜牆 / 弧形牆都準**（唔會好似 bounding box 咁 block 咗成個長方形）
- 牆頂以上（例如天花 void 入面）唔受影響
- 起點 / 終點附近有 exempt 半徑，貼牆潔具嘅 connector 都行得出嚟
- Host + Linked model 嘅牆都計（可以用 checkbox 關）
- 上次輸入會記低（`settings.json`）

## 功能 2：Space / Room 用 Linked model
- 揀 Host 或 Linked model 嘅 **Space（MEP）或 Room（Architecture）**
- 用 boundary polygon 判斷，**L 形 Space 都啱**，唔係淨係用 bounding box
- Link 已經套 `GetTotalTransform()`
- 可選：Space 邊界都留同樣間距
- 後備方案：`SpacePicker.GetAllLinkedSpaces(doc)` 可以放入 ComboBox 俾用戶揀

## Step 1 – UI (`UnifiedRoutingUI.xaml` / `.xaml.cs`)
- 貼 `UI/UnifiedRoutingUI_Constraints.xaml.snippet` 入 XAML（Run button 上面）
- 貼 `UI/UnifiedRoutingUI_Constraints.cs.snippet` 入 code-behind
  - Constructor `InitializeComponent()` 之後 call `LoadConstraintOptions()`
  - Run/OK handler 入面 `if (!ReadConstraintOptions()) return;` 先至 `DialogResult = true`

## Step 2 – Command (`UnifiedRoutingCommand.cs`)
貼 `UI/UnifiedRoutingCommand_Integration.cs.snippet`，放喺 `ShowDialog() == true` 之後、建 VoxelGrid 之前。
改返以下 placeholder 做你原本嘅變數：`volumeMin`、`volumeMax`、`diameter`、`cellSize`、`startPoint`、`endPoint`。

> ⚠️ `PickObject` 一定要喺 modal window 關咗之後先 call，否則會出 exception。

## Step 3 – VoxelGrid (`VoxelGrid.cs`)
- 貼 `UI/VoxelGrid_ApplyConstraints.cs.snippet`，改返欄位名（`NX/NY/NZ`、`Blocked`、`CellToWorld`）
- 原本用 bbox block 牆嘅地方加 `if (e is Wall) continue;`（牆改由 constraints 處理）

## Step 4 – cellSize 建議
間距要大過 cellSize 先有效果。例如間距 50 mm，cellSize 最好 ≤ 50 mm。
Grid 太細會慢，所以用 Space 限制範圍會快好多。

---

# Revit 設定：揀唔到 Linked Space / Room？
1. V/G → **Revit Links** → 揀個 link → **Display Settings** → **Custom**
2. **Model Categories** → Spaces（或 Rooms）→ 剔 **Interior** 同 **Reference**
3. 喺 Plan view 將 mouse 放喺 Space 邊線 / 十字位置，按 **Tab** 切換到 linked Space 再 click
4. 仍然揀唔到就改用 `GetAllLinkedSpaces()` + ComboBox

---

# 測試清單
- [ ] `dotnet build -c Release` → 0 error / 0 warning
- [ ] 牆間距 0 / 50 / 100 mm：管同牆面距離正確（Measure 量淨距）
- [ ] 斜牆：管唔會被推到好遠
- [ ] 潔具貼牆：起點行得出嚟
- [ ] Host Space：L 形 Space 入面唔會 route 出去
- [ ] Linked Space：位置正確（link 有移位 / 旋轉都要試）
- [ ] 起點 / 終點唔喺 Space → 顯示提示
- [ ] Space Not Enclosed → 顯示提示
- [ ] Pipe / Conduit 轉角 elbow 接好
