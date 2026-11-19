// What the plugin does: menu commands and notifications, wired to the engine runner and the panel. Runs on Notepad++'s
// UI thread; engine results come back on a worker thread and are marshalled with BeginInvoke (spec §5).
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Forms;

namespace Deckcheck.Npp
{
    public static class Plugin
    {
        static readonly string[] DeckSuffixes = { ".k", ".key", ".dyn", ".inc" };
        static Settings _settings;
        static Panel _panel;
        static IntPtr _dockData = IntPtr.Zero;
        static EngineRun _run;
        static int _gen;
        static List<string> _modelFolders = new List<string>();
        static readonly HashSet<string> _unsaved = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        static bool _failedOnce;

        public static string Version
        {
            get { return Assembly.GetExecutingAssembly().GetName().Version.ToString(3); }
        }

        static string PluginDir { get { return Path.GetDirectoryName(typeof(Plugin).Assembly.Location); } }
        static string EnginePath { get { return Path.Combine(Path.Combine(PluginDir, "engine"), "deckcheck.exe"); } }

        public static bool ChecksOnSave(string path, IEnumerable<string> modelFolders)
        {
            string ext = Path.GetExtension(path).ToLowerInvariant();
            if (DeckSuffixes.Contains(ext)) return true;
            string folder = Path.GetDirectoryName(path).TrimEnd('\\');
            return modelFolders.Any(f => string.Equals(f.TrimEnd('\\'), folder, StringComparison.OrdinalIgnoreCase));
        }

        static Settings Config
        {
            get
            {
                if (_settings == null)
                {
                    StringBuilder dir = new StringBuilder(32768);
                    Win.SendMessage(Entry.Npp, Win.NPPM_GETPLUGINSCONFIGDIR, new IntPtr(dir.Capacity), dir);
                    string root = dir.Length > 0 ? dir.ToString() : PluginDir;
                    _settings = Settings.Load(Path.Combine(Path.Combine(root, "deckcheck"), "settings.json"));
                }
                return _settings;
            }
        }

        static Panel ThePanel
        {
            get
            {
                if (_panel == null)
                {
                    _panel = new Panel();
                    IntPtr unused = _panel.Handle;  // create the window now, on the UI thread, for BeginInvoke
                    _panel.JumpTo += f => Safe(() => Jump(f));
                }
                return _panel;
            }
        }

        static void Safe(Action a)
        {
            try { a(); } catch (Exception e) { Fail(e); }
        }

        public static void OnNotify(NmHdr h)
        {
            switch (h.Code)
            {
                case Win.NPPN_READY:
                    Entry.SetChecked(Entry.ItemDisable, Config.Disabled);
                    IntPtr unused = ThePanel.Handle;
                    break;
                case Win.NPPN_FILESAVED:
                {
                    string path = PathOfBuffer(h.IdFrom);
                    if (path == null) break;
                    _unsaved.Remove(path);
                    if (!Config.Disabled && ChecksOnSave(path, _modelFolders)) Check(path, false);
                    break;
                }
                case Win.NPPN_SHUTDOWN:
                    CancelRun();
                    break;
                case Win.SCN_SAVEPOINTLEFT:
                case Win.SCN_SAVEPOINTREACHED:
                {
                    string path = CurrentPath();
                    if (string.IsNullOrEmpty(path)) break;
                    if (h.Code == Win.SCN_SAVEPOINTLEFT)
                    {
                        _unsaved.Add(path);
                        if (_panel != null && InModel(path)) _panel.MarkEdited();
                    }
                    else _unsaved.Remove(path);
                    break;
                }
            }
        }

        static bool InModel(string path)
        {
            string folder = Path.GetDirectoryName(path).TrimEnd('\\');
            return _modelFolders.Any(f => string.Equals(f.TrimEnd('\\'), folder, StringComparison.OrdinalIgnoreCase));
        }

        static string CurrentPath()
        {
            StringBuilder s = new StringBuilder(32768);
            Win.SendMessage(Entry.Npp, Win.NPPM_GETFULLCURRENTPATH, new IntPtr(s.Capacity), s);
            return s.ToString();
        }

        static string PathOfBuffer(UIntPtr buffer)
        {
            IntPtr id = new IntPtr((long)buffer.ToUInt64());
            int len = Win.SendMessage(Entry.Npp, Win.NPPM_GETFULLPATHFROMBUFFERID, id, IntPtr.Zero).ToInt32();
            if (len <= 0) return null;
            StringBuilder s = new StringBuilder(len + 1);
            Win.SendMessage(Entry.Npp, Win.NPPM_GETFULLPATHFROMBUFFERID, id, s);
            return s.ToString();
        }

        public static void CheckCurrent()
        {
            string path = CurrentPath();
            if (Config.Disabled)
            {
                ShowPanel();
                ThePanel.ShowMessage("deckcheck is disabled. Plugins > deckcheck > Disable turns it back on.");
                return;
            }
            Check(path, true);
        }

        static void Check(string path, bool requested)
        {
            if (string.IsNullOrEmpty(path) || !Path.IsPathRooted(path) || !File.Exists(path))
            {
                if (requested)
                {
                    ShowPanel();
                    ThePanel.ShowMessage("Save the file first: deckcheck checks files as they are on disk.");
                }
                return;
            }
            CancelRun();
            int gen = ++_gen;
            ShowPanel();
            ThePanel.ShowRunning(path);
            Trace.Write("check_started", "file", path);
            IList<string> mains = Config.MainDecksFor(Path.GetDirectoryName(path));
            Panel panel = ThePanel;
            _run = EngineRun.Start(EnginePath, path, mains, o =>
            {
                try { panel.BeginInvoke((MethodInvoker)delegate { Safe(() => Done(gen, path, o)); }); }
                catch (Exception) { }  // the panel is gone: Notepad++ is closing
            });
        }

