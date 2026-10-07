using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Plumbing;
using Autodesk.Revit.UI;
using MEPAutoRouting.Routing;
using MEPAutoRouting.Shared;

namespace MEPAutoRouting.UI.ViewModels
{
    public partial class MainViewModel : ObservableObject
    {
        private readonly RoutingEventHandler _handler;
        private readonly RevitActionQueue _queue;
        private readonly Dispatcher _dispatcher;
        private readonly UserSettings _settings;
        public RoutingOptions ConstraintOptions { get; } = RoutingOptions.Load();

        public MainViewModel(RoutingEventHandler handler, RevitActionQueue queue, string docTitle, string revitVersion)
        {
            _handler = handler;
            _queue = queue;
            _dispatcher = Dispatcher.CurrentDispatcher;
            _documentTitle = docTitle;
            RevitVersion = $"Revit {revitVersion}";
            AppVersion = typeof(MainViewModel).Assembly.GetName().Version?.ToString(3) ?? "1.0.0";

            Disciplines = Enum.GetValues(typeof(Discipline)).Cast<Discipline>()
                .Select(d => new Option<Discipline> { Value = d, Label = d.Label() }).ToList();

            _settings = UserSettings.Load();
            ApplySettings(_settings);

            PickSourceCommand = new RelayCommand(() => Raise(RoutingRequest.PickSource), () => !IsBusy);
            PickTargetCommand = new RelayCommand(() => Raise(RoutingRequest.PickTarget), () => !IsBusy);
            SwapCommand       = new RelayCommand(Swap, () => Source != null || Target != null);
            PreviewCommand    = new RelayCommand(() => ExecuteRoute(false), () => RouteBlockedReason == null);
            RouteCommand      = new RelayCommand(() => ExecuteRoute(true), () => RouteBlockedReason == null && SelectedType != null);
            ReloadCommand     = new RelayCommand(RequestLoadTypes, () => !IsBusy);
            ClearCommand      = new RelayCommand(ClearSelection);
            ClearLogCommand   = new RelayCommand(() => LogEntries.Clear());
            CopyLogCommand    = new RelayCommand(CopyLog);
            ResetCommand      = new RelayCommand(() => { ApplySettings(new UserSettings()); ResetConstraintUi(); RequestLoadTypes(); Log(LogLevel.Info, "Settings reset to defaults."); });

            ActionQueue = queue;

            Log(LogLevel.Info, $"MEP Auto Routing {AppVersion} started · {RevitVersion}");
        }

        // ================================================================ header / status
        public string RevitVersion { get; }
        public string AppVersion { get; }

        private string _documentTitle;
        public string DocumentTitle { get => _documentTitle; set => Set(ref _documentTitle, value); }

        private string _status = "Ready";
        public string Status { get => _status; set => Set(ref _status, value); }

        private bool _isBusy;
        public bool IsBusy
        {
            get => _isBusy;
            set
            {
                if (!Set(ref _isBusy, value)) return;
                if (!value && Status.StartsWith("Pick")) Status = "Ready";
                OnPropertyChanged(nameof(CanPlan));
                CommandManager.InvalidateRequerySuggested();
            }
        }

        // ================================================================ activity bar
        private bool _isRoutePane = true, _isSettingsPane, _isAboutPane;
        public bool IsRoutePane    { get => _isRoutePane;    set => Set(ref _isRoutePane, value); }
        public bool IsSettingsPane { get => _isSettingsPane; set => Set(ref _isSettingsPane, value); }
        public bool IsAboutPane    { get => _isAboutPane;    set => Set(ref _isAboutPane, value); }

        // ================================================================ connectors
        private ConnectorInfo _source, _target;
        public ConnectorInfo Source
        {
            get => _source;
            set { if (Set(ref _source, value)) OnConnectorsChanged(); }
        }
        public ConnectorInfo Target
        {
            get => _target;
            set { if (Set(ref _target, value)) OnConnectorsChanged(); }
        }

        private void OnConnectorsChanged()
        {
            OnPropertyChanged(nameof(CanPlan));
            ClearRoutePreview();
            CommandManager.InvalidateRequerySuggested();
        }

        public bool CanPlan => !IsBusy && Source != null && Target != null;

        // ================================================================ system
        public List<Option<Discipline>> Disciplines { get; }

        private Discipline _selectedDiscipline;
        public Discipline SelectedDiscipline
        {
            get => _selectedDiscipline;
            set
            {
                if (!Set(ref _selectedDiscipline, value)) return;
                OnPropertyChanged(nameof(HasSystemTypes));
                OnPropertyChanged(nameof(DisciplineLabel));
                Source = null; Target = null;       // different connector domain
                RequestLoadTypes();
            }
        }
        public string DisciplineLabel => SelectedDiscipline.Label();
        public bool HasSystemTypes => SelectedDiscipline.HasSystemType();

