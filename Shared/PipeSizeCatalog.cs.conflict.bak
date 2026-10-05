using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Electrical;
using Autodesk.Revit.DB.Plumbing;

namespace MEPAutoRouting
{
    /// <summary>一個可用 size：Nominal 用嚟 set 參數，Outer 用嚟計牆間距。單位 mm。</summary>
    public readonly record struct PipeSizeInfo(double NominalMm, double OuterMm)
    {
        public double OuterRadiusFt => Geom.MmToFt((OuterMm > 0 ? OuterMm : NominalMm) / 2.0);
        public double NominalFt => Geom.MmToFt(NominalMm);
    }

    /// <summary>
<<<<<<< HEAD
    /// 讀 Pipe / Conduit Type 嘅可用 size。要喺 Revit API context 入面 call。
    /// v4.2：逐條 rule / standard try-catch；讀唔到就返回空 list（UI 會容許自由輸入），唔會 throw。
=======
    /// 讀 Pipe Type（Routing Preferences → Segments）或者 Conduit Type（Electrical Settings）嘅可用 size。
    /// 要喺 Revit API context 入面 call（經 RevitActionQueue）。
>>>>>>> 6dd4d98ac757c36819d8fe104c6a91a62610d7a2
    /// </summary>
    public static class PipeSizeCatalog
    {
        public static List<PipeSizeInfo> GetPipeSizes(Document doc, ElementId pipeTypeId)
        {
            var map = new SortedDictionary<double, PipeSizeInfo>();
<<<<<<< HEAD
            try
            {
                if (doc?.GetElement(pipeTypeId) is not PipeType pt) return new List<PipeSizeInfo>();
                RoutingPreferenceManager rpm = pt.RoutingPreferenceManager;
                if (rpm == null) return new List<PipeSizeInfo>();

                int n = rpm.GetNumberOfRules(RoutingPreferenceRuleGroupType.Segments);
                for (int i = 0; i < n; i++)
                {
                    try
                    {
                        RoutingPreferenceRule rule = rpm.GetRule(RoutingPreferenceRuleGroupType.Segments, i);
                        if (doc.GetElement(rule.MEPPartId) is not PipeSegment seg) continue;
                        foreach (MEPSize s in seg.GetSizes())
                        {
                            if (!s.UsedInSizeLists) continue;
                            double nom = Math.Round(Geom.FtToMm(s.NominalDiameter), 1);
                            map.TryAdd(nom, new PipeSizeInfo(nom, Math.Round(Geom.FtToMm(s.OuterDiameter), 1)));
                        }
                    }
                    catch (Exception ex) { RevitActionQueue.WriteErrorFile($"PipeSizeCatalog: segment rule {i} skipped – {ex.Message}", ex); }
                }
            }
            catch (Exception ex) { RevitActionQueue.WriteErrorFile("PipeSizeCatalog.GetPipeSizes failed – " + ex.Message, ex); }
=======
            if (doc?.GetElement(pipeTypeId) is not PipeType pt) return new List<PipeSizeInfo>();

            RoutingPreferenceManager rpm = pt.RoutingPreferenceManager;
            int n = rpm.GetNumberOfRules(RoutingPreferenceRuleGroupType.Segments);
            for (int i = 0; i < n; i++)
            {
                RoutingPreferenceRule rule = rpm.GetRule(RoutingPreferenceRuleGroupType.Segments, i);
                if (doc.GetElement(rule.MEPPartId) is not PipeSegment seg) continue;
                foreach (MEPSize s in seg.GetSizes())
                {
                    if (!s.UsedInSizeLists) continue;
                    double nom = Math.Round(Geom.FtToMm(s.NominalDiameter), 1);
                    map.TryAdd(nom, new PipeSizeInfo(nom, Math.Round(Geom.FtToMm(s.OuterDiameter), 1)));
                }
            }
>>>>>>> 6dd4d98ac757c36819d8fe104c6a91a62610d7a2
            return map.Values.ToList();
        }

        public static List<PipeSizeInfo> GetConduitSizes(Document doc, ElementId conduitTypeId)
        {
            var map = new SortedDictionary<double, PipeSizeInfo>();
<<<<<<< HEAD
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
=======
            if (doc == null) return new List<PipeSizeInfo>();

            string standard = (doc.GetElement(conduitTypeId) as ConduitType)?.LookupParameter("Standard")?.AsValueString();
            ConduitSizeSettings settings = ConduitSizeSettings.GetConduitSizeSettings(doc);
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
>>>>>>> 6dd4d98ac757c36819d8fe104c6a91a62610d7a2
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
