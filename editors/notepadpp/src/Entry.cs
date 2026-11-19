// The C# side of the six native exports in Exports.il. Every entry point catches everything: a plugin failure must
// never take Notepad++ down (spec §5, do no harm).
using System;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace Deckcheck.Npp
{
    public static class Entry
    {
        public const string Name = "deckcheck";
        public const int ItemCheck = 0, ItemShow = 1, ItemMainDecks = 2, ItemDisable = 4, ItemAbout = 5;

        static IntPtr _npp, _sci1, _sci2, _name;
        static MenuItems _menu;

        public static IntPtr Npp { get { return _npp; } }

        public static int isUnicode() { return 1; }

        public static void setInfo(IntPtr nppData)
        {
            try
            {
                _npp = Marshal.ReadIntPtr(nppData, 0);
                _sci1 = Marshal.ReadIntPtr(nppData, IntPtr.Size);
                _sci2 = Marshal.ReadIntPtr(nppData, 2 * IntPtr.Size);
            }
            catch (Exception) { }
        }

        public static IntPtr getName()
        {
            if (_name == IntPtr.Zero) _name = Marshal.StringToHGlobalUni(Name);
            return _name;
        }

        public static IntPtr getFuncsArray(IntPtr count)
        {
            if (_menu == null)
            {
                _menu = new MenuItems();
                ShortcutKey k = new ShortcutKey();
                k.Alt = true; k.Shift = true; k.Key = (byte)'K';
                _menu.Add("Check model", Safe(CheckModel), k);
                _menu.Add("Show findings", Safe(ShowFindings), null);
                _menu.Add("Main decks...", Safe(MainDecks), null);
                _menu.Add("-", null, null);
                _menu.Add("Disable", Safe(ToggleDisable), null);
                _menu.Add("About", Safe(About), null);
            }
            int n;
            IntPtr arr = _menu.Array(out n);
            Marshal.WriteInt32(count, n);
            return arr;
        }

        public static IntPtr messageProc(uint msg, UIntPtr w, IntPtr l) { return new IntPtr(1); }

        public static void beNotified(IntPtr notification)
        {
            try
            {
                NmHdr h = (NmHdr)Marshal.PtrToStructure(notification, typeof(NmHdr));
                Plugin.OnNotify(h, notification);
            }
            catch (Exception e) { Plugin.Fail(e); }
        }

        public static IntPtr CurrentScintilla()
        {
            IntPtr which = Marshal.AllocHGlobal(4);
            try
            {
                Win.Send(_npp, Win.NPPM_GETCURRENTSCINTILLA, 0, which.ToInt64());
                return Marshal.ReadInt32(which) == 0 ? _sci1 : _sci2;
            }
            finally { Marshal.FreeHGlobal(which); }
        }

        public static IntPtr[] Views { get { return new[] { _sci1, _sci2 }; } }

        public static void SetChecked(int item, bool on)
        {
            Win.Send(_npp, Win.NPPM_SETMENUITEMCHECK, _menu.CommandId(item), on ? 1 : 0);
        }

        static MenuCommand Safe(Action a)
        {
            return delegate
            {
                try { a(); }
                catch (Exception e) { Plugin.Fail(e); }
            };
        }

        static void CheckModel() { Plugin.CheckCurrent(); }
        static void ShowFindings() { Plugin.ShowPanel(); }
        static void MainDecks() { Plugin.EditMainDecks(); }
        static void ToggleDisable() { Plugin.ToggleDisabled(); }

        static void About()
        {
            MessageBox.Show("deckcheck " + Plugin.Version + "\r\nChecks LS-DYNA keyword decks for structural mistakes.\r\n"
                            + "https://github.com/chintan-dshel/deckcheck", "About deckcheck");
        }
    }
}
