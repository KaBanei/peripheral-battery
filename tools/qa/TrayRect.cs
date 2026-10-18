using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;
class TrayRect {
  public delegate bool EnumProc(IntPtr h, IntPtr l);
  [DllImport("user32.dll")] public static extern bool EnumWindows(EnumProc cb, IntPtr l);
  [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
  [DllImport("user32.dll", CharSet=CharSet.Unicode)] public static extern int GetClassName(IntPtr h, StringBuilder s, int n);
  [DllImport("user32.dll", CharSet=CharSet.Unicode)] public static extern int GetWindowText(IntPtr h, StringBuilder s, int n);
  [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr h);
  [StructLayout(LayoutKind.Sequential)] public struct RECT { public int L,T,R,B; }
  [StructLayout(LayoutKind.Sequential, CharSet=CharSet.Unicode)] public struct NID { public int cbSize; public IntPtr hWnd; public uint uID; public Guid guid; }
  [DllImport("shell32.dll", CharSet=CharSet.Unicode)] public static extern int Shell_NotifyIconGetRect(ref NID id, out RECT r);
  static void Main(string[] a) {
    uint pid = uint.Parse(a[0]);
    EnumWindows((h,l) => {
      uint p; GetWindowThreadProcessId(h, out p);
      if (p != pid) return true;
      var cn = new StringBuilder(256); GetClassName(h, cn, 256);
      var tt = new StringBuilder(256); GetWindowText(h, tt, 256);
      Console.WriteLine("win h=" + h + " class=" + cn + " title=[" + tt + "] vis=" + IsWindowVisible(h));
      for (uint id = 1; id <= 4; id++) {
        var nid = new NID(); nid.cbSize = Marshal.SizeOf(typeof(NID)); nid.hWnd = h; nid.uID = id;
        RECT r;
        int hr = Shell_NotifyIconGetRect(ref nid, out r);
        if (hr == 0) Console.WriteLine("  NID uid=" + id + " rect=" + r.L + "," + r.T + "," + r.R + "," + r.B);
      }
      return true;
    }, IntPtr.Zero);
  }
}
