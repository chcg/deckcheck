// The engine's JSON output (deckcheck --format json, "version": 1, with N1's "models") as plain objects.
using System;
using System.Collections;
using System.Collections.Generic;
using System.Web.Script.Serialization;

namespace Deckcheck.Npp
{
    public class EngineError : Exception
    {
        public EngineError(string message) : base(message) { }
    }

    public class Model
    {
        public string Main;
        public int Files;
        public bool Alone;
    }

    public class Finding
    {
        public string File, Severity, Code, Message;
        public string Path;  // absolute, set by the plugin from the engine's working directory
        public int Line, Column, EndColumn;
    }

    public class NotChecked
    {
        public string Main, What;
        public List<string> Reasons = new List<string>();
        public int Count;
    }

    public class CheckResult
    {
        public List<Model> Models = new List<Model>();
        public List<Finding> Diagnostics = new List<Finding>();
        public List<NotChecked> NotChecked = new List<NotChecked>();
        public int Errors, Warnings;
    }

    public static class Findings
    {
        public const int SupportedVersion = 1;

        public static CheckResult Parse(string json)
        {
            Dictionary<string, object> root;
            try
            {
                JavaScriptSerializer js = new JavaScriptSerializer();
                js.MaxJsonLength = int.MaxValue;
                root = js.DeserializeObject(json) as Dictionary<string, object>;
            }
            catch (Exception) { root = null; }
            if (root == null) throw new EngineError("engine output is not valid JSON");
            int version = Int(root, "version");
            if (version != SupportedVersion)
                throw new EngineError("unsupported engine output version " + version + " (this plugin reads version "
                                      + SupportedVersion + ")");
            if (!root.ContainsKey("models")) throw new EngineError("engine too old: no models in its output");
            CheckResult r = new CheckResult();
            foreach (Dictionary<string, object> m in List(root, "models"))
                r.Models.Add(new Model { Main = Str(m, "main"), Files = Int(m, "files"), Alone = (bool)m["alone"] });
            foreach (Dictionary<string, object> d in List(root, "diagnostics"))
                r.Diagnostics.Add(new Finding
                {
                    File = Str(d, "file"), Line = Int(d, "line"), Column = Int(d, "column"),
                    EndColumn = Int(d, "end_column"), Severity = Str(d, "severity"), Code = Str(d, "code"),
                    Message = Str(d, "message")
                });
            foreach (Dictionary<string, object> n in List(root, "not_checked"))
            {
                NotChecked nc = new NotChecked { Main = Str(n, "main"), What = Str(n, "what"), Count = Int(n, "count") };
                foreach (object reason in (IEnumerable)n["reasons"]) nc.Reasons.Add((string)reason);
                r.NotChecked.Add(nc);
            }
            Dictionary<string, object> summary = (Dictionary<string, object>)root["summary"];
            r.Errors = Int(summary, "errors");
            r.Warnings = Int(summary, "warnings");
            return r;
        }

        static IEnumerable<Dictionary<string, object>> List(Dictionary<string, object> d, string key)
        {
            foreach (object o in (IEnumerable)d[key]) yield return (Dictionary<string, object>)o;
        }

        static string Str(Dictionary<string, object> d, string key) { return (string)d[key]; }
        static int Int(Dictionary<string, object> d, string key) { return Convert.ToInt32(d[key]); }
    }
}
