// Plugin settings in <Notepad++ plugins config dir>\deckcheck\settings.json: main decks per folder, and Disable.
// A missing or corrupt file means defaults (do no harm: settings never stop Notepad++ from working).
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Web.Script.Serialization;

namespace Deckcheck.Npp
{
    public sealed class Settings
    {
        readonly string _path;
        readonly Dictionary<string, List<string>> _folders = new Dictionary<string, List<string>>();
        public bool Disabled;

        Settings(string path) { _path = path; }

        static string Key(string folder)
        {
            return Path.GetFullPath(folder).TrimEnd('\\', '/').ToLowerInvariant();
        }

        public static Settings Load(string path)
        {
            Settings s = new Settings(path);
            try
            {
                if (!File.Exists(path)) return s;
                Dictionary<string, object> root =
                    new JavaScriptSerializer().DeserializeObject(File.ReadAllText(path)) as Dictionary<string, object>;
                if (root == null) return s;
                object d;
                if (root.TryGetValue("disabled", out d) && d is bool) s.Disabled = (bool)d;
                object f;
                if (root.TryGetValue("folders", out f) && f is Dictionary<string, object>)
                    foreach (KeyValuePair<string, object> kv in (Dictionary<string, object>)f)
                        s._folders[Key(kv.Key)] = ((IEnumerable)kv.Value).Cast<object>().Select(x => (string)x).ToList();
            }
            catch (Exception)
            {
                s._folders.Clear();
                s.Disabled = false;
            }
            return s;
        }

        public IList<string> MainDecksFor(string folder)
        {
            List<string> found;
            return _folders.TryGetValue(Key(folder), out found) ? found : new List<string>();
        }

        public void SetMainDecks(string folder, IEnumerable<string> decks)
        {
            List<string> list = decks.Select(x => x.Trim()).Where(x => x.Length > 0).ToList();
            if (list.Count == 0) _folders.Remove(Key(folder));
            else _folders[Key(folder)] = list;
        }

        public void Save()
        {
            Dictionary<string, object> root = new Dictionary<string, object>();
            root["disabled"] = Disabled;
            root["folders"] = _folders;
            Directory.CreateDirectory(Path.GetDirectoryName(_path));
            File.WriteAllText(_path, new JavaScriptSerializer().Serialize(root));
        }
    }
}
