// The engine reports Latin-1 byte columns of the file on disk; Scintilla positions are bytes of the buffer. They agree
// except (measured on Notepad++ 8.9.3, plan N3): line 1 of a UTF-8 file with a BOM (Notepad++ drops the 3 BOM bytes),
// an ANSI file held as UTF-8 (each disk byte is one character), and UTF-16 files (no mapping: not painted).
using System;
using System.Text;

namespace Deckcheck.Npp
{
    public static class ColumnMap
    {
        public const int Ansi = 0, Utf8Bom = 1, Utf16BE = 2, Utf16LE = 3, Utf8 = 4, SevenBit = 5, Utf16BENoBom = 6, Utf16LENoBom = 7;
        public const int CodepageUtf8 = 65001;

        /// line and column are the engine's (1-based); the result is a 0-based byte offset into bufferLine, or -1.
        public static int ToBuffer(int line, int column, int encoding, int codepage, byte[] bufferLine)
        {
            if (encoding == Utf16BE || encoding == Utf16LE || encoding == Utf16BENoBom || encoding == Utf16LENoBom) return -1;
            int disk = Math.Max(column - 1, 0);
            if (line == 1 && encoding == Utf8Bom) disk = Math.Max(disk - 3, 0);
            int offset = disk;
            if ((encoding == Ansi || encoding == SevenBit) && codepage == CodepageUtf8)
            {
                string text = Encoding.UTF8.GetString(bufferLine);
                offset = Encoding.UTF8.GetByteCount(text.Substring(0, Math.Min(disk, text.Length)));
            }
            return Math.Min(offset, bufferLine.Length);
        }
    }
}
