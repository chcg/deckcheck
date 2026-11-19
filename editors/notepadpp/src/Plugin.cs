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
        static HashSet<string> _reached = new HashSet<string>(StringComparer.OrdinalIgnoreCase);  // files of the last checked models
        static readonly PaintMemo _memo = new PaintMemo();
        static readonly HashSet<string> _unsaved = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        static bool _failedOnce;

        public static string Version
        {
            get { return Assembly.GetExecutingAssembly().GetName().Version.ToString(3); }
        }

        static string PluginDir { get { return Path.GetDirectoryName(typeof(Plugin).Assembly.Location); } }
        static string EnginePath { get { return Path.Combine(Path.Combine(PluginDir, "engine"), "deckcheck.exe"); } }

        /// Save checks a deck, or any file the last checked model reached; a note beside the model starts nothing.
        public static bool ChecksOnSave(string path, IEnumerable<string> reached)
        {
            string ext = Path.GetExtension(path).ToLowerInvariant();
            if (DeckSuffixes.Contains(ext)) return true;
            return reached.Any(f => string.Equals(f, path, StringComparison.OrdinalIgnoreCase));
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

        static readonly Painter _painter = new Painter();
        static CheckResult _lastResult;
        const uint SCN_DWELLSTART = 2016, SCN_DWELLEND = 2017;
        const uint SCI_SETMOUSEDWELLTIME = 2264, SCI_GETMOUSEDWELLTIME = 2265, SCI_CALLTIPSHOW = 2200, SCI_CALLTIPCANCEL = 2201;
        const int SC_TIME_FOREVER = 10000000;

        public static void OnNotify(NmHdr h, IntPtr raw)
        {
            switch (h.Code)
            {
                case Win.NPPN_READY:
                    Entry.SetChecked(Entry.ItemDisable, Config.Disabled);
                    IntPtr unused = ThePanel.Handle;
                    _painter.Init(Entry.Npp, Entry.Views);
                    foreach (IntPtr sci in Entry.Views)
                        if (Win.Send(sci, SCI_GETMOUSEDWELLTIME, 0, 0).ToInt64() >= SC_TIME_FOREVER)
                            Win.Send(sci, SCI_SETMOUSEDWELLTIME, 500, 0);
                    break;
                case Win.NPPN_BUFFERACTIVATED:
                    PaintActive();
                    break;
                case SCN_DWELLSTART:
                    if (h.HwndFrom == Entry.CurrentScintilla()) ShowCalltip(h.HwndFrom, Marshal.ReadIntPtr(raw, 24).ToInt64());
                    break;
                case SCN_DWELLEND:
                    if (_calltipShown) { Win.Send(h.HwndFrom, SCI_CALLTIPCANCEL, 0, 0); _calltipShown = false; }
                    break;
                case Win.NPPN_FILESAVED:
                {
                    string path = PathOfBuffer(h.IdFrom);
                    if (path == null) break;
                    _unsaved.Remove(path);
                    if (!Config.Disabled && ChecksOnSave(path, _reached)) Check(path, false);
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

        static bool InModel(string path) { return _reached.Contains(path); }

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
            if (requested) ShowPanel();  // a save updates the panel but never forces it open
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
            CheckResult r = o.Result;  // paths already resolved on the worker thread
            _lastFolder = o.Folder;
            _lastResult = r;
            _resultGen++;
            _painter.Forget();
            _reached = new HashSet<string>(r.Models.SelectMany(m => m.ReachedPaths).Concat(new[] { path }),
                                           StringComparer.OrdinalIgnoreCase);
            List<string> unsaved = _unsaved.Where(InModel).OrderBy(x => x).ToList();
            System.Diagnostics.Stopwatch ui = System.Diagnostics.Stopwatch.StartNew();
            ThePanel.ShowResult(r, o.Seconds, path, unsaved);
            long uiMs = ui.ElapsedMilliseconds;
            Trace.Write("check_done", "file", path, "errors", r.Errors, "warnings", r.Warnings, "rows", r.Diagnostics.Count,
                        "models", r.Models.Select(m => m.Main).ToArray(), "alone", r.Models.Any(m => m.Alone),
                        "unsaved", unsaved.ToArray(), "seconds", o.Seconds, "panel_ms", uiMs);
            PaintActive();
            int row;  // end-to-end test hook, like the trace: off unless the variable is set
            if (int.TryParse(Environment.GetEnvironmentVariable("DECKCHECK_NPP_TEST_JUMP"), out row) && row < r.Diagnostics.Count)
                Jump(r.Diagnostics[row]);
        }

        static string _lastFolder;

        static void Jump(Finding f)
        {
            string file = f.Path ?? Path.GetFullPath(Path.Combine(_lastFolder ?? "", f.File));
            Win.SendMessage(Entry.Npp, Win.NPPM_DOOPEN, IntPtr.Zero, file);
            IntPtr sci = Entry.CurrentScintilla();
            int line = Math.Max(f.Line - 1, 0);
            Win.Send(sci, Win.SCI_GOTOLINE, line, 0);
            long start = Win.Send(sci, Win.SCI_POSITIONFROMLINE, line, 0).ToInt64();
            PaintedRange r = Painter.RangeOf(sci, CurrentEncoding(), f);
            if (r != null) Win.Send(sci, Win.SCI_SETSEL, r.Start, r.End);
            Win.Send(sci, Win.SCI_SCROLLCARET, 0, 0);
            Win.Send(sci, Win.SCI_GRABFOCUS, 0, 0);
            Trace.Write("jumped", "file", file, "line", f.Line,
                        "sel_start", Win.Send(sci, 2143, 0, 0).ToInt64() - start,
                        "sel_end", Win.Send(sci, 2145, 0, 0).ToInt64() - start,
                        "text", r == null ? "" : Painter.Text(sci, r));
        }

        static int CurrentEncoding()
        {
            IntPtr buffer = Win.Send(Entry.Npp, Win.NPPM_GETCURRENTBUFFERID, 0, 0);
            return Win.Send(Entry.Npp, Win.NPPMSG + 66 /* NPPM_GETBUFFERENCODING */, buffer.ToInt64(), 0).ToInt32();
        }

        /// Squiggles in the active document for the last result (spec §4); none while disabled.
        static int _resultGen;

        static void PaintActive()
        {
            string path = CurrentPath();
            if (string.IsNullOrEmpty(path)) return;
            IntPtr sci = Entry.CurrentScintilla();
            if (Config.Disabled)
            {
                _painter.Clear(sci);  // a tab painted before Disable is cleared when it becomes active
                Trace.Write("painted", "file", path, "count", 0, "capped", false, "first_text", "", "first_line", 0);
                return;
            }
            if (_lastResult == null || !_memo.Take(path, _resultGen)) return;  // painted for this result: its indicators moved with edits
            List<Finding> all = _lastResult.Diagnostics.Where(f => string.Equals(f.Path, path, StringComparison.OrdinalIgnoreCase)).ToList();
            IList<Finding> mine = Painter.Select(all, path, Painter.Cap);
            int n = _painter.Paint(sci, path, CurrentEncoding(), mine);
            Trace.Write("painted", "file", path, "count", n, "capped", all.Count > Painter.Cap,
                        "first_text", _painter.Ranges.Count > 0 ? Painter.Text(sci, _painter.Ranges[0]) : "",
                        "first_line", _painter.Ranges.Count > 0 ? _painter.Ranges[0].Finding.Line : 0);
            string at = Environment.GetEnvironmentVariable("DECKCHECK_NPP_TEST_DWELL");  // test hook, off unless set
            int k;
            if (int.TryParse(at, out k) && k >= 0 && k < _painter.Ranges.Count) ShowCalltip(sci, _painter.Ranges[k].Start);
        }

        static bool _calltipShown;

        static void ShowCalltip(IntPtr sci, long pos)
        {
            Finding hit = _painter.FindingAt(sci, CurrentPath(), pos);  // from the indicator under the mouse
            if (hit == null) return;
            string text = hit.Code + " " + hit.Severity + ": " + hit.Message;
            int cp = Win.Send(sci, Win.SCI_GETCODEPAGE, 0, 0).ToInt32();
            byte[] bytes = (cp == ColumnMap.CodepageUtf8 ? Encoding.UTF8 : Encoding.Default).GetBytes(text + "\0");
            IntPtr p = Marshal.AllocHGlobal(bytes.Length);
            try
            {
                Marshal.Copy(bytes, 0, p, bytes.Length);
                Win.Send(sci, SCI_CALLTIPSHOW, pos, p.ToInt64());
                _calltipShown = true;
            }
            finally { Marshal.FreeHGlobal(p); }
            Trace.Write("calltip", "text", text);
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
            Trace.Write("panel_shown");
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
            _memo.Forget();
            if (Config.Disabled)
            {
                CancelRun();
                foreach (IntPtr view in Entry.Views) _painter.Clear(view);  // both split views; other tabs on activation
                Trace.Write("painted", "file", CurrentPath(), "count", 0, "capped", false, "first_text", "", "first_line", 0);
                if (_panel != null) _panel.ShowMessage("deckcheck is disabled. Plugins > deckcheck > Disable turns it back on.");
            }
            else PaintActive();
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