        public ObservableCollection<TypeItem> Types { get; } = new();
        public ObservableCollection<TypeItem> SystemTypes { get; } = new();
        public ObservableCollection<TypeItem> Levels { get; } = new();

        private TypeItem _selectedType, _selectedSystemType, _selectedLevel;
        public TypeItem SelectedType       { get => _selectedType;       set { if (Set(ref _selectedType, value)) ClearRoutePreview(); } }
        public TypeItem SelectedSystemType { get => _selectedSystemType; set { if (Set(ref _selectedSystemType, value)) ClearRoutePreview(); } }
        public TypeItem SelectedLevel      { get => _selectedLevel;      set => Set(ref _selectedLevel, value); }

        public void SetTypes(List<TypeItem> types, List<TypeItem> systems, List<TypeItem> levels, ElementId activeLevel)
        {
            Reset(Types, types);
            Reset(SystemTypes, systems);
            Reset(Levels, levels);
            SelectedType = Types.FirstOrDefault();
            SelectedSystemType = SystemTypes.FirstOrDefault();
            SelectedLevel = Levels.FirstOrDefault(l => activeLevel != null && l.Id == activeLevel) ?? Levels.FirstOrDefault();
            if (Types.Count == 0) Log(LogLevel.Warn, $"No {DisciplineLabel} types in this project – load one first.");
        }

        public void SelectLevelById(ElementId id)
        {
            var lvl = Levels.FirstOrDefault(l => l.Id == id);
            if (lvl != null) SelectedLevel = lvl;
        }

        private static void Reset<T>(ObservableCollection<T> col, IEnumerable<T> items)
        {
            col.Clear();
            foreach (var i in items) col.Add(i);
        }

        // ================================================================ routing options
        private double _minSegmentMm;
        public double LeadMm
        {
            get => ConstraintOptions.LeadLengthMm;
            set
            {
                double next = Math.Max(0, value);
                if (Math.Abs(ConstraintOptions.LeadLengthMm - next) < 1e-9) return;
                ConstraintOptions.LeadLengthMm = next;
                OnPropertyChanged(nameof(LeadMm));
            }
        }
        public double MinSegmentMm { get => _minSegmentMm; set => Set(ref _minSegmentMm, Math.Max(0, value)); }

        private bool _matchSize, _addFittings, _connectEnds, _suppressWarnings, _selectAfterRoute, _topmost;
        public bool MatchSize        { get => _matchSize;        set => Set(ref _matchSize, value); }
        public bool AddFittings      { get => _addFittings;      set => Set(ref _addFittings, value); }
        public bool ConnectEnds      { get => _connectEnds;      set => Set(ref _connectEnds, value); }
        public bool SuppressWarnings { get => _suppressWarnings; set => Set(ref _suppressWarnings, value); }
        public bool SelectAfterRoute { get => _selectAfterRoute; set => Set(ref _selectAfterRoute, value); }
        public bool Topmost          { get => _topmost;          set => Set(ref _topmost, value); }

        public RouteOptions BuildOptions() => new RouteOptions
        {
            Discipline = SelectedDiscipline,
            TypeId = SelectedType?.Id ?? ElementId.InvalidElementId,
            SystemTypeId = SelectedSystemType?.Id ?? ElementId.InvalidElementId,
            LevelId = SelectedLevel?.Id ?? ElementId.InvalidElementId,
            LeadMm = LeadMm,
            MinSegmentMm = MinSegmentMm,
            MatchSize = MatchSize,
            AddFittings = AddFittings,
            ConnectEnds = ConnectEnds,
            SuppressWarnings = SuppressWarnings
        };

        private void ApplySettings(UserSettings s)
        {
            _selectedDiscipline = s.Discipline; OnPropertyChanged(nameof(SelectedDiscipline));
            OnPropertyChanged(nameof(HasSystemTypes)); OnPropertyChanged(nameof(DisciplineLabel));
            LeadMm = s.LeadMm; MinSegmentMm = s.MinSegmentMm;
            MatchSize = s.MatchSize; AddFittings = s.AddFittings; ConnectEnds = s.ConnectEnds;
            SuppressWarnings = s.SuppressWarnings; SelectAfterRoute = s.SelectAfterRoute; Topmost = s.Topmost;
        }

