using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Electrical;
using Autodesk.Revit.DB.Plumbing;

namespace MEPAutoRouting
{
    /// <summary>One available size. Nominal sets the parameter, Outer is used for clearance. Units: mm.</summary>
    public readonly record struct PipeSizeInfo(double NominalMm, double OuterMm)
    {
        public double OuterRadiusFt => Geom.MmToFt((OuterMm > 0 ? OuterMm : NominalMm) / 2.0);
        public double NominalFt => Geom.MmToFt(NominalMm);
    }

    /// <summary>
    /// Reads the available sizes of a Pipe / Conduit type. Must be called inside a Revit API context.
    /// v4.2: per-rule try/catch; returns an empty list on failure (UI allows free input), never throws.
    /// v4.8.3: every segment rule is read; if no size is flagged "Used in size lists", all sizes are used;
    ///         optional log reports the segment name(s) and size count.
    /// </summary>
    public static class PipeSizeCatalog
    {
        public static List<PipeSizeInfo> GetPipeSizes(Document doc, ElementId pipeTypeId)
            => GetPipeSizes(doc, pipeTypeId, null);

        public static List<PipeSizeInfo> GetPipeSizes(Document doc, ElementId pipeTypeId, Action<string> log)
        {
            var used = new SortedDictionary<double, PipeSizeInfo>();
            var all = new SortedDictionary<double, PipeSizeInfo>();
            var segmentNames = new List<string>();
            try
            {
                if (doc?.GetElement(pipeTypeId) is not PipeType pt)
                {
                    log?.Invoke($"Size catalog: element {pipeTypeId} is not a pipe type.");
                    return new List<PipeSizeInfo>();
                }
                RoutingPreferenceManager rpm = pt.RoutingPreferenceManager;
                if (rpm == null)
                {
                    log?.Invoke($"Size catalog: '{pt.Name}' has no routing preferences.");
                    return new List<PipeSizeInfo>();
                }

                int n = rpm.GetNumberOfRules(RoutingPreferenceRuleGroupType.Segments);
                for (int i = 0; i < n; i++)
                {
                    try
                    {
                        RoutingPreferenceRule rule = rpm.GetRule(RoutingPreferenceRuleGroupType.Segments, i);
                        if (doc.GetElement(rule.MEPPartId) is not PipeSegment seg)
                        {
                            log?.Invoke($"Size catalog: segment rule {i} of '{pt.Name}' has no pipe segment (id {rule.MEPPartId}).");
                            continue;
                        }
                        segmentNames.Add(seg.Name);
                        foreach (MEPSize s in seg.GetSizes())
                        {
                            double nom = Math.Round(Geom.FtToMm(s.NominalDiameter), 1);
                            var info = new PipeSizeInfo(nom, Math.Round(Geom.FtToMm(s.OuterDiameter), 1));
                            all.TryAdd(nom, info);
                            if (s.UsedInSizeLists) used.TryAdd(nom, info);
                        }
                    }
                    catch (Exception ex) { RevitActionQueue.WriteErrorFile($"PipeSizeCatalog: segment rule {i} skipped – {ex.Message}", ex); }
                }
            }
            catch (Exception ex) { RevitActionQueue.WriteErrorFile("PipeSizeCatalog.GetPipeSizes failed – " + ex.Message, ex); }

            bool fallback = used.Count == 0 && all.Count > 0;
            List<PipeSizeInfo> result = (fallback ? all : used).Values.ToList();
            log?.Invoke($"Size catalog: {result.Count} size(s) from segment(s) " +
                        $"{(segmentNames.Count == 0 ? "-" : string.Join(", ", segmentNames))}" +
                        $"{(fallback ? " (no size flagged 'Used in size lists' – all sizes used)" : "")}.");
            return result;
        }

        public static List<PipeSizeInfo> GetConduitSizes(Document doc, ElementId conduitTypeId)
        {
            var map = new SortedDictionary<double, PipeSizeInfo>();
            try
            {
                if (doc == null) return new List<PipeSizeInfo>();
                string standard = (doc.GetElement(conduitTypeId) as ConduitType)?.LookupParameter("Standard")?.AsValueString();
                ConduitSizeSettings settings = ConduitSizeSettings.GetConduitSizeSettings(doc);
                if (settings == null) return new List<PipeSizeInfo>();
                bool matched = standard != null && settings.Any(kv => kv.Key == standard);

                foreach (KeyValuePair<string, ConduitSizes> kv in settings)
                {
                    if (matched && kv.Key != standard) continue;
                    foreach (ConduitSize cs in kv.Value)
                    {
                        if (!cs.UsedInSizeLists) continue;
                        double nom = Math.Round(Geom.FtToMm(cs.NominalDiameter), 1);
                        map.TryAdd(nom, new PipeSizeInfo(nom, Math.Round(Geom.FtToMm(cs.OuterDiameter), 1)));
                    }
                }
            }
            catch (Exception ex) { RevitActionQueue.WriteErrorFile("PipeSizeCatalog.GetConduitSizes failed – " + ex.Message, ex); }
            return map.Values.ToList();
        }

        public static PipeSizeInfo Snap(double mm, IList<PipeSizeInfo> sizes, out bool exact)
        {
            if (sizes == null || sizes.Count == 0) { exact = true; return new PipeSizeInfo(mm, mm); }
            PipeSizeInfo best = sizes.OrderBy(s => Math.Abs(s.NominalMm - mm)).First();
            exact = Math.Abs(best.NominalMm - mm) < 0.05;
            return best;
        }
    }
}
