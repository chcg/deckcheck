// The words the findings panel shows, kept apart from the window so they can be unit-tested.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;

namespace Deckcheck.Npp
{
    public static class PanelText
    {
        static string Plural(int n, string word) { return n + " " + word + (n == 1 ? "" : "s"); }

        public static string Summary(CheckResult r, double seconds, string file)
        {
            string secs = seconds.ToString("0.0", CultureInfo.InvariantCulture) + " s";
            string counts = Plural(r.Errors, "error") + ", " + Plural(r.Warnings, "warning") + ", " + secs;
            if (r.Models.Count == 1 && r.Models[0].Alone)
                return "Checked alone: no main deck in this folder includes " + Path.GetFileName(file)
                       + " (Plugins > deckcheck > Main decks...). " + counts;
            string mains = string.Join(", ", r.Models.Select(m => m.Main).ToArray());
            return mains + ": " + Plural(r.Models.Sum(m => m.Files), "file") + ", " + counts;
        }

        public static string[] Row(Finding f)
        {
            return new[] { f.Severity, f.Code, f.File + ":" + f.Line, f.Message };
        }

        public static string Notes(CheckResult r, IEnumerable<string> unsavedFiles)
        {
            StringBuilder s = new StringBuilder();
            foreach (string u in unsavedFiles)
                s.Append("Unsaved changes in " + Path.GetFileName(u) + " are not checked.").Append("\r\n");
            if (r.NotChecked.Count > 0)
            {
                s.Append("Not checked:").Append("\r\n");
                foreach (NotChecked n in r.NotChecked)
                {
                    string more = n.Count > n.Reasons.Count ? " (+" + (n.Count - n.Reasons.Count) + " more)" : "";
                    s.Append("  " + n.What + ": " + string.Join("; ", n.Reasons.ToArray()) + more).Append("\r\n");
                }
            }
            return s.ToString().TrimEnd();
        }
    }
}
