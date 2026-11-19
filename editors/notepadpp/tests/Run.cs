// Unit test runner (no test framework: tools that ship with Windows only). Usage: Run.exe <data dir> <FakeEngine.exe>
// Every public static void method named Test* in this file is run; the exit code is the number of failures.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Deckcheck.Npp;

public static class Run
{
    static string Data, Fake;
    static int failures;

    public static int Main(string[] args)
    {
        Data = args[0];
        Fake = args[1];
        foreach (MethodInfo m in typeof(Run).GetMethods(BindingFlags.Public | BindingFlags.Static)
                                            .Where(x => x.Name.StartsWith("Test")).OrderBy(x => x.Name))
        {
            try { m.Invoke(null, null); Console.WriteLine("ok   " + m.Name); }
            catch (TargetInvocationException e)
            {
                failures++;
                Console.WriteLine("FAIL " + m.Name + ": " + e.InnerException.Message);
            }
        }
        Console.WriteLine(failures == 0 ? "all passed" : failures + " failed");
        return failures;
    }

    static void Eq(object want, object got, string what)
    {
        if (!object.Equals(want, got)) throw new Exception(what + ": want <" + want + "> got <" + got + ">");
    }

    static void Throws(Action a, string contains)
    {
        try { a(); }
        catch (EngineError e)
        {
            if (!e.Message.Contains(contains)) throw new Exception("message <" + e.Message + "> lacks <" + contains + ">");
            return;
        }
        throw new Exception("no EngineError for <" + contains + ">");
    }

    // N3 Task 1: column conversion
    static byte[] L1(string s) { return System.Text.Encoding.GetEncoding(28591).GetBytes(s); }
    static byte[] U8(string s) { return System.Text.Encoding.UTF8.GetBytes(s); }

    public static void TestColumnsAnsiBufferAreIdentity()
    {
        Eq(5, ColumnMap.ToBuffer(2, 6, 0, 0, L1("20°C x")), "ansi");
    }

    public static void TestColumnsUtf8BufferAreIdentity()
    {
        // the engine reads the UTF-8 file's bytes as Latin-1: the degree sign counts 2 bytes on both sides
        Eq(6, ColumnMap.ToBuffer(2, 7, 4, 65001, U8("20°C x")), "utf-8");
    }

    public static void TestColumnsBomShiftsLineOneOnly()
    {
        Eq(0, ColumnMap.ToBuffer(1, 4, 1, 65001, U8("*PART")), "line 1 after the 3 BOM bytes");
        Eq(3, ColumnMap.ToBuffer(2, 4, 1, 65001, U8("*PART")), "line 2");
    }

    public static void TestColumnsAnsiConvertedToUtf8CountCharacters()
    {
        // disk: one byte per character; buffer: UTF-8, so after the degree sign each column is one byte further
        Eq(6, ColumnMap.ToBuffer(2, 6, 0, 65001, U8("20°C x")), "converted");
    }

    public static void TestColumnsUtf16CannotBeMapped()
    {
        foreach (int enc in new[] { 2, 3, 6, 7 }) Eq(-1, ColumnMap.ToBuffer(2, 1, enc, 65001, U8("x")), "utf-16 " + enc);
    }

    public static void TestColumnsClampToTheLine()
    {
        Eq(3, ColumnMap.ToBuffer(2, 40, 0, 0, L1("abc")), "clamped");
    }

    // N3 Task 2: which findings get painted
    public static void TestPainterSelectsThisFilesFindingsInOrderCapped()
    {
        List<Finding> all = new List<Finding>();
        for (int i = 5; i > 0; i--) all.Add(new Finding { File = "sub/a.k", Line = i, Column = 1, EndColumn = 2 });
        all.Add(new Finding { File = "main.k", Line = 1, Column = 1, EndColumn = 2 });
        IList<Finding> got = Painter.Select(all, "sub/a.k", 3);
        Eq("1,2,3", string.Join(",", got.Select(f => f.Line.ToString()).ToArray()), "first three lines of sub/a.k");
    }

