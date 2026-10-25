using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace TextX.Core.Translation
{
    /// <summary>
    /// Persistent translation memory (provider + language pair + segment -> translation).
    /// Avoids repeated API calls while typing and lets previously translated labels work offline.
    /// </summary>
    public sealed class TranslationCache
    {
        private readonly object _lock = new object();
        private readonly Dictionary<string, string> _map = new Dictionary<string, string>(StringComparer.Ordinal);
        private readonly LinkedList<string> _order = new LinkedList<string>();
        private bool _dirty;

        public TranslationCache(int capacity = 20000)
        {
            Capacity = capacity;
        }

        public int Capacity { get; set; }
        public int Count { get { lock (_lock) return _map.Count; } }
        public bool IsDirty { get { return _dirty; } }

        public static string Key(string provider, string src, string tgt, string text)
        {
            return provider + "\u0001" + src + "\u0001" + tgt + "\u0001" + text;
        }

        public bool TryGet(string key, out string value)
        {
            lock (_lock) return _map.TryGetValue(key, out value);
        }

        public void Set(string key, string value)
        {
            lock (_lock)
            {
                if (!_map.ContainsKey(key)) _order.AddLast(key);
                _map[key] = value;
                while (_map.Count > Capacity && _order.Count > 0)
                {
                    _map.Remove(_order.First.Value);
                    _order.RemoveFirst();
                }
                _dirty = true;
            }
        }

        public void Clear()
        {
            lock (_lock)
            {
                _map.Clear();
                _order.Clear();
                _dirty = true;
            }
        }

        public void Save(string path)
        {
            List<object> items;
            lock (_lock)
            {
                items = new List<object>(_map.Count);
                foreach (var key in _order)
                {
                    var parts = key.Split('\u0001');
                    if (parts.Length != 4) continue;
                    items.Add(new List<object> { parts[0], parts[1], parts[2], parts[3], _map[key] });
                }
                _dirty = false;
            }
            var tmp = path + ".tmp";
            File.WriteAllText(tmp, Json.Serialize(items), new UTF8Encoding(false));
            if (File.Exists(path)) File.Delete(path);
            File.Move(tmp, path);
        }

        public static TranslationCache Load(string path, int capacity = 20000)
        {
            var cache = new TranslationCache(capacity);
            try
            {
                if (!File.Exists(path)) return cache;
                var root = Json.Parse(File.ReadAllText(path, Encoding.UTF8)) as List<object>;
                if (root == null) return cache;
                foreach (var item in root)
                {
                    var l = item as List<object>;
                    if (l == null || l.Count != 5) continue;
                    cache.Set(Key((string)l[0], (string)l[1], (string)l[2], (string)l[3]), (string)l[4]);
                }
                cache._dirty = false;
            }
            catch
            {
                // a corrupt cache is not fatal
            }
            return cache;
        }
    }
}
