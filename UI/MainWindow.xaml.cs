using System;
using System.Collections.Specialized;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using MEPAutoRouting.UI.ViewModels;

namespace MEPAutoRouting.UI
{
    public partial class MainWindow : Window
    {
        private readonly MainViewModel _vm;
        private GridLength _panelHeight = new GridLength(210);

        public MainWindow(MainViewModel vm)
        {
            InitializeComponent();
            _vm = vm;
            DataContext = vm;

            // keyboard shortcuts
            InputBindings.Add(new KeyBinding(vm.PreviewCommand, Key.F5, ModifierKeys.None));
            InputBindings.Add(new KeyBinding(vm.RouteCommand, Key.Enter, ModifierKeys.Control));
            InputBindings.Add(new KeyBinding(vm.PickSourceCommand, Key.D1, ModifierKeys.Control));
            InputBindings.Add(new KeyBinding(vm.PickTargetCommand, Key.D2, ModifierKeys.Control));
            InputBindings.Add(new KeyBinding(new Shared.RelayCommand(TogglePanel), Key.J, ModifierKeys.Control));

            // auto-scroll OUTPUT like VS Code
            vm.LogEntries.CollectionChanged += (s, e) =>
            {
                if (e.Action == NotifyCollectionChangedAction.Add && vm.LogEntries.Count > 0)
                    OutputList.ScrollIntoView(vm.LogEntries[vm.LogEntries.Count - 1]);
            };

            vm.PropertyChanged += (s, e) =>
            {
                if (e.PropertyName == nameof(MainViewModel.Topmost)) UpdatePin();
            };

            StateChanged += (s, e) => UpdateMaxState();
            UpdatePin();
        }

        // ------------------------------------------------------------- title bar
        private void Minimize_Click(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;

        private void Maximize_Click(object sender, RoutedEventArgs e)
            => WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;

        private void Close_Click(object sender, RoutedEventArgs e) => Close();

        private void Pin_Click(object sender, RoutedEventArgs e)
        {
            _vm.Topmost = !_vm.Topmost;
        }

        private void UpdatePin()
        {
            PinButton.Foreground = (System.Windows.Media.Brush)FindResource(_vm.Topmost ? "Fg.Link" : "Fg.Default");
            PinButton.ToolTip = _vm.Topmost ? "Unpin (currently on top)" : "Keep on top";
        }

        private void UpdateMaxState()
        {
            // WindowChrome + WindowStyle=None overflows the screen by the resize border when maximised
            RootBorder.Margin = WindowState == WindowState.Maximized ? new Thickness(7) : new Thickness(0);
            MaxButton.Content = WindowState == WindowState.Maximized ? "\uE923" : "\uE922";
        }

        // ------------------------------------------------------------- panel
        private void TogglePanel_Click(object sender, RoutedEventArgs e) => TogglePanel();

        private void OutputActivity_Click(object sender, RoutedEventArgs e)
        {
            if (sender is RadioButton rb) rb.IsChecked = false;   // action button, not a pane
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
            if (PanelRow.Height.Value < 1)
            {
                PanelRow.Height = _panelHeight;
            }
            else
            {
                _panelHeight = PanelRow.Height;
                PanelRow.Height = new GridLength(0);
            }
        }
    }

    /// <summary>Shows "No problems" text only when PROBLEMS tab is active and list is empty.</summary>
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

    /// <summary>[info] teal, [done] green, [warn] yellow, [error] red – same as VS Code output.</summary>
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