    // Task 4: panel text
    public static void TestSummaryForAModel()
    {
        CheckResult r = Findings.Parse(File.ReadAllText(Path.Combine(Data, "seeded.json")));
        Eq("main.k: 1 file, " + r.Errors + " errors, " + r.Warnings + " warnings, 12.4 s",
           PanelText.Summary(r, 12.43, @"C:\m\main.k"), "summary");
    }

    public static void TestSummaryForAFileCheckedAlone()
    {
        CheckResult r = Findings.Parse(File.ReadAllText(Path.Combine(Data, "seeded.json")));
        r.Models[0].Alone = true;
        r.Models[0].Main = "notes.inc";
        Eq(true, PanelText.Summary(r, 1.0, @"C:\m\notes.inc").StartsWith(
            "Checked alone: no main deck in this folder includes notes.inc (Plugins > deckcheck > Main decks...)"),
           "alone summary: " + PanelText.Summary(r, 1.0, @"C:\m\notes.inc"));
    }

    public static void TestRowText()
    {
        Finding f = new Finding { File = "sub/inc.k", Line = 8, Column = 29, EndColumn = 31, Severity = "error",
                                  Code = "DC001", Message = "*PART mid refers to MAT 99" };
        Eq("error|DC001|sub/inc.k:8|*PART mid refers to MAT 99", string.Join("|", PanelText.Row(f)), "row");
    }

    public static void TestNotesListNotCheckedAndUnsaved()
    {
        CheckResult r = Findings.Parse(File.ReadAllText(Path.Combine(Data, "seeded.json")));
        string notes = PanelText.Notes(r, new[] { @"C:\m\part.k" });
        Eq(true, notes.Contains("Unsaved changes in part.k are not checked."), "unsaved: " + notes);
        Eq(true, notes.Contains("Not checked:") && notes.Contains(r.NotChecked[0].What), "not checked: " + notes);
    }

