using System;
using System.Collections.Specialized;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Threading;
using MEPAutoRouting.UI.ViewModels;

namespace MEPAutoRouting.UI
{
    public partial class MainWindow : Window
    {
        private readonly MainViewModel _vm;
        private GridLength _panelHeight = new GridLength(210);
        private bool _scrollPending;

        public MainWindow(MainViewModel vm)
        {
            InitializeComponent();
            _vm = vm;
            DataContext = vm;

<<<<<<< HEAD
            // 任何漏網 UI exception 都寫入 OUTPUT + error.log，唔會令 Revit crash
            Dispatcher.UnhandledException += (s, e) =>
            {
                RevitActionQueue.WriteErrorFile("Unhandled UI exception – " + e.Exception.Message, e.Exception);
                e.Handled = true;
                SafeUiLog(Routing.LogLevel.Error,
                    $"Unhandled UI exception – {e.Exception.GetType().Name}: {e.Exception.Message}");
            };

=======
            // 任何漏網 UI exception 都寫入 OUTPUT，唔會靜靜雞冇反應
            Dispatcher.UnhandledException += (s, e) =>
            {
                _vm.Log(Routing.LogLevel.Error,
                    $"Unhandled UI exception – {e.Exception.GetType().Name}: {e.Exception.Message}");
                e.Handled = true;
            };

            // Route / Preview 快捷鍵：先 commit 住 focus 緊嘅 TextBox，再執行
>>>>>>> 6dd4d98ac757c36819d8fe104c6a91a62610d7a2
            InputBindings.Add(new KeyBinding(new Shared.RelayCommand(() => RunCommand(_vm.PreviewCommand, "Preview")), Key.F5, ModifierKeys.None));
            InputBindings.Add(new KeyBinding(new Shared.RelayCommand(() => RunCommand(_vm.RouteCommand, "Route")), Key.Enter, ModifierKeys.Control));
            InputBindings.Add(new KeyBinding(vm.PickSourceCommand, Key.D1, ModifierKeys.Control));
            InputBindings.Add(new KeyBinding(vm.PickTargetCommand, Key.D2, ModifierKeys.Control));
            InputBindings.Add(new KeyBinding(new Shared.RelayCommand(TogglePanel), Key.J, ModifierKeys.Control));

<<<<<<< HEAD
            // v4.2 FIX：唔好喺 CollectionChanged 入面同步 ScrollIntoView
            // （會觸發 "An ItemsControl is inconsistent with its items source" → Revit crash）
            vm.LogEntries.CollectionChanged += OnLogEntriesChanged;
=======
            vm.LogEntries.CollectionChanged += (s, e) =>
            {
                if (e.Action == NotifyCollectionChangedAction.Add && vm.LogEntries.Count > 0)
                    OutputList.ScrollIntoView(vm.LogEntries[vm.LogEntries.Count - 1]);
            };
>>>>>>> 6dd4d98ac757c36819d8fe104c6a91a62610d7a2

            vm.PropertyChanged += (s, e) =>
            {
                if (e.PropertyName == nameof(MainViewModel.Topmost)) UpdatePin();
            };

            StateChanged += (s, e) => UpdateMaxState();

            // v4.2 FIX：window 完全 load 好先初始化（size list 等），唔好喺 constructor 做
            Loaded += (_, _) =>
            {
                try { _vm.InitConstraintUi(); }
                catch (Exception ex)
                {
                    RevitActionQueue.WriteErrorFile("InitConstraintUi failed – " + ex.Message, ex);
                    SafeUiLog(Routing.LogLevel.Error, $"Initialisation failed – {ex.GetType().Name}: {ex.Message}");
                }
            };

            Closed += (_, _) => vm.LogEntries.CollectionChanged -= OnLogEntriesChanged;

            UpdatePin();
        }

        private void OnLogEntriesChanged(object sender, NotifyCollectionChangedEventArgs e)
        {
            if (e.Action != NotifyCollectionChangedAction.Add || _scrollPending) return;
            _scrollPending = true;   // 一連串 log 只 scroll 一次
            Dispatcher.BeginInvoke(new Action(() =>
            {
                _scrollPending = false;
                try
                {
                    if (!OutputList.IsLoaded || !OutputList.IsVisible || _vm.LogEntries.Count == 0) return;
                    OutputList.ScrollIntoView(_vm.LogEntries[_vm.LogEntries.Count - 1]);
                }
                catch (Exception ex)
                {
                    RevitActionQueue.WriteErrorFile("OUTPUT auto-scroll failed – " + ex.Message, ex);
                }
            }), DispatcherPriority.Background);
        }

        /// <summary>寫 log 失敗都唔可以 throw。</summary>
        private void SafeUiLog(Routing.LogLevel level, string msg)
        {
            try { _vm.Log(level, msg); }
            catch (Exception ex) { RevitActionQueue.WriteErrorFile("VM.Log failed – " + ex.Message, ex); }
        }

