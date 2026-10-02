MEPAutoRouting v3 - Build & Deploy
===================================
Target     : Revit 2025 / net8.0-windows
Command    : MEPAutoRouting.UnifiedRoutingCommand (modeless window + ExternalEvent)
Algorithm  : A* 6-direction + start/goal direction rules + collinear merge + slope + auto elbow
Boundary   : None / Space / Room / Mass  (Host 或 Linked model)

Build   : dotnet build .\MEPAutoRouting.csproj -c Release
Deploy  : 關 Revit → powershell -ExecutionPolicy Bypass -File .\Deploy-Revit2025.ps1
Settings: %APPDATA%\MEPAutoRouting\settings.json

Notes
- RevitAPI / RevitAPIUI reference 要 Private=false
- Pipe / Conduit Type 要設定 Elbow
- 揀 Linked Space/Room/Mass：V/G → Revit Links → Custom → 開返該 category（Space/Room 要剔 Interior + Reference）