    // Task 5: settings
    public static void TestSettingsRoundTrip()
    {
        string p = Temp("settings.json");
        Settings s = Settings.Load(p);
        Eq(false, s.Disabled, "default disabled");
        Eq(0, s.MainDecksFor(@"C:\Models\Car").Count, "default main decks");
        s.SetMainDecks(@"C:\Models\Car", new[] { "main.k", "sub\\other.k" });
        s.Disabled = true;
        s.Save();
        Settings t = Settings.Load(p);
        Eq(true, t.Disabled, "disabled");
        Eq("main.k;sub\\other.k", string.Join(";", t.MainDecksFor(@"c:\models\car\")), "main decks, case and slash");
    }

    public static void TestCorruptSettingsFallBackToDefaults()
    {
        string p = Temp("bad.json");
        File.WriteAllText(p, "{ not json");
        Settings s = Settings.Load(p);
        Eq(false, s.Disabled, "disabled");
        Eq(0, s.MainDecksFor(@"C:\x").Count, "main decks");
    }

    public static void TestCheckOnSaveRule()
    {
        Eq(true, Plugin.ChecksOnSave(@"C:\m\main.k", new string[0]), ".k");
        Eq(true, Plugin.ChecksOnSave(@"C:\m\MAIN.KEY", new string[0]), ".key");
        Eq(true, Plugin.ChecksOnSave(@"C:\m\part.inc", new string[0]), ".inc");
        Eq(false, Plugin.ChecksOnSave(@"C:\m\notes.txt", new string[0]), ".txt");
        Eq(true, Plugin.ChecksOnSave(@"C:\m\mesh.dat", new[] { @"c:\M\mesh.dat" }), "a file the last model reached");
        Eq(false, Plugin.ChecksOnSave(@"C:\m\notes.txt", new[] { @"C:\m\main.k" }), "a file beside the model, not in it");
    }

    // Task 3: running the engine
    static RunOutcome RunFake(string file, IList<string> mains, int waitMs)
    {
        RunOutcome got = null;
        System.Threading.ManualResetEvent done = new System.Threading.ManualResetEvent(false);
        EngineRun.Start(Fake, file, mains, o => { got = o; done.Set(); });
        if (!done.WaitOne(waitMs)) throw new Exception("engine run did not finish");
        return got;
    }

    static void Env(string name, string value) { Environment.SetEnvironmentVariable(name, value); }

    static void ClearFakeEnv()
    {
        foreach (string n in new[] { "FAKE_JSON", "FAKE_SLEEP_MS", "FAKE_EXIT", "FAKE_STDERR", "FAKE_CHILD", "FAKE_PIDS", "FAKE_ARGS" })
            Env(n, null);
    }

    static string Temp(string name)
    {
        string d = Path.Combine(Path.GetTempPath(), "deckcheck_npp_tests");
        Directory.CreateDirectory(d);
        string p = Path.Combine(d, name);
        if (File.Exists(p)) File.Delete(p);
        return p;
    }

    public static void TestEngineRunParsesTheResult()
    {
        ClearFakeEnv();
        Env("FAKE_JSON", Path.GetFullPath(Path.Combine(Data, "seeded.json")));
        Env("FAKE_EXIT", "1");
        RunOutcome o = RunFake(Path.GetFullPath(Path.Combine(Data, "seeded.json")), new string[0], 20000);
        Eq(null, o.Error, "error");
        Eq(false, o.Cancelled, "cancelled");
        Eq("main.k", o.Result.Models[0].Main, "main");
    }

    public static void TestEngineRunResolvesPathsOnTheWorker()
    {
        ClearFakeEnv();
        Env("FAKE_JSON", Path.GetFullPath(Path.Combine(Data, "seeded.json")));
        string file = Path.GetFullPath(Path.Combine(Data, "seeded.json"));
        RunOutcome o = RunFake(file, new string[0], 20000);
        string main = Path.Combine(Path.GetDirectoryName(file), "main.k");
        Eq(main, o.Result.Diagnostics[0].Path, "finding path");
        Eq(main, o.Result.Models[0].ReachedPaths[0], "reached path");
    }

    public static void TestPaintMemoPaintsEachBufferOncePerResult()
    {
        PaintMemo m = new PaintMemo();
        Eq(true, m.Take(@"C:\m\a.k", 1), "first paint");
        Eq(false, m.Take(@"c:\M\A.K", 1), "same result, same file: keep the indicators, which move with edits");
        Eq(true, m.Take(@"C:\m\b.k", 1), "another file");
        Eq(true, m.Take(@"C:\m\a.k", 2), "a new result repaints");
    }

    public static void TestEngineRunPassesForMainDecksAndTheFilesFolder()
    {
        ClearFakeEnv();
        string args = Temp("args.txt");
        Env("FAKE_ARGS", args);
        Env("FAKE_JSON", Path.GetFullPath(Path.Combine(Data, "seeded.json")));
        string file = Path.GetFullPath(Path.Combine(Data, "seeded.json"));
        RunFake(file, new[] { "a b.k", "c.k" }, 20000);
        Eq("--for\nseeded.json\n--main-deck\na b.k\n--main-deck\nc.k\n--format\njson\n--background\ncwd=" + Path.GetDirectoryName(file),
           File.ReadAllText(args), "arguments and working directory");
    }

    public static void TestEngineFailureReportsItsMessage()
    {
        ClearFakeEnv();
        Env("FAKE_EXIT", "2");
        Env("FAKE_STDERR", "deckcheck: internal error: boom (please report this)");
        RunOutcome o = RunFake(Path.GetFullPath(Path.Combine(Data, "seeded.json")), new string[0], 20000);
        Eq(true, o.Error != null && o.Error.Contains("boom"), "error message: " + o.Error);
    }

    public static void TestMissingEngine()
    {
        RunOutcome got = null;
        EngineRun.Start(Path.Combine(Data, "no-such-engine.exe"), Path.Combine(Data, "seeded.json"), new string[0], o => got = o);
        System.Threading.Thread.Sleep(500);
        Eq(true, got != null && got.Error.Contains("engine not found"), "missing engine: " + (got == null ? "no outcome" : got.Error));
    }

    public static void TestEngineRunsBelowNormalPriority()
    {
        ClearFakeEnv();
        string pids = Temp("prio.txt");
        Env("FAKE_PIDS", pids);
        Env("FAKE_JSON", Path.GetFullPath(Path.Combine(Data, "seeded.json")));
        Env("FAKE_SLEEP_MS", "500");
        RunFake(Path.GetFullPath(Path.Combine(Data, "seeded.json")), new string[0], 20000);
        Eq("BelowNormal", File.ReadAllText(pids).Trim().Split(' ')[1], "priority");
    }

    public static void TestCancelKillsTheWholeTree()
    {
        ClearFakeEnv();
        string pids = Temp("pids.txt");
        Env("FAKE_PIDS", pids);
        Env("FAKE_CHILD", "1");
        Env("FAKE_SLEEP_MS", "30000");
        RunOutcome got = null;
        System.Threading.ManualResetEvent done = new System.Threading.ManualResetEvent(false);
        EngineRun run = EngineRun.Start(Fake, Path.GetFullPath(Path.Combine(Data, "seeded.json")), new string[0],
                                        o => { got = o; done.Set(); });
        for (int i = 0; i < 100 && (!File.Exists(pids) || File.ReadAllLines(pids).Length < 2); i++)
            System.Threading.Thread.Sleep(100);
        int[] ids = File.ReadAllLines(pids).Select(l => int.Parse(l.Split(' ')[0])).ToArray();
        Eq(2, ids.Length, "processes started");
        run.Cancel();
        if (!done.WaitOne(5000)) throw new Exception("cancelled run did not report");
        Eq(true, got.Cancelled, "cancelled");
        System.Threading.Thread.Sleep(2000);
        foreach (int id in ids)
        {
            bool alive;
            try { alive = !System.Diagnostics.Process.GetProcessById(id).HasExited; } catch (ArgumentException) { alive = false; }
            Eq(false, alive, "process " + id + " still alive");
        }
        ClearFakeEnv();
    }

    // Task 2: findings model
    public static void TestParseRealEngineOutput()
    {
        CheckResult r = Findings.Parse(File.ReadAllText(Path.Combine(Data, "seeded.json")));
        Eq(1, r.Models.Count, "models");
        Eq("main.k", r.Models[0].Main, "main");
        Eq(false, r.Models[0].Alone, "alone");
        Eq("main.k", string.Join(",", r.Models[0].Reached.ToArray()), "reached");
        Finding f = r.Diagnostics.First(d => d.Code == "DC001");
        Eq(8, f.Line, "line"); Eq(29, f.Column, "column"); Eq(31, f.EndColumn, "end"); Eq("error", f.Severity, "severity");
        Eq(r.Diagnostics.Count(d => d.Severity == "error"), r.Errors, "errors");
        Eq(true, r.NotChecked.Count > 0 && r.NotChecked[0].Reasons.Count > 0, "not checked");
    }

    public static void TestRefusesAnotherVersion()
    {
        Throws(() => Findings.Parse("{\"version\": 2, \"models\": [], \"diagnostics\": [], \"not_checked\": [], \"summary\": {\"errors\": 0, \"warnings\": 0}}"),
               "unsupported engine output version 2");
    }

    public static void TestRefusesNonJson()
    {
        Throws(() => Findings.Parse("Traceback (most recent call last)"), "not valid JSON");
    }

    public static void TestRefusesAnEngineWithoutModels()
    {
        Throws(() => Findings.Parse("{\"version\": 1, \"diagnostics\": [], \"not_checked\": [], \"summary\": {\"errors\": 0, \"warnings\": 0}}"),
               "engine too old");
    }
}
