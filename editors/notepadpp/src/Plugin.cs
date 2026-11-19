// What the plugin does: wiring between the menu, notifications, the engine runner and the panel.
using System;
using System.Reflection;

namespace Deckcheck.Npp
{
    public static class Plugin
    {
        public static string Version
        {
            get { return Assembly.GetExecutingAssembly().GetName().Version.ToString(3); }
        }

        public static void OnNotify(NmHdr h) { }
        public static void CheckCurrent() { }
        public static void ShowPanel() { }
        public static void EditMainDecks() { }
        public static void ToggleDisabled() { }
        public static void Fail(Exception e) { }
    }
}
