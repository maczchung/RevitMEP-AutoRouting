using System.Collections.Generic;
using Autodesk.Revit.DB;

namespace MEPAutoRouting
{
    /// <summary>
    /// 收集 Transaction commit 時 Revit 出嘅 warning / error。
    /// Revit error 會令成個 transaction rollback（冇 pipe、又冇提示）→ 而家會寫入 PROBLEMS。
    /// </summary>
    public sealed class RouteFailureCollector : IFailuresPreprocessor
    {
        public List<string> Warnings { get; } = new();
        public List<string> Errors { get; } = new();
        public bool DeleteWarnings { get; }

        public RouteFailureCollector(bool deleteWarnings) => DeleteWarnings = deleteWarnings;

        public FailureProcessingResult PreprocessFailures(FailuresAccessor fa)
        {
            foreach (FailureMessageAccessor f in fa.GetFailureMessages())
            {
                string msg = f.GetDescriptionText();
                if (f.GetSeverity() == FailureSeverity.Warning)
                {
                    Warnings.Add(msg);
                    if (DeleteWarnings) fa.DeleteWarning(f);
                }
                else
                {
                    Errors.Add(msg);
                }
            }
            return Errors.Count > 0 ? FailureProcessingResult.ProceedWithRollBack : FailureProcessingResult.Continue;
        }

        /// <summary>Transaction.Start() 之後、Commit() 之前 call。</summary>
        public static RouteFailureCollector Attach(Transaction tx, bool deleteWarnings)
        {
            var c = new RouteFailureCollector(deleteWarnings);
            FailureHandlingOptions o = tx.GetFailureHandlingOptions();
            o.SetFailuresPreprocessor(c);
            o.SetClearAfterRollback(true);
            tx.SetFailureHandlingOptions(o);
            return c;
        }
    }
}
