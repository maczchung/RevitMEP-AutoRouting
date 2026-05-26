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
                    BoundingBoxXYZ bb = elem.get_BoundingBox(null);
                    if (bb == null)
                        continue;

                    boxes.Add(bb);
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
                        BoundingBoxXYZ linkBox = linkedElem.get_BoundingBox(null);
                        if (linkBox == null)
                            continue;

                        BoundingBoxXYZ transformedBox =
                            TransformBoundingBox(linkBox, linkTransform);

                        if (transformedBox != null)
                            boxes.Add(transformedBox);
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
        private BoundingBoxXYZ TransformBoundingBox(
            BoundingBoxXYZ box,
            Transform transform)
        {
            if (box == null || transform == null)
                return null;

            XYZ min = box.Min;
            XYZ max = box.Max;

            XYZ[] corners = new XYZ[]
            {
                new XYZ(min.X, min.Y, min.Z),
                new XYZ(max.X, min.Y, min.Z),
                new XYZ(min.X, max.Y, min.Z),
                new XYZ(max.X, max.Y, min.Z),

                new XYZ(min.X, min.Y, max.Z),
                new XYZ(max.X, min.Y, max.Z),
                new XYZ(min.X, max.Y, max.Z),
                new XYZ(max.X, max.Y, max.Z)
            };

            double minX = double.MaxValue;
            double minY = double.MaxValue;
            double minZ = double.MaxValue;

            double maxX = double.MinValue;
            double maxY = double.MinValue;
            double maxZ = double.MinValue;

            foreach (XYZ corner in corners)
            {
                XYZ p = transform.OfPoint(corner);

                minX = Math.Min(minX, p.X);
                minY = Math.Min(minY, p.Y);
                minZ = Math.Min(minZ, p.Z);

                maxX = Math.Max(maxX, p.X);
                maxY = Math.Max(maxY, p.Y);
                maxZ = Math.Max(maxZ, p.Z);
            }

            BoundingBoxXYZ newBox = new BoundingBoxXYZ();
            newBox.Min = new XYZ(minX, minY, minZ);
            newBox.Max = new XYZ(maxX, maxY, maxZ);

            return newBox;
        }
    }
}