using Autodesk.Revit.DB;
using System.Windows;
using MEPAutoRouting.Core;

namespace MEPAutoRouting.Routing
{
    public partial class UnifiedRoutingUI : Window
    {
        public enum UserAction
        {
            None,
            PickStart,
            PickEnd,
            PickVolume,
            Run,
            Cancel
        }

        public UserAction ActionRequested { get; private set; } = UserAction.None;

        public XYZ StartPoint { get; set; }
        public XYZ EndPoint { get; set; }
        public Connector StartConnector { get; set; }
        public Connector EndConnector { get; set; }
        public Element StartElement { get; set; }
        public Element EndElement { get; set; }
        public Element RoutingVolumeElement { get; set; }
        public BoundingBoxXYZ RoutingBounds { get; set; }

        public UnifiedRoutingUI()
        {
            InitializeComponent();
            cbMode.SelectedIndex = (int)UnifiedRoutingCommand.CurrentMode;
        }

        public void RefreshUI()
        {
            cbMode.SelectedIndex = (int)UnifiedRoutingCommand.CurrentMode;

            if (StartPoint != null)
            {
                string name = StartElement != null ? StartElement.Name : "Element";
                string type = StartConnector != null ? "Connector" : "Fallback";
                txtStart.Text = name + " (" + type + ") | " + FormatXYZ(StartPoint);
            }
            else
            {
                txtStart.Text = "Not selected";
            }

            if (EndPoint != null)
            {
                string name = EndElement != null ? EndElement.Name : "Element";
                string type = EndConnector != null ? "Connector" : "Fallback";
                txtEnd.Text = name + " (" + type + ") | " + FormatXYZ(EndPoint);
            }
            else
            {
                txtEnd.Text = "Not selected";
            }

            if (RoutingBounds != null)
            {
                string name = RoutingVolumeElement != null ? RoutingVolumeElement.Name : "Routing Volume";
                txtVolume.Text = "Host: " + name + " | " + RoutingVolumeUtils.FormatBounds(RoutingBounds);
            }
            else
            {
                txtVolume.Text = "Not selected";
            }
        }

        private void PickStart_Click(object sender, RoutedEventArgs e)
        {
            UnifiedRoutingCommand.CurrentMode = cbMode.SelectedIndex == 0 ? RoutingMode.Pipe : RoutingMode.Conduit;
            ActionRequested = UserAction.PickStart;
            DialogResult = true;
        }

        private void PickEnd_Click(object sender, RoutedEventArgs e)
        {
            UnifiedRoutingCommand.CurrentMode = cbMode.SelectedIndex == 0 ? RoutingMode.Pipe : RoutingMode.Conduit;
            ActionRequested = UserAction.PickEnd;
            DialogResult = true;
        }

        private void PickVolume_Click(object sender, RoutedEventArgs e)
        {
            UnifiedRoutingCommand.CurrentMode = cbMode.SelectedIndex == 0 ? RoutingMode.Pipe : RoutingMode.Conduit;
            ActionRequested = UserAction.PickVolume;
            DialogResult = true;
        }

        private void Run_Click(object sender, RoutedEventArgs e)
        {
            UnifiedRoutingCommand.CurrentMode = cbMode.SelectedIndex == 0 ? RoutingMode.Pipe : RoutingMode.Conduit;
            ActionRequested = UserAction.Run;
            DialogResult = true;
        }

        private void Cancel_Click(object sender, RoutedEventArgs e)
        {
            ActionRequested = UserAction.Cancel;
            DialogResult = false;
        }

        private string FormatXYZ(XYZ point)
        {
            if (point == null)
                return "(null)";

            return "(" + point.X.ToString("F3") + ", " + point.Y.ToString("F3") + ", " + point.Z.ToString("F3") + ")";
        }
    }
}
