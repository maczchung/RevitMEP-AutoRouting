using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using System.Collections.Generic;
using System.Windows;
using MEPAutoRouting.Core;
using System.Globalization;

namespace MEPAutoRouting.Routing
{
    public partial class UnifiedRoutingUI : Window
    {
        public enum UserAction { None, PickStart, PickEnd, PickVolume, Run, Cancel }
        public UserAction ActionRequested { get; private set; } = UserAction.None;

        public XYZ StartPoint { get; set; }
        public XYZ EndPoint { get; set; }
        public Connector StartConnector { get; set; }
        public Connector EndConnector { get; set; }
        public Element StartElement { get; set; }
        public Element EndElement { get; set; }
        public Element RoutingVolumeElement { get; set; }
        public BoundingBoxXYZ RoutingBounds { get; set; }

        public List<PipeTypeOption> PipeTypeOptions { get; set; }
        public ElementId SelectedPipeTypeId { get; set; }
        public double SelectedPipeDiameterMm { get; set; }
        public RoutingOptions Options { get; private set; } = RoutingOptions.Load();

        public UnifiedRoutingUI()
        {
            InitializeComponent();
            LoadConstraintOptions();
            cbMode.SelectedIndex = (int)UnifiedRoutingCommand.CurrentMode;
            PipeTypeOptions = new List<PipeTypeOption>();
            SelectedPipeTypeId = ElementId.InvalidElementId;
            SelectedPipeDiameterMm = 100.0;
        }

        public void RefreshUI()
        {
            cbMode.SelectedIndex = (int)UnifiedRoutingCommand.CurrentMode;
            RefreshPipeTypeCombo();
            txtPipeSize.Text = SelectedPipeDiameterMm.ToString("F0");

            txtStart.Text = StartPoint != null ? ((StartElement != null ? StartElement.Name : "Element") + " (" + (StartConnector != null ? "Connector" : "Fallback") + ") | " + FormatXYZ(StartPoint)) : "Not selected";
            txtEnd.Text = EndPoint != null ? ((EndElement != null ? EndElement.Name : "Element") + " (" + (EndConnector != null ? "Connector" : "Fallback") + ") | " + FormatXYZ(EndPoint)) : "Not selected";
            txtVolume.Text = RoutingBounds != null ? ("Host: " + (RoutingVolumeElement != null ? RoutingVolumeElement.Name : "Routing Volume") + " | " + RoutingVolumeUtils.FormatBounds(RoutingBounds)) : "Not selected";
        }

        private void RefreshPipeTypeCombo()
        {
            cbPipeType.Items.Clear();
            if (PipeTypeOptions == null) PipeTypeOptions = new List<PipeTypeOption>();
            int selectedIndex = -1;
            for (int i = 0; i < PipeTypeOptions.Count; i++)
            {
                cbPipeType.Items.Add(PipeTypeOptions[i]);
                if (SelectedPipeTypeId != ElementId.InvalidElementId && PipeTypeOptions[i].Id == SelectedPipeTypeId)
                    selectedIndex = i;
            }
            if (selectedIndex >= 0) cbPipeType.SelectedIndex = selectedIndex;
            else if (cbPipeType.Items.Count > 0) cbPipeType.SelectedIndex = 0;
        }

        private bool CapturePipeSettingsFromUI()
        {
            PipeTypeOption selectedOption = cbPipeType.SelectedItem as PipeTypeOption;
            if (selectedOption != null) SelectedPipeTypeId = selectedOption.Id;
            double sizeMm;
            if (!double.TryParse(txtPipeSize.Text, out sizeMm) || sizeMm <= 0)
            {
                TaskDialog.Show("Pipe Settings", "Please input a valid pipe size in millimetres.");
                return false;
            }
            SelectedPipeDiameterMm = sizeMm;
            return true;
        }

        private void LoadConstraintOptions()
        {
            TxtWallClearance.Text = Options.WallClearanceMm.ToString(CultureInfo.InvariantCulture);
            ChkIncludeLinkWalls.IsChecked = Options.IncludeLinkWalls;
            ChkClearanceOnSpaceBoundary.IsChecked = Options.ClearanceOnSpaceBoundary;
            RbSpaceNone.IsChecked = Options.SpaceSource == SpaceSource.None;
            RbSpaceHost.IsChecked = Options.SpaceSource == SpaceSource.Host;
            RbSpaceLink.IsChecked = Options.SpaceSource == SpaceSource.Link;
        }

        private bool ReadConstraintOptions()
        {
            if (!RoutingOptions.TryParseMm(TxtWallClearance.Text, out double mm, out string error))
            {
                MessageBox.Show(error, "Unified Routing");
                TxtWallClearance.Focus();
                return false;
            }
            Options.WallClearanceMm = mm;
            Options.IncludeLinkWalls = ChkIncludeLinkWalls.IsChecked == true;
            Options.ClearanceOnSpaceBoundary = ChkClearanceOnSpaceBoundary.IsChecked == true;
            Options.SpaceSource = RbSpaceHost.IsChecked == true ? SpaceSource.Host : RbSpaceLink.IsChecked == true ? SpaceSource.Link : SpaceSource.None;
            Options.Save();
            return true;
        }

        private void PickStart_Click(object sender, RoutedEventArgs e) { if (!CapturePipeSettingsFromUI() || !ReadConstraintOptions()) return; UnifiedRoutingCommand.CurrentMode = cbMode.SelectedIndex == 0 ? RoutingMode.Pipe : RoutingMode.Conduit; ActionRequested = UserAction.PickStart; DialogResult = true; }
        private void PickEnd_Click(object sender, RoutedEventArgs e) { if (!CapturePipeSettingsFromUI() || !ReadConstraintOptions()) return; UnifiedRoutingCommand.CurrentMode = cbMode.SelectedIndex == 0 ? RoutingMode.Pipe : RoutingMode.Conduit; ActionRequested = UserAction.PickEnd; DialogResult = true; }
        private void PickVolume_Click(object sender, RoutedEventArgs e) { if (!CapturePipeSettingsFromUI() || !ReadConstraintOptions()) return; UnifiedRoutingCommand.CurrentMode = cbMode.SelectedIndex == 0 ? RoutingMode.Pipe : RoutingMode.Conduit; ActionRequested = UserAction.PickVolume; DialogResult = true; }
        private void Run_Click(object sender, RoutedEventArgs e) { if (!CapturePipeSettingsFromUI() || !ReadConstraintOptions()) return; UnifiedRoutingCommand.CurrentMode = cbMode.SelectedIndex == 0 ? RoutingMode.Pipe : RoutingMode.Conduit; ActionRequested = UserAction.Run; DialogResult = true; }
        private void Cancel_Click(object sender, RoutedEventArgs e) { ActionRequested = UserAction.Cancel; DialogResult = false; }

        private string FormatXYZ(XYZ point)
        {
            if (point == null) return "(null)";
            return "(" + point.X.ToString("F3") + ", " + point.Y.ToString("F3") + ", " + point.Z.ToString("F3") + ")";
        }
    }

    public class PipeTypeOption
    {
        public ElementId Id { get; private set; }
        public string Name { get; private set; }
        public PipeTypeOption(ElementId id, string name) { Id = id; Name = name; }
        public override string ToString() { return Name; }
    }
}
