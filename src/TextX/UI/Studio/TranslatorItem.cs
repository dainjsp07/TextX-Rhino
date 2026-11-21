using System;
using TextX.Core;

namespace TextX.UI.Studio
{
    /// <summary>One text in the translator on Home: a new text, or a text or leader loaded from Rhino.</summary>
    public sealed class TranslatorItem
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
        /// <summary>The object's formatting when it was loaded (kept for the Rhino side; a TextFormat).</summary>
        public object OriginalFormat;

        public bool IsNew { get { return ObjectId == Guid.Empty; } }

        /// <summary>The first line of the text, shortened, for menus.</summary>
        public string Title
        {
            get
            {
                if (string.IsNullOrWhiteSpace(Source)) return "(empty)";
                var first = Source.Replace("\r", string.Empty).Split('\n')[0];
                if (Source.IndexOf('\n') >= 0) first += " ...";
                return first.Length > 48 ? first.Substring(0, 45) + "..." : first;
            }
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
