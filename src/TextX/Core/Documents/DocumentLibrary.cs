using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace TextX.Core.Documents
{
    /// <summary>One document in the history list (read without loading the whole project).</summary>
    public sealed class LibraryEntry
    {
        public string Id;
        public string Name;
        public string SourcePath;
        public string SourceLang;
        public string TargetLang;
        public int PageCount;
        public int TranslatedBlocks;
        public int TotalBlocks;
        public DateTime Modified;
        public string Folder;
        public string ThumbnailPath { get { return Path.Combine(Folder, DocumentLibrary.ThumbnailFile); } }
        public bool IsTranslated { get { return TotalBlocks > 0 && TranslatedBlocks >= TotalBlocks; } }
    }

    /// <summary>
    /// The document history: every opened PDF gets a folder with a copy of the file, the extracted
    /// pages with their translations and edits (project.json) and a thumbnail. Reopening a document
    /// continues where the user left off, and works even if the original file was moved.
    /// </summary>
    public sealed class DocumentLibrary
    {
        public const string ProjectFile = "project.json";
        public const string IndexFile = "entry.json";
        public const string SourceFile = "source.pdf";
        public const string ThumbnailFile = "thumb.png";

        public DocumentLibrary(string folder)
        {
            Folder = folder;
        }

        public string Folder { get; private set; }

        public string FolderOf(string id)
        {
            return Path.Combine(Folder, id);
        }

        /// <summary>
        /// Starts a project for a PDF: copies it into the library (so the history keeps working when the
        /// original moves). A file already in the library is reopened instead of being added twice.
        /// </summary>
        public DocProject Add(string pdfPath, string targetLang)
        {
            var info = new FileInfo(pdfPath);
            var existing = Find(pdfPath, info.Length);
            if (existing != null)
            {
                var again = Load(existing.Id);
                if (again != null) return again;
            }
            var project = new DocProject
            {
                Name = Path.GetFileNameWithoutExtension(pdfPath),
                SourcePath = Path.GetFullPath(pdfPath),
                FileSize = info.Length,
                TargetLang = string.IsNullOrEmpty(targetLang) ? "en" : targetLang,
            };
            var dir = FolderOf(project.Id);
            Directory.CreateDirectory(dir);
            var stored = Path.Combine(dir, SourceFile);
            File.Copy(pdfPath, stored, true);
            project.StoredPdf = stored;
            Save(project);
            return project;
        }

        /// <summary>The entry for a file already in the library (same path and size), or null.</summary>
        public LibraryEntry Find(string pdfPath, long size)
        {
            string full;
            try { full = Path.GetFullPath(pdfPath); } catch (Exception) { return null; }
            return List().FirstOrDefault(e => string.Equals(e.SourcePath, full, StringComparison.OrdinalIgnoreCase) && Size(e) == size);
        }

        private static long Size(LibraryEntry e)
        {
            try
            {
                var stored = Path.Combine(e.Folder, SourceFile);
                return File.Exists(stored) ? new FileInfo(stored).Length : -1;
            }
            catch (Exception) { return -1; }
        }

        public void Save(DocProject project)
        {
            project.Modified = DateTime.UtcNow;
            var dir = FolderOf(project.Id);
            Directory.CreateDirectory(dir);
            if (!string.IsNullOrEmpty(project.StoredPdf) && !project.StoredPdf.StartsWith(dir, StringComparison.OrdinalIgnoreCase))
                project.StoredPdf = Path.Combine(dir, SourceFile);
            WriteAtomic(Path.Combine(dir, ProjectFile), project.ToJson());
            var entry = new Dictionary<string, object>
            {
                { "id", project.Id }, { "name", project.Name }, { "source", project.SourcePath },
                { "sourceLang", project.SourceLang }, { "targetLang", project.TargetLang },
                { "pages", project.PageCount }, { "translated", project.TranslatedBlocks }, { "blocks", project.TotalBlocks },
                { "modified", project.Modified.ToString("o") },
            };
            WriteAtomic(Path.Combine(dir, IndexFile), Json.Serialize(entry));
        }

        public DocProject Load(string id)
        {
            var dir = FolderOf(id);
            var path = Path.Combine(dir, ProjectFile);
            if (!File.Exists(path)) return null;
            var project = DocProject.FromJson(File.ReadAllText(path, Encoding.UTF8));
            var stored = Path.Combine(dir, SourceFile);
            if (File.Exists(stored)) project.StoredPdf = stored;
            return project;
        }

        /// <summary>All documents, most recent first.</summary>
        public List<LibraryEntry> List()
        {
            var result = new List<LibraryEntry>();
            if (!Directory.Exists(Folder)) return result;
            foreach (var dir in Directory.GetDirectories(Folder))
            {
                var path = Path.Combine(dir, IndexFile);
                if (!File.Exists(path)) continue;
                try
                {
                    var o = Json.Parse(File.ReadAllText(path, Encoding.UTF8)) as Dictionary<string, object>;
                    if (o == null) continue;
                    DateTime modified;
                    DateTime.TryParse(Str(o, "modified"), null, System.Globalization.DateTimeStyles.RoundtripKind, out modified);
                    result.Add(new LibraryEntry
                    {
                        Id = Str(o, "id"),
                        Name = Str(o, "name"),
                        SourcePath = Str(o, "source"),
                        SourceLang = Str(o, "sourceLang"),
                        TargetLang = Str(o, "targetLang"),
                        PageCount = Int(o, "pages"),
                        TranslatedBlocks = Int(o, "translated"),
                        TotalBlocks = Int(o, "blocks"),
                        Modified = modified,
                        Folder = dir,
                    });
                }
                catch (Exception) { /* a damaged entry does not hide the others */ }
            }
            return result.Where(e => !string.IsNullOrEmpty(e.Id)).OrderByDescending(e => e.Modified).ToList();
        }

        public void Delete(string id)
        {
            var dir = FolderOf(id);
            if (Directory.Exists(dir)) Directory.Delete(dir, true);
        }

        /// <summary>Total size of the library on disk, in bytes.</summary>
        public long SizeOnDisk()
        {
            if (!Directory.Exists(Folder)) return 0;
            return new DirectoryInfo(Folder).EnumerateFiles("*", SearchOption.AllDirectories).Sum(f => f.Length);
        }

        public void SaveThumbnail(string id, byte[] png)
        {
            if (png == null) return;
            var dir = FolderOf(id);
            Directory.CreateDirectory(dir);
            File.WriteAllBytes(Path.Combine(dir, ThumbnailFile), png);
        }

        private static void WriteAtomic(string path, string text)
        {
            var temp = path + ".tmp";
            File.WriteAllText(temp, text, new UTF8Encoding(false));
            if (File.Exists(path)) File.Replace(temp, path, null);
            else File.Move(temp, path);
        }

        private static string Str(Dictionary<string, object> o, string key)
        {
            object v;
            return o.TryGetValue(key, out v) && v != null ? Convert.ToString(v, System.Globalization.CultureInfo.InvariantCulture) : string.Empty;
        }

        private static int Int(Dictionary<string, object> o, string key)
        {
            object v;
            if (!o.TryGetValue(key, out v) || v == null) return 0;
            try { return Convert.ToInt32(v, System.Globalization.CultureInfo.InvariantCulture); } catch (Exception) { return 0; }
        }
    }
}
