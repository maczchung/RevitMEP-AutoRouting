using System.Windows;
using Autodesk.Revit.DB;

namespace MEPAutoRouting.Routing
{
    public partial class UnifiedRoutingUI : Window
    {
        // 定義使用者按了什麼按鈕
        public enum UserAction
        {
            None,
            PickStart,
            PickEnd,
            Run,
            Cancel
        }

        public UserAction ActionRequested { get; private set; } = UserAction.None;

        // 這些屬性用來在 Command 和 UI 之間傳遞資料
        public XYZ StartPoint { get; set; }
        public XYZ EndPoint { get; set; }
        public Connector StartConnector { get; set; }
        public Connector EndConnector { get; set; }
        public Element StartElement { get; set; }
        public Element EndElement { get; set; }

        public UnifiedRoutingUI()
        {
            InitializeComponent();
            cbMode.SelectedIndex = (int)UnifiedRoutingCommand.CurrentMode;
        }

        // 用來把外部選好的點更新到畫面上
        public void RefreshUI()
        {
            if (StartPoint != null)
            {
                string name = StartElement?.Name ?? "Element";
                string type = StartConnector != null ? "Connector" : "Fallback";
                txtStart.Text = $"{name} ({type}) | {FormatXYZ(StartPoint)}";
            }
            if (EndPoint != null)
            {
                string name = EndElement?.Name ?? "Element";
                string type = EndConnector != null ? "Connector" : "Fallback";
                txtEnd.Text = $"{name} ({type}) | {FormatXYZ(EndPoint)}";
            }
        }

        private void PickStart_Click(object sender, RoutedEventArgs e)
        {
            UnifiedRoutingCommand.CurrentMode = cbMode.SelectedIndex == 0 ? RoutingMode.Pipe : RoutingMode.Conduit;
            ActionRequested = UserAction.PickStart;
            this.DialogResult = true; // 設定 DialogResult 會自動關閉視窗 (Close)
        }

        private void PickEnd_Click(object sender, RoutedEventArgs e)
        {
            UnifiedRoutingCommand.CurrentMode = cbMode.SelectedIndex == 0 ? RoutingMode.Pipe : RoutingMode.Conduit;
            ActionRequested = UserAction.PickEnd;
            this.DialogResult = true; 
        }

        private void Run_Click(object sender, RoutedEventArgs e)
        {
            UnifiedRoutingCommand.CurrentMode = cbMode.SelectedIndex == 0 ? RoutingMode.Pipe : RoutingMode.Conduit;
            ActionRequested = UserAction.Run;
            this.DialogResult = true;
        }

        private void Cancel_Click(object sender, RoutedEventArgs e)
        {
            ActionRequested = UserAction.Cancel;
            this.DialogResult = false;
        }

        private string FormatXYZ(XYZ point)
        {
            if (point == null) return "(null)";
            return $"({point.X:F3}, {point.Y:F3}, {point.Z:F3})";
        }
    }
}