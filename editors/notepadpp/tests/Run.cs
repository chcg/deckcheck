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

    public static void TestEngineRunPassesForMainDecksAndTheFilesFolder()
    {
        ClearFakeEnv();
        string args = Temp("args.txt");
        Env("FAKE_ARGS", args);
        Env("FAKE_JSON", Path.GetFullPath(Path.Combine(Data, "seeded.json")));
        string file = Path.GetFullPath(Path.Combine(Data, "seeded.json"));
        RunFake(file, new[] { "a b.k", "c.k" }, 20000);
        Eq("--for\nseeded.json\n--main-deck\na b.k\n--main-deck\nc.k\n--format\njson\ncwd=" + Path.GetDirectoryName(file),
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
