using Autodesk.Revit.DB;

namespace MEPAutoRouting.Shared
{
    /// <summary>Suppresses warnings (not errors) during routing so the user is not spammed with dialogs.</summary>
    public class WarningSwallower : IFailuresPreprocessor
    {
        public int Suppressed { get; private set; }

        public FailureProcessingResult PreprocessFailures(FailuresAccessor accessor)
        {
            foreach (var f in accessor.GetFailureMessages())
            {
                if (f.GetSeverity() == FailureSeverity.Warning)
                {
                    accessor.DeleteWarning(f);
                    Suppressed++;
                }
            }
            return FailureProcessingResult.Continue;
        }
    }
}