        public void SaveSettings()
        {
            _settings.Discipline = SelectedDiscipline;
            _settings.LeadMm = LeadMm; _settings.MinSegmentMm = MinSegmentMm;
            _settings.MatchSize = MatchSize; _settings.AddFittings = AddFittings; _settings.ConnectEnds = ConnectEnds;
            _settings.SuppressWarnings = SuppressWarnings; _settings.SelectAfterRoute = SelectAfterRoute; _settings.Topmost = Topmost;
            _settings.Save();
            ConstraintOptions.Save();
        }

        // ================================================================ preview / summary
        public ObservableCollection<PathPointItem> PreviewPoints { get; } = new();
        public ObservableCollection<RouteProblem> Problems { get; } = new();

        private int _segmentCount;
        private double _totalFallFt;
        public int SegmentCount { get => _segmentCount; private set => Set(ref _segmentCount, value); }

        private string _totalLengthText = "0 mm";
        public string TotalLengthText { get => _totalLengthText; private set => Set(ref _totalLengthText, value); }

        public int ProblemCount => Problems.Count;
        public int ErrorCount => Problems.Count(p => p.IsError);
        public int WarningCount => Problems.Count(p => !p.IsError);
        public string ProblemSummary => $"{ErrorCount} error · {WarningCount} warning";
        public string TotalFallText => $"{UnitConv.Mm(_totalFallFt)} mm";

        private int _selectedEditorTab;
        public int SelectedEditorTab { get => _selectedEditorTab; set => Set(ref _selectedEditorTab, value); }

        private int _selectedPanelTab = 1;
        public int SelectedPanelTab { get => _selectedPanelTab; set => Set(ref _selectedPanelTab, value); }

        public void SetPreview(IList<XYZ> pts, IList<RouteProblem> problems)
        {
            PreviewPoints.Clear();
            _totalFallFt = pts == null || pts.Count < 2 ? 0 : Math.Abs(pts[0].Z - pts[pts.Count - 1].Z);
            double total = 0;
            for (int i = 0; i < pts.Count; i++)
            {
                double seg = i < pts.Count - 1 ? pts[i].DistanceTo(pts[i + 1]) : 0;
                total += seg;
                PreviewPoints.Add(new PathPointItem
                {
                    Index = i,
                    X = UnitConv.Mm(pts[i].X),
                    Y = UnitConv.Mm(pts[i].Y),
                    Z = UnitConv.Mm(pts[i].Z),
                    Segment = i < pts.Count - 1 ? UnitConv.Mm(seg) : "—",
                    Direction = i < pts.Count - 1 ? RoutePlanner.DirectionLabel(pts[i], pts[i + 1]) : "● End"
                });
            }

            Problems.Clear();
            foreach (RouteProblem problem in problems) Problems.Add(problem);
            OnPropertyChanged(nameof(ProblemCount));
            OnPropertyChanged(nameof(ErrorCount));
            OnPropertyChanged(nameof(WarningCount));
            OnPropertyChanged(nameof(ProblemSummary));
            OnPropertyChanged(nameof(SlopeText));
            OnPropertyChanged(nameof(TotalFallText));
            OnPropertyChanged(nameof(BoundaryText));
            if (problems.Count > 0) SelectedPanelTab = 0;

            UpdateSummary(pts.Count - 1, total);
            SelectedEditorTab = 1;
        }

        private void UpdateSummary(int segments, double totalFt)
        {
            SegmentCount = Math.Max(0, segments);
            TotalLengthText = $"{UnitConv.Mm(totalFt)} mm";
        }

        // v4.7 (ISS-012/ISS-013) – any routing-input change invalidates the current preview,
        // total fall and summary so stale route results cannot survive a parameter change.
        private void ClearRoutePreview()
        {
            PreviewPoints.Clear();
            _totalFallFt = 0;
            OnPropertyChanged(nameof(TotalFallText));
            UpdateSummary(0, 0);
        }

        // ================================================================ output log
        public ObservableCollection<LogEntry> LogEntries { get; } = new();

        public void Log(LogLevel level, string message)
        {
            void add()
            {
                LogEntries.Add(new LogEntry { Level = level, Message = message });
                if (LogEntries.Count > 500) LogEntries.RemoveAt(0);
                if (level != LogLevel.Info) Status = message.Length > 80 ? message.Substring(0, 80) + "…" : message;
            }
            if (_dispatcher.CheckAccess()) add();
            else
            {
                try { _dispatcher.BeginInvoke(new Action(add)); }
                catch (Exception ex) { RevitActionQueue.WriteErrorFile("Could not post VM log to UI – " + ex.Message, ex); }
            }
        }

