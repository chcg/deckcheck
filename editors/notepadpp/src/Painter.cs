// Squiggles (spec §4): Scintilla indicators on each finding's field, in the active document only, capped per file
// (spec §5: tens of thousands of findings must not freeze scrolling). Also maps a finding to a buffer range for the jump.
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;

namespace Deckcheck.Npp
{
    public class PaintedRange
    {
        public long Start, End;
        public Finding Finding;
    }

    /// Which buffers were painted for which result: a buffer is painted once per result, then its indicators move with
    /// edits; repainting from the result's line numbers after an edit would put squiggles on the wrong fields.
    public sealed class PaintMemo
    {
        readonly HashSet<string> _done = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        int _gen = -1;

        public bool Take(string path, int gen)
        {
            if (gen != _gen) { _done.Clear(); _gen = gen; }
            return _done.Add(path);
        }

        public void Forget() { _done.Clear(); _gen = -1; }
    }

    public sealed class Painter
    {
        public const int Cap = 2000;
        const uint NPPM_ALLOCATEINDICATOR = Win.NPPMSG + 113;
        const uint SCI_GETLENGTH = 2006, SCI_INDICSETSTYLE = 2080, SCI_INDICSETFORE = 2082, SCI_SETINDICATORCURRENT = 2500;
        const uint SCI_INDICATORFILLRANGE = 2504, SCI_INDICATORCLEARRANGE = 2505, SCI_INDICSETALPHA = 2523;
        const int INDIC_SQUIGGLEPIXMAP = 13;
        int _error = 9, _warning = 10;
        public List<PaintedRange> Ranges = new List<PaintedRange>();

        public static IList<Finding> Select(IList<Finding> all, string file, int cap)
        {
            string want = Norm(file);
            return all.Where(f => Norm(f.Path ?? f.File) == want)
                      .OrderBy(f => f.Line).ThenBy(f => f.Column).Take(cap).ToList();
        }

        static string Norm(string p) { return p.Replace('\\', '/').ToLowerInvariant(); }

        public void Init(IntPtr npp, IntPtr[] views)
        {
            IntPtr start = Marshal.AllocHGlobal(4);
            try
            {
                if (Win.Send(npp, NPPM_ALLOCATEINDICATOR, 2, start.ToInt64()) != IntPtr.Zero)
                {
                    _error = Marshal.ReadInt32(start);
                    _warning = _error + 1;
                }
            }
            finally { Marshal.FreeHGlobal(start); }
            foreach (IntPtr sci in views)
            {
                Win.Send(sci, SCI_INDICSETSTYLE, _error, INDIC_SQUIGGLEPIXMAP);
                Win.Send(sci, SCI_INDICSETFORE, _error, 0x0014E5);   // #E51400 as Scintilla's BGR
                Win.Send(sci, SCI_INDICSETSTYLE, _warning, INDIC_SQUIGGLEPIXMAP);
                Win.Send(sci, SCI_INDICSETFORE, _warning, 0x009BD7); // #D79B00
            }
        }

        public void Clear(IntPtr sci)
        {
            long len = Win.Send(sci, SCI_GETLENGTH, 0, 0).ToInt64();
            foreach (int ind in new[] { _error, _warning })
            {
                Win.Send(sci, SCI_SETINDICATORCURRENT, ind, 0);
                Win.Send(sci, SCI_INDICATORCLEARRANGE, 0, len);
            }
            Ranges = new List<PaintedRange>();
        }

        const uint SCI_SETINDICATORVALUE = 2502, SCI_INDICATORVALUEAT = 2507;
        readonly Dictionary<string, List<Finding>> _painted = new Dictionary<string, List<Finding>>(StringComparer.OrdinalIgnoreCase);

        /// Each filled range carries the value (index + 1) of its finding, so a hover finds the finding from the
        /// indicator itself: the ranges move with edits, the positions captured here do not.
        public int Paint(IntPtr sci, string path, int encoding, IList<Finding> mine)
        {
            Clear(sci);
            List<Finding> painted = new List<Finding>();
            foreach (Finding f in mine)
            {
                PaintedRange r = RangeOf(sci, encoding, f);
                if (r == null) continue;
                painted.Add(f);
                Win.Send(sci, SCI_SETINDICATORCURRENT, f.Severity == "error" ? _error : _warning, 0);
                Win.Send(sci, SCI_SETINDICATORVALUE, painted.Count, 0);
                Win.Send(sci, SCI_INDICATORFILLRANGE, r.Start, r.End - r.Start);
                Ranges.Add(r);
            }
            _painted[path] = painted;
            return Ranges.Count;
        }

        public Finding FindingAt(IntPtr sci, string path, long pos)
        {
            List<Finding> painted;
            if (!_painted.TryGetValue(path, out painted)) return null;
            foreach (int ind in new[] { _error, _warning })
            {
                int v = Win.Send(sci, SCI_INDICATORVALUEAT, ind, pos).ToInt32();
                if (v > 0 && v <= painted.Count) return painted[v - 1];
            }
            return null;
        }

        public void Forget() { _painted.Clear(); }

        /// The buffer range of a finding's field, clamped to its line; null when it cannot be mapped.
        public static PaintedRange RangeOf(IntPtr sci, int encoding, Finding f)
        {
            int line = f.Line - 1;
            long lines = Win.Send(sci, 2154 /* SCI_GETLINECOUNT */, 0, 0).ToInt64();
            if (line < 0 || line >= lines) return null;
            byte[] text = LineBytes(sci, line);
            int cp = Win.Send(sci, Win.SCI_GETCODEPAGE, 0, 0).ToInt32();
            int s = ColumnMap.ToBuffer(f.Line, f.Column, encoding, cp, text);
            int e = ColumnMap.ToBuffer(f.Line, f.EndColumn, encoding, cp, text);
            if (s < 0 || e < 0) return null;
            if (e <= s) e = Math.Min(s + 1, text.Length);
            if (e <= s) return null;
            long start = Win.Send(sci, Win.SCI_POSITIONFROMLINE, line, 0).ToInt64();
            return new PaintedRange { Start = start + s, End = start + e, Finding = f };
        }

        /// The line's bytes without its line ending.
        public static byte[] LineBytes(IntPtr sci, int line)
        {
            int n = Win.Send(sci, Win.SCI_LINELENGTH, line, 0).ToInt32();
            IntPtr buf = Marshal.AllocHGlobal(n + 1);
            try
            {
                Win.Send(sci, Win.SCI_GETLINE, line, buf.ToInt64());
                byte[] b = new byte[n];
                Marshal.Copy(buf, b, 0, n);
                int k = n;
                while (k > 0 && (b[k - 1] == '\n' || b[k - 1] == '\r')) k--;
                Array.Resize(ref b, k);
                return b;
            }
            finally { Marshal.FreeHGlobal(buf); }
        }

        public static string Text(IntPtr sci, PaintedRange r)
        {
            int line = Win.Send(sci, 2166 /* SCI_LINEFROMPOSITION */, r.Start, 0).ToInt32();
            long ls = Win.Send(sci, Win.SCI_POSITIONFROMLINE, line, 0).ToInt64();
            byte[] b = LineBytes(sci, line);
            int s = (int)(r.Start - ls), e = (int)Math.Min(r.End - ls, b.Length);
            if (s < 0 || e <= s) return "";
            int cp = Win.Send(sci, Win.SCI_GETCODEPAGE, 0, 0).ToInt32();
            Encoding enc = cp == ColumnMap.CodepageUtf8 ? Encoding.UTF8 : Encoding.Default;
            return enc.GetString(b, s, e - s);
        }
    }
}
