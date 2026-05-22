MEPAutoRouting Unified UI Package

1) Copy all folders/files into your project root.
2) Edit MEPAutoRouting.addin:
   Replace YOUR_USER with your Windows user name, e.g. m.kwok.
3) Build:
   dotnet clean
   dotnet build
4) Copy bin\Debug\net48\MEPAutoRouting.dll to:
   C:\Users\<YOUR_USER>\AppData\Roaming\Autodesk\Revit\Addins\2024\
5) Copy MEPAutoRouting.addin to the same Addins\2024 folder.
6) Restart Revit.

Important:
- Remove old .addin files that still point to MEPAutoRouting.AutoRouteMEPCommand.
- The current routing path is a simple L-shape fallback. Core/AStarPathfinder.cs is ready for future true A* upgrade.