        static void Done(int gen, string path, RunOutcome o)
        {
            if (gen != _gen) return;  // a newer check started: this result must never reach the panel
            _run = null;
            if (o.Cancelled) { Trace.Write("check_cancelled", "file", path); return; }
            if (o.Error != null)
            {
                ThePanel.ShowMessage("deckcheck could not check " + Path.GetFileName(path) + ": " + o.Error);
                Trace.Write("check_error", "file", path, "error", o.Error);
                return;
            }
            CheckResult r = o.Result;
            _lastFolder = o.Folder;
            _modelFolders = r.Models.Select(m => Path.GetDirectoryName(Path.GetFullPath(Path.Combine(o.Folder, m.Main))))
                                    .Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            List<string> unsaved = _unsaved.Where(InModel).OrderBy(x => x).ToList();
            ThePanel.ShowResult(r, o.Seconds, path, unsaved);
            Trace.Write("check_done", "file", path, "errors", r.Errors, "warnings", r.Warnings, "rows", r.Diagnostics.Count,
                        "models", r.Models.Select(m => m.Main).ToArray(), "alone", r.Models.Any(m => m.Alone),
                        "unsaved", unsaved.ToArray(), "seconds", o.Seconds);
            int row;  // end-to-end test hook, like the trace: off unless the variable is set
            if (int.TryParse(Environment.GetEnvironmentVariable("DECKCHECK_NPP_TEST_JUMP"), out row) && row < r.Diagnostics.Count)
                Jump(r.Diagnostics[row]);
        }

        static string _lastFolder;

        static void Jump(Finding f)
        {
            string file = Path.GetFullPath(Path.Combine(_lastFolder ?? "", f.File));
            Win.SendMessage(Entry.Npp, Win.NPPM_DOOPEN, IntPtr.Zero, file);
            IntPtr sci = Entry.CurrentScintilla();
            int line = Math.Max(f.Line - 1, 0);
            Win.Send(sci, Win.SCI_GOTOLINE, line, 0);
            long start = Win.Send(sci, Win.SCI_POSITIONFROMLINE, line, 0).ToInt64();
            Win.Send(sci, Win.SCI_SETSEL, start + f.Column - 1, start + f.EndColumn - 1);
            Win.Send(sci, Win.SCI_SCROLLCARET, 0, 0);
            Win.Send(sci, Win.SCI_GRABFOCUS, 0, 0);
            Trace.Write("jumped", "file", file, "line", f.Line,
                        "sel_start", Win.Send(sci, 2143, 0, 0).ToInt64() - start,
                        "sel_end", Win.Send(sci, 2145, 0, 0).ToInt64() - start);
        }

        static void CancelRun()
        {
            if (_run != null) { _run.Cancel(); _run = null; }
            _gen++;
        }

        public static void ShowPanel()
        {
            Panel p = ThePanel;
            if (_dockData == IntPtr.Zero)
            {
                TbData d = new TbData();
                d.Client = p.Handle;
                d.Name = "deckcheck";
                d.DlgId = Entry.ItemShow;
                d.Mask = Win.DWS_DF_CONT_BOTTOM;
                d.ModuleName = "deckcheck.dll";
                d.AddInfo = "";
                _dockData = Marshal.AllocHGlobal(Marshal.SizeOf(typeof(TbData)));  // kept for Notepad++'s lifetime
                Marshal.StructureToPtr(d, _dockData, false);
                Win.SendMessage(Entry.Npp, Win.NPPM_DMMREGASDCKDLG, IntPtr.Zero, _dockData);
            }
            Win.SendMessage(Entry.Npp, Win.NPPM_DMMSHOW, IntPtr.Zero, p.Handle);
        }

        public static void EditMainDecks()
        {
            string path = CurrentPath();
            if (string.IsNullOrEmpty(path) || !Path.IsPathRooted(path))
            {
                MessageBox.Show("Save the file first: main decks are set per folder.", "deckcheck");
                return;
            }
            string folder = Path.GetDirectoryName(path);
            using (MainDecksDialog d = new MainDecksDialog(folder, Config.MainDecksFor(folder)))
            {
                if (d.ShowDialog() != DialogResult.OK) return;
                Config.SetMainDecks(folder, d.Decks);
                Config.Save();
                Trace.Write("main_decks", "folder", folder, "decks", d.Decks.ToArray());
            }
        }

        public static void ToggleDisabled()
        {
            Config.Disabled = !Config.Disabled;
            Config.Save();
            Entry.SetChecked(Entry.ItemDisable, Config.Disabled);
            if (Config.Disabled)
            {
                CancelRun();
                if (_panel != null) _panel.ShowMessage("deckcheck is disabled. Plugins > deckcheck > Disable turns it back on.");
            }
            Trace.Write("disabled", "on", Config.Disabled);
        }

        public static void Fail(Exception e)
        {
            Trace.Write("internal_error", "error", e.ToString());
            if (_failedOnce) return;  // reported once, then deckcheck steps aside (spec §5)
            _failedOnce = true;
            try { if (_panel != null) _panel.ShowMessage("deckcheck: internal error: " + e.Message + " (please report this)"); }
            catch (Exception) { }
        }
    }
}
