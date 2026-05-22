using System.Windows;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Autodesk.Revit.UI.Selection;
using MEPAutoRouting.Core;

namespace MEPAutoRouting.Routing
{
    public partial class UnifiedRoutingUI : Window
    {
        private readonly UIDocument _uidoc;
        private readonly Document _doc;

        private bool _runClicked;
        private bool _cancelClicked;
        private bool _pickStartClicked;
        private bool _pickEndClicked;

        public XYZ StartPoint { get; private set; }
        public XYZ EndPoint { get; private set; }

        public Connector StartConnector { get; private set; }
        public Connector EndConnector { get; private set; }

        public UnifiedRoutingUI(UIDocument uidoc)
        {
            InitializeComponent();

            _uidoc = uidoc;
            _doc = uidoc.Document;

            cbMode.SelectedIndex = (int)UnifiedRoutingCommand.CurrentMode;

            // Programmatic binding as backup.
            // XAML already has Click="...", so flags below avoid duplicate execution.
            btnPickStart.Click += PickStart_Click;
            btnPickEnd.Click += PickEnd_Click;
            btnRun.Click += Run_Click;
            btnCancel.Click += Cancel_Click;
        }

        private void PickStart_Click(object sender, RoutedEventArgs e)
        {
            if (_pickStartClicked)
                return;

            _pickStartClicked = true;

            Hide();

            try
            {
                Reference reference = _uidoc.Selection.PickObject(
                    ObjectType.Element,
                    "Select start element / connector");

                Element element = _doc.GetElement(reference);
                XYZ pickPoint = reference.GlobalPoint;

                bool preferPipe = cbMode.SelectedIndex == 0;
                bool preferElectrical = cbMode.SelectedIndex == 1;

                StartConnector = ConnectorUtils.GetClosestConnector(
                    element,
                    pickPoint,
                    preferPipe,
                    preferElectrical);

                StartPoint = StartConnector != null
                    ? StartConnector.Origin
                    : ConnectorUtils.GetFallbackPoint(element);

                if (StartPoint == null)
                {
                    TaskDialog.Show(
                        "Routing",
                        "Cannot get start point or connector from selected element.");

                    return;
                }

                txtStart.Text =
                    (StartConnector != null
                        ? element.Name + " (Connector)"
                        : element.Name + " (Fallback Point)") +
                    " | " +
                    FormatXYZ(StartPoint);
            }
            catch
            {
                // User cancelled selection.
            }
            finally
            {
                _pickStartClicked = false;
                Show();
                Activate();
            }
        }

        private void PickEnd_Click(object sender, RoutedEventArgs e)
        {
            if (_pickEndClicked)
                return;

            _pickEndClicked = true;

            Hide();

            try
            {
                Reference reference = _uidoc.Selection.PickObject(
                    ObjectType.Element,
                    "Select end element / connector");

                Element element = _doc.GetElement(reference);
                XYZ pickPoint = reference.GlobalPoint;

                bool preferPipe = cbMode.SelectedIndex == 0;
                bool preferElectrical = cbMode.SelectedIndex == 1;

                EndConnector = ConnectorUtils.GetClosestConnector(
                    element,
                    pickPoint,
                    preferPipe,
                    preferElectrical);

                EndPoint = EndConnector != null
                    ? EndConnector.Origin
                    : ConnectorUtils.GetFallbackPoint(element);

                if (EndPoint == null)
                {
                    TaskDialog.Show(
                        "Routing",
                        "Cannot get end point or connector from selected element.");

                    return;
                }

                txtEnd.Text =
                    (EndConnector != null
                        ? element.Name + " (Connector)"
                        : element.Name + " (Fallback Point)") +
                    " | " +
                    FormatXYZ(EndPoint);
            }
            catch
            {
                // User cancelled selection.
            }
            finally
            {
                _pickEndClicked = false;
                Show();
                Activate();
            }
        }

        private void Run_Click(object sender, RoutedEventArgs e)
        {
            if (_runClicked)
                return;

            _runClicked = true;

            TaskDialog.Show("UI Debug", "Run_Click triggered.");

            UnifiedRoutingCommand.CurrentMode =
                cbMode.SelectedIndex == 0
                    ? RoutingMode.Pipe
                    : RoutingMode.Conduit;

            if (StartPoint == null || EndPoint == null)
            {
                TaskDialog.Show(
                    "Routing",
                    "Please select both start and end.");

                _runClicked = false;
                return;
            }

            if (StartPoint.DistanceTo(EndPoint) < 0.01)
            {
                TaskDialog.Show(
                    "Routing",
                    "Start and End are too close or the same connector. Please select two different connectors.");

                _runClicked = false;
                return;
            }

            DialogResult = true;
            Close();
        }

        private void Cancel_Click(object sender, RoutedEventArgs e)
        {
            if (_cancelClicked)
                return;

            _cancelClicked = true;

            TaskDialog.Show("UI Debug", "Cancel_Click triggered.");

            DialogResult = false;
            Close();
        }

        private string FormatXYZ(XYZ point)
        {
            if (point == null)
                return "(null)";

            return "(" +
                   point.X.ToString("F3") + ", " +
                   point.Y.ToString("F3") + ", " +
                   point.Z.ToString("F3") + ")";
        }
    }
}