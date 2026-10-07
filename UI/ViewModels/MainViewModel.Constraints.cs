// =====================================================================================
//  MainViewModel.Constraints.cs  (v4.2)
//  Slope / Wall clearance / Calculation boundary (manual pick) / Pipe size / Route guard
//  v4.2 修正：
//   • InitConstraintUi 只會行一次（由 MainWindow.Loaded 觸發；constructor 唔好再 call）
//   • 由 Revit thread 返 UI 一律用 BeginInvoke（唔再同步 Invoke → 避免 ItemsControl crash）
//   • Load sizes / Pick boundary 全部 try-catch，失敗寫 OUTPUT + error.log
// =====================================================================================
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.Windows.Input;
using System.Windows.Threading;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Plumbing;

namespace MEPAutoRouting.UI.ViewModels
{
    public partial class MainViewModel
    {
        // ------------------------------------------------------------------ hooks（喺 MainViewModel.cs 實作）
        private partial void Notify(string propertyName);
        private partial ElementId GetSelectedTypeId();
        private partial bool IsConduitDiscipline();
        private partial PipingSystemType GetSelectedPipingSystemType();

        // ------------------------------------------------------------------ infra
        public RevitActionQueue ActionQueue { get; set; }
        private readonly Dispatcher _uiDispatcher = Dispatcher.CurrentDispatcher;
        private bool _constraintUiReady;
        private ElementId _lastSystemTypeId;
        private List<PipeSizeInfo> _sizes = new();

        private static readonly string[] ConstraintUiProperties =
        {
            nameof(SlopeEnabled), nameof(SlopeValue), nameof(SlopeUnit), nameof(SlopeFlow), nameof(SlopeText),
            nameof(WallClearanceMm), nameof(IncludeLinkWalls), nameof(ClearanceOnBoundary), nameof(RegionMarginMm),
            nameof(Boundary), nameof(HasBoundary), nameof(BoundaryText), nameof(BoundaryDetailText), nameof(BoundarySummaryText),
            nameof(PipeSizeText), nameof(SelectedListSize), nameof(IsSizeInputEnabled), nameof(SizeSummaryText),
            nameof(RouteBlockedReason)
        };

        /// <summary>由 MainWindow.Loaded call 一次。重複 call 會被忽略。</summary>
        public void InitConstraintUi()
        {
            if (_constraintUiReady) return;
            PropertyChanged -= OnConstraintVmPropertyChanged;
            PropertyChanged += OnConstraintVmPropertyChanged;
            _lastSystemTypeId = GetSelectedPipingSystemType()?.Id;
            _constraintUiReady = true;
            NotifyConstraintUi();
            ReloadSizes();
        }

        /// <summary>由 Revit thread（ExternalEvent）返 UI：一律非同步，失敗唔會 throw。</summary>
        private void PostToUi(Action a, string what)
        {
            try
            {
                _uiDispatcher.BeginInvoke(new Action(() =>
                {
                    try { a(); }
                    catch (Exception ex)
                    {
                        RevitActionQueue.WriteErrorFile($"{what} (UI update) failed – {ex.Message}", ex);
                        try { Log(Routing.LogLevel.Error, $"{what} failed – {ex.GetType().Name}: {ex.Message}"); } catch { }
                    }
                }));
            }
            catch (Exception ex) { RevitActionQueue.WriteErrorFile($"{what}: could not post to UI – {ex.Message}", ex); }
        }

        private void NotifyConstraintUi()
        {
            foreach (string p in ConstraintUiProperties) Notify(p);
        }

        private void OnConstraintVmPropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            switch (e.PropertyName)
            {
                case "SelectedType":
                case "SelectedDiscipline":
                    ReloadSizes();
                    break;
                case "SelectedSystemType":
                    HandleSystemTypeChanged();
                    break;
                case "MatchSize":
                    Notify(nameof(IsSizeInputEnabled));
                    Notify(nameof(SizeSummaryText));
                    Notify(nameof(RouteBlockedReason));
                    CommandManager.InvalidateRequerySuggested();
                    break;
                case "Source":
                case "Target":
                case "IsBusy":
                    Notify(nameof(RouteBlockedReason));
                    CommandManager.InvalidateRequerySuggested();
                    break;
            }
        }