        // ================================================================ commands
        public ICommand PickSourceCommand { get; }
        public ICommand PickTargetCommand { get; }
        public ICommand SwapCommand { get; }
        public ICommand PreviewCommand { get; }
        public ICommand RouteCommand { get; }
        public ICommand ReloadCommand { get; }
        public ICommand ClearCommand { get; }
        public ICommand ClearLogCommand { get; }
        public ICommand CopyLogCommand { get; }
        public ICommand ResetCommand { get; }

        public void RequestLoadTypes() => Raise(RoutingRequest.LoadTypes);

        // v4.8 (ISS-006) – the request is captured in the closure; no shared field can be overwritten.
        private void Raise(RoutingRequest request)
        {
            if (IsBusy) return;
            if (!_queue.Enqueue(request.ToString(), app => _handler.Execute(app, request)))
                Log(LogLevel.Warn, "Revit is busy. Finish the current command and try again.");
        }

        private void Swap()
        {
            var s = _source;
            _source = _target; _target = s;
            OnPropertyChanged(nameof(Source)); OnPropertyChanged(nameof(Target));
            OnConnectorsChanged();
            Log(LogLevel.Info, "Swapped source and target.");
        }

        private void ClearSelection()
        {
            Source = null; Target = null;
            ClearRoutePreview();   // v4.7 (ISS-013) – also covers the case where both were already null
            Problems.Clear(); OnPropertyChanged(nameof(ProblemCount));
            Status = "Ready";
        }

        private void CopyLog()
        {
            try { Clipboard.SetText(string.Join(Environment.NewLine, LogEntries.Select(l => l.ToString()))); Status = "Output copied to clipboard"; }
            catch { /* clipboard locked */ }
        }

        private partial void Notify(string propertyName) => OnPropertyChanged(propertyName);

        private partial ElementId GetSelectedTypeId()
            => SelectedType?.Id ?? ElementId.InvalidElementId;

        private partial bool IsConduitDiscipline()
            => SelectedDiscipline == Discipline.Conduit;

        private partial PipingSystemType GetSelectedPipingSystemType()
            => SelectedSystemType?.Element as PipingSystemType;

        private partial RouteRequest BuildRouteRequest(bool commit, IRoutingBoundary boundary,
                                                        PipeSizeInfo? size, List<RouteProblem> preChecks)
            => new RouteRequest
            {
                Source = Source,
                Target = Target,
                PipeTypeId = SelectedType?.Id ?? ElementId.InvalidElementId,
                SystemTypeId = SelectedSystemType?.Id ?? ElementId.InvalidElementId,
                LevelId = SelectedLevel?.Id ?? ElementId.InvalidElementId,
                Discipline = SelectedDiscipline,
                Options = ConstraintOptions,
                Boundary = boundary,
                Size = size,
                PreChecks = preChecks,
                MinSegmentMm = MinSegmentMm,
                SuppressWarnings = SuppressWarnings,
                AddFittings = AddFittings,
                ConnectEnds = ConnectEnds,
                Commit = commit,
                SelectAfterRoute = SelectAfterRoute   // v4.8 (ISS-015)
            };

        private partial void ApplyRouteResult(RouteResult result)
        {
            PreviewPoints.Clear();
            double total = 0;
            for (int i = 0; i < result.Path.Count; i++)
            {
                double segment = i < result.Path.Count - 1 ? result.Path[i].DistanceTo(result.Path[i + 1]) : 0;
                total += segment;
                PreviewPoints.Add(new PathPointItem
                {
                    Index = i,
                    X = UnitConv.Mm(result.Path[i].X),
                    Y = UnitConv.Mm(result.Path[i].Y),
                    Z = UnitConv.Mm(result.Path[i].Z),
                    Segment = i < result.Path.Count - 1 ? UnitConv.Mm(segment) : "-",
                    Direction = i < result.Path.Count - 1 ? RoutePlanner.DirectionLabel(result.Path[i], result.Path[i + 1]) : "End"
                });
            }
            Problems.Clear();
            foreach (RouteProblem problem in result.Problems) Problems.Add(problem);
            UpdateSummary(Math.Max(0, result.Path.Count - 1), total);
            // v4.8.1 (Task 1) – show the actually applied fall (0 when the slope could not be applied)
            _totalFallFt = result.AppliedFallFt >= 0 ? result.AppliedFallFt
                : result.Path.Count < 2 ? 0 : Math.Abs(result.Path[0].Z - result.Path[result.Path.Count - 1].Z);
            OnPropertyChanged(nameof(ProblemCount));
            OnPropertyChanged(nameof(ErrorCount));
            OnPropertyChanged(nameof(WarningCount));
            OnPropertyChanged(nameof(ProblemSummary));
            OnPropertyChanged(nameof(TotalFallText));
            SelectedEditorTab = 1;
        }
    }
}