        // ------------------------------------------------------------- title bar
        private void Minimize_Click(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;

        private void Maximize_Click(object sender, RoutedEventArgs e)
            => WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;

        private void Close_Click(object sender, RoutedEventArgs e) => Close();

        private void Pin_Click(object sender, RoutedEventArgs e) => _vm.Topmost = !_vm.Topmost;

<<<<<<< HEAD
        // ------------------------------------------------------------- route / preview
=======
        // ------------------------------------------------------------- route / preview（3 個入口行為一致）
>>>>>>> 6dd4d98ac757c36819d8fe104c6a91a62610d7a2
        private void BtnRoute_Click(object sender, RoutedEventArgs e) => RunCommand(_vm.RouteCommand, "Route");

        private void BtnPreview_Click(object sender, RoutedEventArgs e) => RunCommand(_vm.PreviewCommand, "Preview");

        private void RunCommand(ICommand cmd, string name)
        {
            CommitFocusedInput();
<<<<<<< HEAD
            SafeUiLog(Routing.LogLevel.Info, $"▶ {name} clicked");
            if (cmd == null) { SafeUiLog(Routing.LogLevel.Error, $"{name} command is not available (null)."); return; }
            if (!cmd.CanExecute(null))
            {
                SafeUiLog(Routing.LogLevel.Warn, $"{name} cannot run: {_vm.RouteBlockedReason ?? "CanExecute returned false"}");
                return;
            }
            try { cmd.Execute(null); }
            catch (Exception ex)
            {
                RevitActionQueue.WriteErrorFile($"{name} failed on UI thread – " + ex.Message, ex);
                SafeUiLog(Routing.LogLevel.Error, $"{name} failed on UI thread – {ex.GetType().Name}: {ex.Message}");
=======
            _vm.Log(Routing.LogLevel.Info, $"▶ {name} clicked");
            if (cmd == null) { _vm.Log(Routing.LogLevel.Error, $"{name} command is not available (null)."); return; }
            if (!cmd.CanExecute(null))
            {
                _vm.Log(Routing.LogLevel.Warn, $"{name} cannot run: {_vm.RouteBlockedReason ?? "CanExecute returned false"}");
                return;
            }

            try { cmd.Execute(null); }
            catch (Exception ex)
            {
                _vm.Log(Routing.LogLevel.Error, $"{name} failed on UI thread – {ex.GetType().Name}: {ex.Message}");
>>>>>>> 6dd4d98ac757c36819d8fe104c6a91a62610d7a2
            }
        }

        private static void CommitFocusedInput()
        {
            if (Keyboard.FocusedElement is not TextBox tb) return;
            tb.GetBindingExpression(TextBox.TextProperty)?.UpdateSource();
            if (tb.TemplatedParent is ComboBox cb)
                cb.GetBindingExpression(ComboBox.TextProperty)?.UpdateSource();
        }

        private void UpdatePin()
        {
            PinButton.Foreground = (System.Windows.Media.Brush)FindResource(_vm.Topmost ? "Fg.Link" : "Fg.Default");
            PinButton.ToolTip = _vm.Topmost ? "Unpin (currently on top)" : "Keep on top";
        }

        private void UpdateMaxState()
        {
            RootBorder.Margin = WindowState == WindowState.Maximized ? new Thickness(7) : new Thickness(0);
            MaxButton.Content = WindowState == WindowState.Maximized ? "\uE923" : "\uE922";
        }

        // ------------------------------------------------------------- panel
        private void TogglePanel_Click(object sender, RoutedEventArgs e) => TogglePanel();

        private void OutputActivity_Click(object sender, RoutedEventArgs e)
        {
            if (sender is RadioButton rb) rb.IsChecked = false;
            _vm.SelectedPanelTab = 1;
            TogglePanel();
        }

        private void ShowProblems_Click(object sender, RoutedEventArgs e)
        {
            _vm.SelectedPanelTab = 0;
            if (PanelRow.Height.Value < 1) TogglePanel();
        }

        private void TogglePanel()
        {
            if (PanelRow.Height.Value < 1) PanelRow.Height = _panelHeight;
            else { _panelHeight = PanelRow.Height; PanelRow.Height = new GridLength(0); }
        }
    }

    public class NoProblemsVisibilityConverter : IMultiValueConverter
    {
        public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
        {
            bool problemsTab = values.Length > 0 && values[0] is int tab && tab == 0;
            bool empty = values.Length > 1 && values[1] is int count && count == 0;
            return problemsTab && empty ? Visibility.Visible : Visibility.Collapsed;
        }

        public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
            => throw new NotSupportedException();
    }

    public class LogLevelBrushConverter : IValueConverter
    {
        private static readonly System.Windows.Media.Brush Info    = Freeze("#4EC9B0");
        private static readonly System.Windows.Media.Brush Success = Freeze("#89D185");
        private static readonly System.Windows.Media.Brush Warn    = Freeze("#CCA700");
        private static readonly System.Windows.Media.Brush Error   = Freeze("#F14C4C");

        private static System.Windows.Media.Brush Freeze(string hex)
        {
            var b = new System.Windows.Media.SolidColorBrush(
                (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(hex));
            b.Freeze();
            return b;
        }

        public object Convert(object value, Type targetType, object parameter, CultureInfo culture) => value switch
        {
            Routing.LogLevel.Success => Success,
            Routing.LogLevel.Warn => Warn,
            Routing.LogLevel.Error => Error,
            _ => Info
        };

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => throw new NotSupportedException();
    }
}