        public void ResetConstraintUi()
        {
            var d = new RoutingOptions();
            ConstraintOptions.WallClearanceMm = d.WallClearanceMm;
            ConstraintOptions.IncludeLinkWalls = d.IncludeLinkWalls;
            ConstraintOptions.ClearanceOnBoundary = d.ClearanceOnBoundary;
            ConstraintOptions.RegionMarginMm = d.RegionMarginMm;
            ConstraintOptions.PipeSizeMm = d.PipeSizeMm;
            ConstraintOptions.Slope = new SlopeSettings();
            Boundary = null;
            NotifyConstraintUi();
        }

        // ================================================================== SLOPE
        public bool SlopeEnabled
        {
            get => ConstraintOptions.Slope.Enabled;
            set
            {
                if (ConstraintOptions.Slope.Enabled == value) return;
                ConstraintOptions.Slope.Enabled = value;
                Notify(nameof(SlopeEnabled)); Notify(nameof(SlopeText));
            }
        }

        public double SlopeValue
        {
            get => ConstraintOptions.Slope.Value;
            set
            {
                if (!SlopeSettings.TryParse(value.ToString(CultureInfo.InvariantCulture), SlopeUnit, out double v, out string err))
                {
                    Log(Routing.LogLevel.Warn, err);
                    Notify(nameof(SlopeValue));
                    return;
                }
                ConstraintOptions.Slope.Value = v;
                Notify(nameof(SlopeValue)); Notify(nameof(SlopeText));
            }
        }

        public SlopeUnit SlopeUnit
        {
            get => ConstraintOptions.Slope.Unit;
            set
            {
                if (ConstraintOptions.Slope.Unit == value) return;
                double g = ConstraintOptions.Slope.Gradient;
                ConstraintOptions.Slope.Unit = value;
                if (g > 0) ConstraintOptions.Slope.Value = Math.Round(value == SlopeUnit.Percent ? g * 100 : 1 / g, 2);
                Notify(nameof(SlopeUnit)); Notify(nameof(SlopeValue)); Notify(nameof(SlopeText));
            }
        }

        public FlowDirection SlopeFlow
        {
            get => ConstraintOptions.Slope.Flow;
            set { ConstraintOptions.Slope.Flow = value; Notify(nameof(SlopeFlow)); }
        }

        public string SlopeText => ConstraintOptions.Slope.Display;

        private void HandleSystemTypeChanged()
        {
            if (!_constraintUiReady) return;
            PipingSystemType st = GetSelectedPipingSystemType();
            if (st == null || st.Id == _lastSystemTypeId) return;
            _lastSystemTypeId = st.Id;

            bool gravity = SlopeSettings.IsGravitySystem(st);
            if (gravity == SlopeEnabled) return;
            SlopeEnabled = gravity;
            Log(Routing.LogLevel.Info, gravity
                ? $"'{st.Name}' is a gravity system – slope turned on ({SlopeText})."
                : $"'{st.Name}' is not a gravity system – slope turned off.");
        }

        // ================================================================== WALL CLEARANCE
        public double WallClearanceMm
        {
            get => ConstraintOptions.WallClearanceMm;
            set
            {
                if (value < 0 || value > 2000)
                {
                    Log(Routing.LogLevel.Warn, "Wall clearance must be between 0 and 2000 mm.");
                    Notify(nameof(WallClearanceMm));
                    return;
                }
                ConstraintOptions.WallClearanceMm = value;
                Notify(nameof(WallClearanceMm));
            }
        }

        public bool IncludeLinkWalls
        {
            get => ConstraintOptions.IncludeLinkWalls;
            set { ConstraintOptions.IncludeLinkWalls = value; Notify(nameof(IncludeLinkWalls)); }
        }

        public double RegionMarginMm
        {
            get => ConstraintOptions.RegionMarginMm;
            set
            {
                if (value < 0 || value > 20000)
                {
                    Log(Routing.LogLevel.Warn, "Region margin must be between 0 and 20000 mm.");
                    Notify(nameof(RegionMarginMm));
                    return;
                }
                ConstraintOptions.RegionMarginMm = value;
                Notify(nameof(RegionMarginMm));
            }
        }

        public bool ClearanceOnBoundary
        {
            get => ConstraintOptions.ClearanceOnBoundary;
            set { ConstraintOptions.ClearanceOnBoundary = value; Notify(nameof(ClearanceOnBoundary)); }
        }

