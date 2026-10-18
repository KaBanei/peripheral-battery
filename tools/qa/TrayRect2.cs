using System;
using System.Runtime.InteropServices;
using System.Text;
class TrayRect2 {
  public delegate bool EnumProc(IntPtr h, IntPtr l);
  [DllImport("user32.dll")] public static extern bool EnumWindows(EnumProc cb, IntPtr l);
  [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
  [DllImport("user32.dll", CharSet=CharSet.Unicode)] public static extern int GetClassName(IntPtr h, StringBuilder s, int n);
  [StructLayout(LayoutKind.Sequential)] public struct RECT { public int L,T,R,B; }
  [StructLayout(LayoutKind.Sequential, CharSet=CharSet.Unicode)] public struct NID { public int cbSize; public IntPtr hWnd; public uint uID; public Guid guid; }
  [DllImport("shell32.dll", CharSet=CharSet.Unicode)] public static extern int Shell_NotifyIconGetRect(ref NID id, out RECT r);
  [DllImport("user32.dll", CharSet=CharSet.Unicode)] public static extern IntPtr FindWindow(string c, string n);
  static void Main(string[] a) {
    uint pid = uint.Parse(a[0]);
    Console.WriteLine("Shell_TrayWnd=" + FindWindow("Shell_TrayWnd", null));
    Console.WriteLine("NotifyIconOverflowWindow=" + FindWindow("NotifyIconOverflowWindow", null));
    EnumWindows((h,l) => {
      uint p; GetWindowThreadProcessId(h, out p);
      if (p != pid) return true;
      var cn = new StringBuilder(256); GetClassName(h, cn, 256);
      Console.WriteLine("win h=" + h + " class=" + cn);
      for (uint id = 0; id <= 3; id++) {
        var nid = new NID(); nid.cbSize = Marshal.SizeOf(typeof(NID)); nid.hWnd = h; nid.uID = id;
        RECT r; int hr = Shell_NotifyIconGetRect(ref nid, out r);
        Console.WriteLine("  uid=" + id + " hr=0x" + hr.ToString("X8") + " rect=" + r.L + "," + r.T + "," + r.R + "," + r.B);
      }
      return true;
    }, IntPtr.Zero);
  }
}
