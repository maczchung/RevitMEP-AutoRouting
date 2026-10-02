using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;

namespace MEPAutoRouting
{
    public enum ProblemSeverity { Warning, Error }

    /// <summary>Warning 照樣可以 Route；Error 會 block commit。訊息全部英文。</summary>
    public sealed record RouteProblem(ProblemSeverity Severity, string Message, int? PointIndex = null)
    {
        public bool IsError => Severity == ProblemSeverity.Error;
        public string Icon => IsError ? "⛔" : "⚠";
        public string SeverityText => IsError ? "Error" : "Warning";
        public override string ToString() => $"{Icon} {Message}";

        public static RouteProblem Error(string m, int? i = null) => new(ProblemSeverity.Error, m, i);
        public static RouteProblem Warn(string m, int? i = null) => new(ProblemSeverity.Warning, m, i);
    }

    public static class PathValidator
    {
        public static List<RouteProblem> Validate(IList<XYZ> path, double minSegmentFt,
                                                  Func<XYZ, bool> isBlocked = null, double sampleStepFt = 0)
        {
            var list = new List<RouteProblem>();
            if (!PathUtils.IsValid(path)) { list.Add(RouteProblem.Error("No valid path found.")); return list; }

            for (int i = 0; i < path.Count - 1; i++)
            {
                double len = path[i].DistanceTo(path[i + 1]);
                if (len < 1e-6) { list.Add(RouteProblem.Error($"Segment {i} has zero length.", i)); continue; }

                bool isLead = i == 0 || i == path.Count - 2;
                if (!isLead && len < minSegmentFt)
                    list.Add(RouteProblem.Warn(
                        $"Segment {i} is only {Geom.FtToMm(len):0} mm long – the elbow may not fit.", i));

                if (i < path.Count - 2)
                {
                    XYZ d1 = (path[i + 1] - path[i]).Normalize();
                    XYZ d2 = (path[i + 2] - path[i + 1]).Normalize();
                    if (d1.DotProduct(d2) < -0.999)
                        list.Add(RouteProblem.Error(
                            $"U-turn at point {i + 1} – the path reverses direction and no elbow can be created.", i + 1));
                }

                if (isBlocked != null && sampleStepFt > 0 && !isLead)
                {
                    int n = Math.Max(1, (int)Math.Ceiling(len / sampleStepFt));
                    for (int k = 1; k < n; k++)
                    {
                        XYZ p = path[i] + (path[i + 1] - path[i]) * ((double)k / n);
                        if (isBlocked(p))
                        {
                            list.Add(RouteProblem.Error(
                                $"Segment {i} hits an obstacle or leaves the calculation boundary.", i));
                            break;
                        }
                    }
                }
            }
            return list;
        }

        public static bool HasErrors(IEnumerable<RouteProblem> problems) => problems?.Any(p => p.IsError) == true;
    }
}
