// Notepad++ and Scintilla messages, structs and the menu item array (C# 5: built by the .NET Framework's csc).
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;

namespace Deckcheck.Npp
{
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    public delegate void MenuCommand();

    [StructLayout(LayoutKind.Sequential)]
    public struct ShortcutKey
    {
        [MarshalAs(UnmanagedType.I1)] public bool Ctrl;
        [MarshalAs(UnmanagedType.I1)] public bool Alt;
        [MarshalAs(UnmanagedType.I1)] public bool Shift;
        public byte Key;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    public struct FuncItem
    {
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)] public string Name;
        public IntPtr Fn;
        public int CmdId;
        [MarshalAs(UnmanagedType.I1)] public bool Init2Check;
        public IntPtr Shortcut;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct NmHdr
    {
        public IntPtr HwndFrom;
        public UIntPtr IdFrom;
        public uint Code;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct Rect { public int Left, Top, Right, Bottom; }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    public struct TbData
    {
        public IntPtr Client;
        public string Name;
        public int DlgId;
        public uint Mask;
        public IntPtr IconTab;
        public string AddInfo;
        public Rect Float;
        public int PrevCont;
        public string ModuleName;
    }

    /// The plugin's menu: a pinned unmanaged FuncItem array that Notepad++ reads, and later fills in with command ids.
    public sealed class MenuItems
    {
        readonly List<MenuCommand> _keep = new List<MenuCommand>();
        readonly List<IntPtr> _shortcuts = new List<IntPtr>();
        readonly List<FuncItem> _items = new List<FuncItem>();
        IntPtr _array = IntPtr.Zero;
        static readonly int Size = Marshal.SizeOf(typeof(FuncItem));

        public int Add(string name, MenuCommand fn, ShortcutKey? key)
        {
            FuncItem item = new FuncItem();
            item.Name = name;
            if (fn != null)
            {
                _keep.Add(fn);
                item.Fn = Marshal.GetFunctionPointerForDelegate(fn);
            }
            if (key.HasValue)
            {
                IntPtr p = Marshal.AllocHGlobal(Marshal.SizeOf(typeof(ShortcutKey)));
                Marshal.StructureToPtr(key.Value, p, false);
                _shortcuts.Add(p);
                item.Shortcut = p;
            }
            _items.Add(item);
            return _items.Count - 1;
        }

        public IntPtr Array(out int count)
        {
            count = _items.Count;
            if (_array == IntPtr.Zero)
            {
                _array = Marshal.AllocHGlobal(Size * _items.Count);
                for (int i = 0; i < _items.Count; i++)
                    Marshal.StructureToPtr(_items[i], new IntPtr(_array.ToInt64() + i * Size), false);
            }
            return _array;
        }

        /// The command id Notepad++ assigned to item i (written into the array after getFuncsArray).
        public int CommandId(int i)
        {
            FuncItem it = (FuncItem)Marshal.PtrToStructure(new IntPtr(_array.ToInt64() + i * Size), typeof(FuncItem));
            return it.CmdId;
        }
    }

    public static class Win
    {
        public const uint WM_USER = 0x0400;
        public const uint NPPMSG = WM_USER + 1000;
        public const uint NPPM_GETCURRENTSCINTILLA = NPPMSG + 4;
        public const uint NPPM_DMMSHOW = NPPMSG + 30;
        public const uint NPPM_DMMHIDE = NPPMSG + 31;
        public const uint NPPM_DMMREGASDCKDLG = NPPMSG + 33;
        public const uint NPPM_SETMENUITEMCHECK = NPPMSG + 40;
        public const uint NPPM_GETPLUGINSCONFIGDIR = NPPMSG + 46;
        public const uint NPPM_GETFULLPATHFROMBUFFERID = NPPMSG + 58;
        public const uint NPPM_GETCURRENTBUFFERID = NPPMSG + 60;
        public const uint NPPM_DOOPEN = NPPMSG + 77;
        public const uint RUNCOMMAND_USER = WM_USER + 3000;
        public const uint NPPM_GETFULLCURRENTPATH = RUNCOMMAND_USER + 1;

        public const uint NPPN_FIRST = 1000;
        public const uint NPPN_READY = NPPN_FIRST + 1;
        public const uint NPPN_FILESAVED = NPPN_FIRST + 8;
        public const uint NPPN_SHUTDOWN = NPPN_FIRST + 9;
        public const uint NPPN_BUFFERACTIVATED = NPPN_FIRST + 10;
        public const uint SCN_SAVEPOINTREACHED = 2002;
        public const uint SCN_SAVEPOINTLEFT = 2003;

        public const uint SCI_GOTOLINE = 2024;
        public const uint SCI_SETSEL = 2160;
        public const uint SCI_POSITIONFROMLINE = 2167;
        public const uint SCI_SCROLLCARET = 2169;
        public const uint SCI_GRABFOCUS = 2400;
        public const uint SCI_GETCODEPAGE = 2137;
        public const uint SCI_GETLINE = 2153;
        public const uint SCI_LINELENGTH = 2350;

        public const uint DWS_DF_CONT_BOTTOM = 3u << 28;

        [DllImport("user32.dll")]
        public static extern IntPtr SendMessage(IntPtr h, uint msg, IntPtr w, IntPtr l);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        public static extern IntPtr SendMessage(IntPtr h, uint msg, IntPtr w, StringBuilder l);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        public static extern IntPtr SendMessage(IntPtr h, uint msg, IntPtr w, string l);

        public static IntPtr Send(IntPtr h, uint msg, long w, long l)
        {
            return SendMessage(h, msg, new IntPtr(w), new IntPtr(l));
        }
    }
}
