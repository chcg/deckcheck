// A stand-in for deckcheck.exe in unit tests. Environment:
//   FAKE_JSON      file whose text is printed to stdout
//   FAKE_SLEEP_MS  sleep before printing
//   FAKE_EXIT      exit code (default 0); FAKE_STDERR text written to stderr
//   FAKE_CHILD=1   start a sleeping copy of itself first (FAKE_CHILD=0 for it); its pid goes to FAKE_PIDS
//   FAKE_PIDS      file that gets this process's pid and priority, one line per process
//   FAKE_ARGS      file that gets the command line arguments and working directory
using System;
using System.Diagnostics;
using System.IO;
using System.Threading;

public static class FakeEngine
{
    static string Env(string name) { return Environment.GetEnvironmentVariable(name); }

    public static int Main(string[] args)
    {
        Process me = Process.GetCurrentProcess();
        if (Env("FAKE_PIDS") != null)
            File.AppendAllText(Env("FAKE_PIDS"), me.Id + " " + me.PriorityClass + Environment.NewLine);
        if (Env("FAKE_ARGS") != null)
            File.WriteAllText(Env("FAKE_ARGS"), string.Join("\n", args) + "\ncwd=" + Environment.CurrentDirectory);
        if (Env("FAKE_CHILD") == "1")
        {
            ProcessStartInfo psi = new ProcessStartInfo(me.MainModule.FileName);
            psi.UseShellExecute = false;
            psi.EnvironmentVariables["FAKE_CHILD"] = "0";
            psi.EnvironmentVariables["FAKE_SLEEP_MS"] = "60000";
            psi.EnvironmentVariables.Remove("FAKE_ARGS");
            Process.Start(psi);
        }
        int sleep;
        if (int.TryParse(Env("FAKE_SLEEP_MS"), out sleep)) Thread.Sleep(sleep);
        if (Env("FAKE_STDERR") != null) Console.Error.Write(Env("FAKE_STDERR"));
        if (Env("FAKE_JSON") != null) Console.Out.Write(File.ReadAllText(Env("FAKE_JSON")));
        int code;
        return int.TryParse(Env("FAKE_EXIT"), out code) ? code : 0;
    }
}
