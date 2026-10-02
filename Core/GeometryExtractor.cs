using System;
using System.Collections.Generic;
using Autodesk.Revit.DB;

namespace MEPAutoRouting.Core
{
    public class GeometryExtractor
    {
        private readonly Document _doc;

        public GeometryExtractor(Document doc)
        {
            _doc = doc;
        }

        /// <summary>
        /// Get bounding boxes from both host model and linked models.
        /// Returned bounding boxes are always in host model coordinates.
        /// </summary>
        public List<BoundingBoxXYZ> GetBoundingBoxes(BuiltInCategory category)
        {
            List<BoundingBoxXYZ> boxes = new List<BoundingBoxXYZ>();

            // 1. Host model elements
            boxes.AddRange(GetHostBoundingBoxes(category));

            // 2. Linked model elements
            boxes.AddRange(GetLinkedBoundingBoxes(category));

            return boxes;
        }

        private List<BoundingBoxXYZ> GetHostBoundingBoxes(BuiltInCategory category)
        {
            List<BoundingBoxXYZ> boxes = new List<BoundingBoxXYZ>();

            try
            {
                FilteredElementCollector collector =
                    new FilteredElementCollector(_doc)
                        .OfCategory(category)
                        .WhereElementIsNotElementType();

                foreach (Element elem in collector)
                {
                    if (elem is Wall) continue;
                    BoundingBoxXYZ bb = elem.get_BoundingBox(null);
                    if (bb == null) continue;
                    var world = BoundingBoxUtils.GetWorldBounds(bb);
                    boxes.Add(CreateBounds(world.Min, world.Max));
                }
            }
            catch
            {
                // Ignore category errors in host model.
            }

            return boxes;
        }

        private List<BoundingBoxXYZ> GetLinkedBoundingBoxes(BuiltInCategory category)
        {
            List<BoundingBoxXYZ> boxes = new List<BoundingBoxXYZ>();

            try
            {
                FilteredElementCollector linkCollector =
                    new FilteredElementCollector(_doc)
                        .OfClass(typeof(RevitLinkInstance));

                foreach (Element linkElem in linkCollector)
                {
                    RevitLinkInstance linkInstance = linkElem as RevitLinkInstance;
                    if (linkInstance == null)
                        continue;

                    Document linkDoc = linkInstance.GetLinkDocument();
                    if (linkDoc == null)
                        continue;

                    Transform linkTransform = linkInstance.GetTransform();

                    FilteredElementCollector linkedElements =
                        new FilteredElementCollector(linkDoc)
                            .OfCategory(category)
                            .WhereElementIsNotElementType();

                    foreach (Element linkedElem in linkedElements)
                    {
                        if (linkedElem is Wall) continue;
                        BoundingBoxXYZ linkBox = linkedElem.get_BoundingBox(null);
                        if (linkBox == null)
                            continue;

                        var world = BoundingBoxUtils.GetWorldBounds(linkBox, linkTransform);
                        boxes.Add(CreateBounds(world.Min, world.Max));
                    }
                }
            }
            catch
            {
                // Ignore linked model extraction errors.
            }

            return boxes;
        }

        /// <summary>
        /// Transform a linked model bounding box into host coordinates.
        /// BoundingBoxXYZ is axis-aligned, so we transform all 8 corners
        /// then create a new axis-aligned box around the transformed points.
        /// </summary>
        private BoundingBoxXYZ CreateBounds(XYZ min, XYZ max)
        {
            BoundingBoxXYZ newBox = new BoundingBoxXYZ();
            newBox.Min = min;
            newBox.Max = max;
            return newBox;
        }
    }
}