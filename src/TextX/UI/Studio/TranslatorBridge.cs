using System;
using System.Collections.Generic;
using Eto.Forms;
using TextX.UI.Controls;
using TextX.UI.Theming;

namespace TextX.UI.Studio
{
    /// <summary>
    /// What the translator on Home needs from Rhino: the selected objects, the layers, Rhino's text
    /// properties, the viewport preview and the print tools. The plug-in sets <see cref="StudioWindow.BridgeFactory"/>
    /// to its Rhino implementation; this base class is what Studio uses on its own (previews, tests).
    /// </summary>
    public class TranslatorBridge
    {
        protected StudioWindow Window { get; private set; }

        public event EventHandler StyleChanged;

        public virtual void Attach(StudioWindow window) { Window = window; }

        public virtual void Detach() { }

        /// <summary>True when Rhino is there (objects, layers, text properties).</summary>
        public virtual bool InRhino { get { return false; } }

        /// <summary>Rhino's text properties: annotation style, font, height, alignment, mask, layer and color.</summary>
        public virtual Control CreateFormatSections(ThemeScope scope) { return null; }

        /// <summary>Rhino's leader properties: arrowhead, size, curve, landing, text alignment.</summary>
        public virtual Control CreateLeaderSections(ThemeScope scope) { return null; }

        /// <summary>The look of the text for the one-line previews.</summary>
        public virtual LineStyle ReadStyle() { return new LineStyle(); }

        protected void RaiseStyleChanged()
        {
            var h = StyleChanged;
            if (h != null) h(this, EventArgs.Empty);
        }

        /// <summary>The layers of the open Rhino file: index and full path (Parent::Child).</summary>
        public virtual IList<KeyValuePair<int, string>> Layers() { return new List<KeyValuePair<int, string>>(); }

        /// <summary>The layer for placed text and leaders; -1 = the current layer.</summary>
        public virtual int LayerIndex { get { return -1; } set { } }

        /// <summary>Loads the text and leaders selected in Rhino into the translator.</summary>
        public virtual void LoadSelected()
        {
            if (Window != null) Window.TranslatorMessage("In Rhino, Load selected brings the selected text and leaders here.", StatusText.Level.Info);
        }

        public virtual void SelectInRhino(TranslatorItem item) { }

        /// <summary>The loaded objects or their texts changed (the viewport preview follows).</summary>
        public virtual void ItemsChanged() { }

        /// <summary>A new text starts: text properties go back to the current annotation style.</summary>
        public virtual void ResetFormat() { }

        public virtual string PrintInfo() { return "Text set to show the source on screen switches to the translation when you print or export a PDF, then back."; }

        public virtual bool PrintPreviewOn { get { return false; } }

        public virtual void ManageObjects() { }
    }
}