        // ================================================================== CALCULATION BOUNDARY（手動 Pick）
        private IRoutingBoundary _boundary;
        public IRoutingBoundary Boundary
        {
            get => _boundary;
            set
            {
                _boundary = value;
                Notify(nameof(Boundary)); Notify(nameof(HasBoundary));
                Notify(nameof(BoundaryText)); Notify(nameof(BoundaryDetailText)); Notify(nameof(BoundarySummaryText));
            }
        }

        public bool HasBoundary => _boundary != null;

        public string BoundaryText => _boundary == null
            ? "No boundary – routing volume is used"
            : $"{_boundary.Kind} · {_boundary.Name}";

        public string BoundaryDetailText
        {
            get
            {
                if (_boundary == null) return "Pick a Space, Room or Mass to limit the route.";
                double w = Geom.FtToMm(_boundary.Max.X - _boundary.Min.X);
                double d = Geom.FtToMm(_boundary.Max.Y - _boundary.Min.Y);
                double h = Geom.FtToMm(_boundary.Max.Z - _boundary.Min.Z);
                return $"{(_boundary.IsFromLink ? "Linked" : "Host")} · {w:0} × {d:0} × {h:0} mm";
            }
        }

        public string BoundarySummaryText => _boundary == null ? "None" : $"{_boundary.Kind} {_boundary.Name}";

        private ICommand _pickBoundaryCommand, _pickLinkedBoundaryCommand, _clearBoundaryCommand;
        public ICommand PickBoundaryCommand =>
            _pickBoundaryCommand ??= new global::MEPAutoRouting.Shared.RelayCommand(() => PickBoundary(false));
        public ICommand PickLinkedBoundaryCommand =>
            _pickLinkedBoundaryCommand ??= new global::MEPAutoRouting.Shared.RelayCommand(() => PickBoundary(true));
        public ICommand ClearBoundaryCommand =>
            _clearBoundaryCommand ??= new global::MEPAutoRouting.Shared.RelayCommand(() =>
            {
                Boundary = null;
                Log(Routing.LogLevel.Info, "Calculation boundary cleared.");
            });

        private void PickBoundary(bool fromLink)
        {
            if (ActionQueue == null) { Log(Routing.LogLevel.Error, "Revit action queue is not initialised."); return; }

            Log(Routing.LogLevel.Info, fromLink
                ? "Pick a Space, Room or Mass in a linked model… (Esc to cancel)"
                : "Pick a Space, Room or Mass in the host model… (Esc to cancel)");

            ActionQueue.Enqueue("Pick boundary", app =>
            {
                IRoutingBoundary b = null;
                string error = null;
                try
                {
                    b = fromLink ? BoundaryPicker.PickLinked(app.ActiveUIDocument)
                                 : BoundaryPicker.PickHost(app.ActiveUIDocument);
                }
                catch (InvalidOperationException ex) { error = ex.Message; }

                PostToUi(() =>
                {
                    if (error != null) { Log(Routing.LogLevel.Error, error); return; }
                    if (b == null) { Log(Routing.LogLevel.Info, "Boundary pick cancelled."); return; }
                    Boundary = b;
                    Log(Routing.LogLevel.Success, $"Calculation boundary set: {BoundaryText} ({BoundaryDetailText}).");
                }, "Pick boundary");
            });
        }

        // ================================================================== PIPE SIZE
        public ObservableCollection<double> AvailableSizes { get; } = new();

        public bool IsSizeInputEnabled => !MatchSize;

        public double PipeSizeMm => ConstraintOptions.PipeSizeMm;

        public string PipeSizeText
        {
            get => PipeSizeMm > 0 ? PipeSizeMm.ToString("0.#", CultureInfo.InvariantCulture) : "";
            set
            {
                if (string.IsNullOrWhiteSpace(value))
                {
                    ConstraintOptions.PipeSizeMm = 0;
                    NotifySize();
                    return;
                }
                if (!double.TryParse(value.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out double mm)
                    || mm <= 0 || mm > 3000)
                {
                    Log(Routing.LogLevel.Warn, $"Invalid pipe size '{value}'. Enter a diameter in mm (0 – 3000).");
                    Notify(nameof(PipeSizeText));
                    return;
                }

                if (_sizes.Count > 0)
                {
                    PipeSizeInfo hit = PipeSizeCatalog.Snap(mm, _sizes, out bool exact);
                    if (!exact)
                        Log(Routing.LogLevel.Warn,
                            $"{mm:0.#} mm is not in the size list of the selected type – changed to {hit.NominalMm:0.#} mm.");
                    mm = hit.NominalMm;
                }
                ConstraintOptions.PipeSizeMm = mm;
                NotifySize();
            }
        }

