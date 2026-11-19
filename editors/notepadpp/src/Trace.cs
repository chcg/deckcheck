// Event trace for end-to-end tests: JSON lines appended to the file named by DECKCHECK_NPP_TRACE. Off when unset.
using System;
using System.Collections.Generic;
using System.IO;
using System.Web.Script.Serialization;

namespace Deckcheck.Npp
{
    public static class Trace
    {
        static readonly string Path_ = Environment.GetEnvironmentVariable("DECKCHECK_NPP_TRACE");
        static readonly object Lock = new object();

        public static void Write(string evt, params object[] pairs)
        {
            if (string.IsNullOrEmpty(Path_)) return;
            try
            {
                Dictionary<string, object> d = new Dictionary<string, object>();
                d["event"] = evt;
                for (int i = 0; i + 1 < pairs.Length; i += 2) d[(string)pairs[i]] = pairs[i + 1];
                string line = new JavaScriptSerializer().Serialize(d) + Environment.NewLine;
                lock (Lock) File.AppendAllText(Path_, line);
            }
            catch (Exception) { }
        }
    }
}
