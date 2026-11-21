using System;
using Eto.Drawing;
using Eto.Forms;
using Rhino.UI;
using TextX.UI.Controls;
using TextX.UI.Theming;

namespace TextX.UI.Studio
{
    /// <summary>Asks for a word or phrase and how it must always be translated.</summary>
    public sealed class GlossaryTermDialog : Dialog<bool>
    {
        private readonly ThemeScope _scope = new ThemeScope();
        private readonly TextBox _term = new TextBox();
        private readonly TextBox _translation = new TextBox();

        public string Term { get { return (_term.Text ?? string.Empty).Trim(); } }
        public string Translation { get { return (_translation.Text ?? string.Empty).Trim(); } }

        public GlossaryTermDialog(string sourceLanguage, string targetLanguage)
        {
            Title = "Glossary term";
            Resizable = false;
            Padding = new Padding(18);
            this.UseRhinoStyle();
            _scope.Bind(this, (x, p) => x.BackgroundColor = p.Window);
            var ok = new FlatButton("Add", IconKind.Check, ButtonKind.Primary, 32);
            ok.Click += (s, e) =>
            {
                if (Term.Length == 0 || Translation.Length == 0) return;
                Close(true);
            };
            var cancel = new FlatButton("Cancel", IconKind.None, ButtonKind.Secondary, 32);
            cancel.Click += (s, e) => Close(false);
            _term.Width = 320;
            _translation.Width = 320;
            var layout = new TableLayout { Spacing = new Size(10, 10) };
            layout.Rows.Add(new TableRow(Ui.Label(_scope, "Whenever this appears in the original, it is translated your way — also inside sentences.", 12.5f, false, p => p.Muted, true)));
            layout.Rows.Add(new TableRow(Ui.Label(_scope, "In " + (sourceLanguage ?? "the original"), 12.5f, true)));
            layout.Rows.Add(new TableRow(_term));
            layout.Rows.Add(new TableRow(Ui.Label(_scope, "Always translate into " + (targetLanguage ?? "the translation") + " as", 12.5f, true)));
            layout.Rows.Add(new TableRow(_translation));
            layout.Rows.Add(new TableRow(Ui.Row(8, Ui.Spring(), cancel, ok)));
            Content = layout;
            DefaultButton = null;
            Shown += (s, e) => _term.Focus();
        }
    }

    /// <summary>Shows a longer text (a summary) with Copy and Save.</summary>
    public sealed class TextDialog : Dialog
    {
        private readonly ThemeScope _scope = new ThemeScope();

        public TextDialog(string title, string text, string note)
        {
            Title = title;
            Resizable = true;
            ClientSize = new Size(720, 560);
            Padding = new Padding(18);
            this.UseRhinoStyle();
            _scope.Bind(this, (x, p) => x.BackgroundColor = p.Window);
            var area = Ui.TextArea(_scope, 13.5f);
            area.Text = text ?? string.Empty;
            area.ReadOnly = true;
            var copy = new FlatButton("Copy", IconKind.None, ButtonKind.Secondary, 32);
            copy.Click += (s, e) => { try { Clipboard.Instance.Text = text ?? string.Empty; } catch { /* clipboard busy */ } };
            var save = new FlatButton("Save...", IconKind.Download, ButtonKind.Secondary, 32);
            save.Click += (s, e) =>
            {
                var dlg = new Eto.Forms.SaveFileDialog { Title = "Save the summary", FileName = "Summary.txt" };
                dlg.Filters.Add(new FileFilter("Text file", ".txt"));
                if (dlg.ShowDialog(this) != DialogResult.Ok || string.IsNullOrEmpty(dlg.FileName)) return;
                var path = dlg.FileName.EndsWith(".txt", StringComparison.OrdinalIgnoreCase) ? dlg.FileName : dlg.FileName + ".txt";
                System.IO.File.WriteAllText(path, text ?? string.Empty, new System.Text.UTF8Encoding(true));
            };
            var close = new FlatButton("Close", IconKind.None, ButtonKind.Primary, 32);
            close.Click += (s, e) => Close();
            var layout = new TableLayout { Spacing = new Size(0, 10) };
            layout.Rows.Add(new TableRow(Ui.FieldBox(area)) { ScaleHeight = true });
            if (!string.IsNullOrEmpty(note)) layout.Rows.Add(new TableRow(Ui.Label(_scope, note, 12f, false, p => p.Muted, true)));
            layout.Rows.Add(new TableRow(Ui.Row(8, copy, save, Ui.Spring(), close)));
            Content = layout;
        }
    }
}
