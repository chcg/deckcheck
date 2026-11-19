// Runs the engine for one check (spec §5, do no harm): in the background, at below-normal priority, inside a Job object
// so that cancelling, a new check, or Notepad++ exiting (even crashing) kills the whole process tree.
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;

namespace Deckcheck.Npp
{
    public class RunOutcome
    {
        public CheckResult Result;
        public string Error;
        public bool Cancelled;
        public double Seconds;
        public string Folder;  // the engine's working directory: the JSON's relative paths resolve against it
    }

    public sealed class EngineRun
    {
        readonly object _lock = new object();
        IntPtr _job = IntPtr.Zero;
        bool _cancelled;

        public static EngineRun Start(string exe, string file, IList<string> mainDecks, Action<RunOutcome> done)
        {
            EngineRun run = new EngineRun();
            Thread t = new Thread(() => run.Body(exe, file, mainDecks, done));
            t.IsBackground = true;
            t.Name = "deckcheck engine";
            t.Start();
            return run;
        }

        public void Cancel()
        {
            lock (_lock)
            {
                _cancelled = true;
                CloseJob();
            }
        }

        void CloseJob()
        {
            if (_job != IntPtr.Zero)
            {
                CloseHandle(_job);  // JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE: every process in the job ends
                _job = IntPtr.Zero;
            }
        }

        static string Quote(string a)
        {
            return a.IndexOfAny(new[] { ' ', '\t', '"' }) < 0 && a.Length > 0 ? a : "\"" + a.Replace("\"", "\\\"") + "\"";
        }

        void Body(string exe, string file, IList<string> mainDecks, Action<RunOutcome> done)
        {
            RunOutcome o = new RunOutcome();
            o.Folder = Path.GetDirectoryName(file);
            Stopwatch clock = Stopwatch.StartNew();
            try
            {
                if (!File.Exists(exe))
                {
                    o.Error = "engine not found: " + exe;
                    return;
                }
                List<string> args = new List<string> { "--for", Path.GetFileName(file) };
                foreach (string m in mainDecks) { args.Add("--main-deck"); args.Add(m); }
                args.Add("--format"); args.Add("json");
                StringBuilder line = new StringBuilder();
                foreach (string a in args) { if (line.Length > 0) line.Append(' '); line.Append(Quote(a)); }

                ProcessStartInfo psi = new ProcessStartInfo(exe, line.ToString());
                psi.UseShellExecute = false;
                psi.CreateNoWindow = true;
                psi.RedirectStandardOutput = true;
                psi.RedirectStandardError = true;
                psi.StandardOutputEncoding = Encoding.UTF8;
                psi.StandardErrorEncoding = Encoding.UTF8;
                psi.WorkingDirectory = o.Folder;
                psi.EnvironmentVariables["PYTHONIOENCODING"] = "utf-8";
                Process p;
                lock (_lock)
                {
                    if (_cancelled) { o.Cancelled = true; return; }
                    p = Process.Start(psi);
                    _job = NewKillOnCloseJob();
                    AssignProcessToJobObject(_job, p.Handle);
                    try { p.PriorityClass = ProcessPriorityClass.BelowNormal; } catch (Win32Exception) { }
                }
                string err = null;
                Thread errReader = new Thread(() => { err = p.StandardError.ReadToEnd(); });
                errReader.IsBackground = true;
                errReader.Start();
                string output = p.StandardOutput.ReadToEnd();
                p.WaitForExit();
                errReader.Join();
                lock (_lock)
                {
                    if (_cancelled) { o.Cancelled = true; return; }
                    CloseJob();
                }
                if (p.ExitCode == 0 || p.ExitCode == 1)
                    o.Result = Findings.Parse(output);
                else
                {
                    string msg = (err ?? "").Trim();
                    o.Error = msg.Length > 0 ? msg : "engine exited with code " + p.ExitCode;
                }
            }
            catch (EngineError e) { o.Error = e.Message; }
            catch (Exception e) { o.Error = "could not run the engine: " + e.Message; }
            finally
            {
                o.Seconds = clock.Elapsed.TotalSeconds;
                done(o);
            }
        }

        // Job object plumbing
        [StructLayout(LayoutKind.Sequential)]
        struct BasicLimits
        {
            public long PerProcessUserTimeLimit, PerJobUserTimeLimit;
            public uint LimitFlags;
            public UIntPtr MinimumWorkingSetSize, MaximumWorkingSetSize;
            public uint ActiveProcessLimit;
            public UIntPtr Affinity;
            public uint PriorityClass, SchedulingClass;
        }

        [StructLayout(LayoutKind.Sequential)]
        struct IoCounters { public ulong R, W, O, RB, WB, OB; }

        [StructLayout(LayoutKind.Sequential)]
        struct ExtendedLimits
        {
            public BasicLimits Basic;
            public IoCounters Io;
            public UIntPtr ProcessMemoryLimit, JobMemoryLimit, PeakProcessMemoryUsed, PeakJobMemoryUsed;
        }

        const uint KillOnJobClose = 0x2000, LimitPriorityClass = 0x20, BelowNormalPriorityClass = 0x4000;

        static IntPtr NewKillOnCloseJob()
        {
            IntPtr job = CreateJobObject(IntPtr.Zero, null);
            if (job == IntPtr.Zero) throw new Win32Exception();
            ExtendedLimits info = new ExtendedLimits();
            info.Basic.LimitFlags = KillOnJobClose | LimitPriorityClass;  // children inherit below-normal priority too
            info.Basic.PriorityClass = BelowNormalPriorityClass;
            int size = Marshal.SizeOf(typeof(ExtendedLimits));
            IntPtr p = Marshal.AllocHGlobal(size);
            try
            {
                Marshal.StructureToPtr(info, p, false);
                if (!SetInformationJobObject(job, 9, p, (uint)size)) throw new Win32Exception();
            }
            finally { Marshal.FreeHGlobal(p); }
            return job;
        }

        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        static extern IntPtr CreateJobObject(IntPtr attributes, string name);

        [DllImport("kernel32.dll", SetLastError = true)]
        static extern bool SetInformationJobObject(IntPtr job, int infoClass, IntPtr info, uint length);

        [DllImport("kernel32.dll", SetLastError = true)]
        static extern bool AssignProcessToJobObject(IntPtr job, IntPtr process);

        [DllImport("kernel32.dll", SetLastError = true)]
        static extern bool CloseHandle(IntPtr handle);
    }
}
