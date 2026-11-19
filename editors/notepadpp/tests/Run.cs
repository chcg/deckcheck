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