        public object SelectedListSize
        {
            get
            {
                foreach (double s in AvailableSizes)
                    if (Math.Abs(s - PipeSizeMm) < 0.05) return s;
                return null;
            }
            set
            {
                if (value is double d) PipeSizeText = d.ToString(CultureInfo.InvariantCulture);
            }
        }

        public string SizeSummaryText => MatchSize ? "Match source"
            : PipeSizeMm > 0 ? $"Ø{PipeSizeMm:0.#} mm" : "Not set";

        private void NotifySize()
        {
            Notify(nameof(PipeSizeText)); Notify(nameof(SelectedListSize));
            Notify(nameof(SizeSummaryText)); Notify(nameof(RouteBlockedReason));
            CommandManager.InvalidateRequerySuggested();
        }

        private void ReloadSizes()
        {
            if (!_constraintUiReady || ActionQueue == null) return;

            ElementId typeId;
            bool conduit;
            try
            {
                typeId = GetSelectedTypeId();
                conduit = IsConduitDiscipline();
            }
            catch (Exception ex)
            {
                RevitActionQueue.WriteErrorFile("ReloadSizes: reading selected type failed – " + ex.Message, ex);
                return;
            }
            if (typeId == null || typeId == ElementId.InvalidElementId) { ApplySizes(new List<PipeSizeInfo>()); return; }

            ActionQueue.Enqueue("Load sizes", app =>
            {
                Document doc = app.ActiveUIDocument?.Document;
                if (doc == null) return;
                List<PipeSizeInfo> list = conduit
                    ? PipeSizeCatalog.GetConduitSizes(doc, typeId)
                    : PipeSizeCatalog.GetPipeSizes(doc, typeId);
                PostToUi(() => ApplySizes(list), "Load sizes");
            });
        }

        private void ApplySizes(List<PipeSizeInfo> list)
        {
            _sizes = list ?? new List<PipeSizeInfo>();
            AvailableSizes.Clear();
            foreach (PipeSizeInfo s in _sizes) AvailableSizes.Add(s.NominalMm);

            if (_sizes.Count == 0)
            {
                Log(Routing.LogLevel.Warn, "No size list found for the selected type – any size can be entered.");
            }
            else if (PipeSizeMm > 0)
            {
                PipeSizeInfo hit = PipeSizeCatalog.Snap(PipeSizeMm, _sizes, out bool exact);
                if (!exact)
                {
                    Log(Routing.LogLevel.Warn,
                        $"{PipeSizeMm:0.#} mm is not available for the selected type – changed to {hit.NominalMm:0.#} mm.");
                    ConstraintOptions.PipeSizeMm = hit.NominalMm;
                }
            }
            NotifySize();
        }

        public PipeSizeInfo? ResolveRouteSize(IList<RouteProblem> problems)
        {
            if (MatchSize) return null;
            if (PipeSizeMm <= 0)
            {
                problems.Add(RouteProblem.Error("Pipe size is not set – enter a size or tick 'Match source'."));
                return null;
            }
            if (_sizes.Count == 0)
            {
                problems.Add(RouteProblem.Warn(
                    $"No size list found for the selected type – {PipeSizeMm:0.#} mm is used as entered."));
                return new PipeSizeInfo(PipeSizeMm, PipeSizeMm);
            }
            PipeSizeInfo hit = PipeSizeCatalog.Snap(PipeSizeMm, _sizes, out bool exact);
            if (!exact)
                problems.Add(RouteProblem.Warn(
                    $"{PipeSizeMm:0.#} mm is not available for the selected type – {hit.NominalMm:0.#} mm is used."));
            return hit;
        }

        public (IRoutingBoundary Boundary, PipeSizeInfo? Size, List<RouteProblem> PreChecks) GetConstraintInputs()
        {
            var pre = new List<RouteProblem>();
            PipeSizeInfo? size = ResolveRouteSize(pre);
            return (Boundary, size, pre);
        }

        // ================================================================== ROUTE GUARD
        public string RouteBlockedReason =>
            Source == null ? "Source connector is not picked."
          : Target == null ? "Target connector is not picked."
          : IsBusy ? "Another routing job is still running."
          : !MatchSize && PipeSizeMm <= 0 ? "Pipe size is not set – enter a size or tick 'Match source'."
          : null;
    }
}
