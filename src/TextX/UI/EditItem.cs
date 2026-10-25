using System;
using TextX.Core;
using TextX.Integration;

namespace TextX.UI
{
    /// <summary>One text being edited in the Text X window (a loaded object or a new text).</summary>
    public sealed class EditItem
    {
        public Guid ObjectId = Guid.Empty;
        public uint DocSerial;
        public bool IsLeader;
        public string Source = string.Empty;
        public string Translation = string.Empty;
        public bool TranslationEdited;
        public TranslationDisplayMode Mode = TranslationDisplayMode.Dual;
        public PrintAlignment PrintAlign = PrintAlignment.Inherit;
        public bool AnchorToSourceBox = true;
        public bool Include = true;
        public string Provider = string.Empty;
        public string LoadNote;
        public TranslationRecord OriginalRecord;
        public TextFormat OriginalFormat;

        public bool IsNew { get { return ObjectId == Guid.Empty; } }

        // ---- grid bindings
        public bool? IncludeCell
        {
            get { return Include; }
            set { Include = value ?? false; }
        }

        public string KindCell
        {
            get { return IsNew ? "New" : (IsLeader ? "Leader" : "Text"); }
        }

        public string TextCell
        {
            get { return Shorten(Source); }
        }

        public string StateCell
        {
            get
            {
                if (string.IsNullOrEmpty(Translation)) return "-";
                return TranslationEdited ? "edited" : "ok";
            }
        }

        private static string Shorten(string s)
        {
            if (string.IsNullOrEmpty(s)) return "(empty)";
            var first = s.Replace("\r", string.Empty).Split('\n')[0];
            if (s.IndexOf('\n') >= 0) first += " ...";
            return first.Length > 48 ? first.Substring(0, 45) + "..." : first;
        }

        /// <summary>Builds the metadata record for this item (languages come from the window).</summary>
        public TranslationRecord BuildRecord(string sourceLang, string targetLang)
        {
            var r = OriginalRecord != null ? OriginalRecord.Clone() : new TranslationRecord();
            r.Source = Source ?? string.Empty;
            r.Translation = Translation ?? string.Empty;
            r.SourceLang = sourceLang;
            r.TargetLang = targetLang;
            r.Mode = Mode;
            r.PrintAlign = PrintAlign;
            r.AnchorToSourceBox = AnchorToSourceBox;
            r.ManuallyEdited = TranslationEdited;
            if (!string.IsNullOrEmpty(Provider)) r.Provider = Provider;
            r.State = TextState.Display;
            r.SavedHAlign = -1;
            r.SavedWrapped = -1;
            r.SavedFormatWidth = 0;
            r.AnchorShift = null;
            r.Touch();
            return r;
        }
    }
}
