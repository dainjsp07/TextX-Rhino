using System;
using System.Collections.Generic;
using System.Drawing;
using Rhino.Display;
using Rhino.Geometry;

namespace TextX.UI
{
    /// <summary>
    /// Draws the pending result of an edit on top of the original objects using Rhino's own
    /// text renderer, so what you see is exactly what Apply will produce.
    /// </summary>
    public sealed class PreviewConduit : DisplayConduit
    {
        private readonly List<KeyValuePair<AnnotationBase, Guid>> _items = new List<KeyValuePair<AnnotationBase, Guid>>();
        private BoundingBox _bbox = BoundingBox.Empty;

        public Color Color = Color.FromArgb(230, 120, 0);

        public void SetItems(IEnumerable<KeyValuePair<AnnotationBase, Guid>> items)
        {
            _items.Clear();
            _bbox = BoundingBox.Empty;
            if (items == null) return;
            foreach (var kv in items)
            {
                if (kv.Key == null) continue;
                _items.Add(kv);
                _bbox.Union(kv.Key.GetBoundingBox(false));
            }
        }

        public bool HasItems { get { return _items.Count > 0; } }

        protected override void CalculateBoundingBox(CalculateBoundingBoxEventArgs e)
        {
            if (_bbox.IsValid) e.IncludeBoundingBox(_bbox);
        }

        protected override void DrawForeground(DrawEventArgs e)
        {
            var vpId = e.Viewport.Id;
            foreach (var kv in _items)
            {
                // page-space objects are only drawn in their own layout viewport
                if (kv.Value != Guid.Empty && kv.Value != vpId) continue;
                // model-space objects are not drawn in layout page space (only through details)
                if (kv.Value == Guid.Empty && e.Viewport.ViewportType == ViewportType.PageViewMainViewport) continue;
                try
                {
                    e.Display.DrawAnnotation(kv.Key, Color);
                }
                catch
                {
                    // never break the display pipeline
                }
            }
        }
    }
}
