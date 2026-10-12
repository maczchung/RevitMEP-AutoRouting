# Daily Handover Report – GitHub Actions 安裝及使用指南

每日 **05:00 HKT** 自動產生接手報告，存入 `reports/`，即使 Chat 遺失都可以即刻接手。

---

## 1. 檔案結構（複製到 Repo 根目錄）

```text
MEPAutoRouting/                     ← 你的 Repo 根目錄
├─ .github/workflows/daily-handover.yml   ← 排程 (05:00 HKT)
├─ scripts/Generate-Handover.ps1          ← 產生報告
├─ docs/HANDOVER_NOTES.md                 ← 你每日手動更新
├─ .vscode/tasks.json                     ← 本機一鍵產生（可選）
├─ reports/                               ← 報告輸出（自動）
│   ├─ LATEST.md
│   └─ 2026/Handover_2026-10-10.md
└─ VERSION                                ← 例如 4.8.3
```

將 `.gitignore.handover-snippet` 內容（`build.log`）加入你現有嘅 `.gitignore`。

---

## 2. 第一次設定（約 10 分鐘）

### Step 1 – 修改 workflow 設定
打開 `.github/workflows/daily-handover.yml`，改 `env:` 區：

| 參數 | 說明 |
|---|---|
| `SOLUTION_PATH` | 你的 `.sln` 或 `.csproj` 相對路徑 |
| `DEFAULT_BUILD_MODE` | `dotnet`（SDK-style csproj）/ `msbuild`（舊式 csproj）/ `skip`（唔 build）|
| `DOTNET_VERSION` | Revit 2025 = `8.0.x` |

> ⚠ **Revit API DLL 問題**：GitHub 雲端機冇裝 Revit，如果 csproj 用 `HintPath` 指向
> `C:\Program Files\Autodesk\Revit 2025\RevitAPI.dll`，雲端 build 會失敗。
> 解決方法二選一：
> 1. 改用 NuGet 參考（例如 `Nice3point.Revit.Api.RevitAPI` / `RevitAPIUI`，版本 2025.*，設 `ExcludeAssets=runtime`）
> 2. 將 `DEFAULT_BUILD_MODE` 設為 `skip` —— 報告照樣產生，只係 Build 一欄顯示 SKIPPED
>
> Build 失敗唔會阻止報告產生，只會喺報告顯示 ❌ FAIL 同錯誤訊息。

### Step 2 – Commit & Push 到 default branch（通常 `main`）
```bash
git add .github scripts docs .vscode reports VERSION .gitignore
git commit -m "Add daily handover automation"
git push
```
> 排程 workflow **只會喺 default branch 運行**。

### Step 3 – 確認權限
GitHub Repo → **Settings → Actions → General**
- Actions permissions：Allow all actions
- **Workflow permissions：Read and write permissions** ✅

如果 `main` 有 Branch Protection（禁止直接 push），Bot 會 push 失敗 → 需要喺 ruleset 加 bypass，或者將報告改存 artifact（workflow 已經同時上載 artifact 做備份）。

### Step 4 – 手動測試一次
1. Repo → **Actions** tab
2. 左邊揀 **Daily Handover Report**
3. 右邊按 **Run workflow** → 可揀 `build_mode` 同 `hours`（例如 `168` = 過去 7 日）
4. 完成後：
   - 打開 `reports/LATEST.md` 睇報告
   - 或者點入該次 run → **Summary** 頁面直接顯示完整報告

---

## 3. 每日用法

### 收工前（2 分鐘）
1. 更新 `docs/HANDOVER_NOTES.md`（Today / Known Issues / Next Priority / Test Results）
2. 版本有變就改 `VERSION`
3. Commit + Push：
```bash
git add .
git commit -m "feat: wall avoidance for curved walls"
git push
```

### 05:00 HKT 自動執行
```text
Checkout → Build (Release) → 收集 Git 記錄 / 修改檔案 / TODO / Issues
        → 合併 HANDOVER_NOTES.md → 寫入 reports/ → 自動 commit "[handover]"
```

### Chat 遺失時
1. 打開 `reports/LATEST.md`
2. 複製第 **13 節 Recovery Prompt**
3. 貼入新 Copilot Chat → 即可繼續開發

---

## 4. 報告內容

| 節 | 內容 | 來源 |
|---|---|---|
| 摘要表 | 日期、版本、Branch、最新 Commit、Build、活動量 | 自動 |
| 1–2 | Status / Environment | HANDOVER_NOTES |
| 3 | 過去 24 小時 Commits | Git 自動 |
| 4 | Added / Modified / Deleted 檔案 | Git 自動 |
| 5 | Build 結果、Errors、Warnings、錯誤行 | Build log 自動 |
| 6–9 | Today、Completed、Test Results、Known Issues | HANDOVER_NOTES |
| 9 | Open GitHub Issues | GitHub 自動 |
| 10 | 程式碼內 TODO / FIXME / HACK | 自動掃描 |
| 11–12 | Next Priority、Design Decisions | HANDOVER_NOTES |
| 13 | Recovery Prompt | 自動組合 |

> 💡 喺 code 寫 `// TODO: handle sloped ceiling` 會自動出現喺報告第 10 節。

---

## 5. 本機產生報告（唔使等 05:00）

VS Code：`Ctrl+Shift+P` → **Tasks: Run Task** → 揀
- `Handover: Build + Generate Report (local)`
- `Handover: Generate Report only (no build)`

或者 Terminal：
```powershell
./scripts/Generate-Handover.ps1
./scripts/Generate-Handover.ps1 -Hours 72        # 過去 3 日
```
> 如果電腦冇 PowerShell 7（`pwsh`），將 `tasks.json` 入面 `"pwsh"` 改做 `"powershell"`。
> 如果話 script 被封鎖：`Set-ExecutionPolicy -Scope CurrentUser RemoteSigned`

---

## 6. 注意事項

| 項目 | 說明 |
|---|---|
| 時間誤差 | GitHub 排程高峰期可能延遲幾分鐘至半小時，屬正常 |
| 自動停用 | Public repo 60 日冇活動，GitHub 會停用排程；收到通知後喺 Actions 頁面重新 Enable 即可 |
| 用量 | Private repo 用 Windows runner 會以較高倍率扣 Actions 分鐘；用 `skip` 模式可縮短時間 |
| 唔會無限循環 | 報告 commit 帶 `[skip ci]`，而且 `GITHUB_TOKEN` push 唔會觸發其他 workflow |
| 修改時間 | 改 cron：`'0 21 * * *'`（UTC）。HKT = UTC+8，例如 08:00 HKT → `'0 0 * * *'` |
| 公司政策 | 如 Repo 屬 Cundall 組織，請確認 Actions 已獲 IT 批准啟用 |
