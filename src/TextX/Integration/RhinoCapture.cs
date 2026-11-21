using System;
using Rhino;
using TextX.Core.Tools;

namespace TextX.Integration
{
    /// <summary>Pictures of Rhino views (for reading the text they show).</summary>
    public static class RhinoCapture
    {
        /// <summary>The active Rhino view as a picture, at twice its size so small text reads well.</summary>
        public static GrayImage ActiveView(RhinoDoc doc)
        {
            var view = doc == null ? null : doc.Views.ActiveView;
            if (view == null) return null;
            var size = view.ActiveViewport.Size;
            using (var bmp = view.CaptureToBitmap(new System.Drawing.Size(Math.Max(64, size.Width * 2), Math.Max(64, size.Height * 2))))
                return bmp == null ? null : DocumentServices.FromBitmap(bmp);
        }
    }
}
