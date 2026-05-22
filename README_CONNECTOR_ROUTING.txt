MEPAutoRouting Connector Routing Package

What changed:
- UI now selects the nearest connector on the selected element.
- If no connector is found, it falls back to the element LocationPoint / LocationCurve midpoint / bounding box center.
- Pipe mode attempts to connect the first/last pipe endpoints to selected connectors if coincident.
- Conduit mode uses connector origins as start/end when available, but does not yet create conduit fittings.

Install:
1) Replace project files with this package.
2) Edit MEPAutoRouting.addin and replace YOUR_USER with your real Windows user.
3) dotnet clean
4) dotnet build
5) Copy bin\Debug\net48\MEPAutoRouting.dll to Revit Addins 2024 folder.
6) Copy MEPAutoRouting.addin to the same folder.
7) Restart Revit.
