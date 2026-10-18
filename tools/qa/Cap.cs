using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Text;

class Cap
{
    public delegate bool EnumProc(IntPtr h, IntPtr l);
    [DllImport("user32.dll")] static extern bool EnumWindows(EnumProc cb, IntPtr l);
    [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern int GetWindowText(IntPtr h, StringBuilder s, int n);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern int GetClassName(IntPtr h, StringBuilder s, int n);
    [DllImport("user32.dll")] static extern bool IsWindowVisible(IntPtr h);
    [DllImport("user32.dll")] static extern bool GetWindowRect(IntPtr h, out RECT r);
    [DllImport("user32.dll")] static extern bool GetClientRect(IntPtr h, out RECT r);
    [DllImport("user32.dll")] static extern uint GetDpiForWindow(IntPtr h);
    [DllImport("user32.dll")] static extern bool PrintWindow(IntPtr h, IntPtr hdc, uint flags);
    [DllImport("user32.dll")] static extern bool SetForegroundWindow(IntPtr h);
    [DllImport("user32.dll")] static extern bool ShowWindow(IntPtr h, int c);
    [DllImport("shcore.dll")] static extern int GetProcessDpiAwareness(IntPtr proc, out int aw);
    [DllImport("kernel32.dll")] static extern IntPtr OpenProcess(uint access, bool inherit, uint pid);
    [DllImport("kernel32.dll")] static extern bool CloseHandle(IntPtr h);
    [StructLayout(LayoutKind.Sequential)] public struct RECT { public int L, T, R, B; }

    static void Main(string[] args)
    {
        string name = args.Length > 0 ? args[0] : "PeripheralBattery";
        string outPath = args.Length > 1 ? args[1] : "shot.png";
        System.Diagnostics.Process proc = null;
        foreach (var p in System.Diagnostics.Process.GetProcessesByName(name)) { proc = p; break; }
        if (proc == null) { Console.WriteLine("process not found"); return; }
        IntPtr target = IntPtr.Zero; string title = ""; int area = 0;
        EnumWindows((h, l) =>
        {
            uint pid; GetWindowThreadProcessId(h, out pid);
            if (pid != proc.Id || !IsWindowVisible(h)) return true;
            var t = new StringBuilder(256); GetWindowText(h, t, 256);
            RECT r; GetWindowRect(h, out r);
            int a = (r.R - r.L) * (r.B - r.T);
            if (a > area) { area = a; target = h; title = t.ToString(); }
            return true;
        }, IntPtr.Zero);
        if (target == IntPtr.Zero) { Console.WriteLine("no window"); return; }
        RECT wr; GetWindowRect(target, out wr);
        RECT cr; GetClientRect(target, out cr);
        int aw = -1; IntPtr hp = OpenProcess(0x1000, false, (uint)proc.Id);
        int hr = hp != IntPtr.Zero ? GetProcessDpiAwareness(hp, out aw) : -1;
        if (hp != IntPtr.Zero) CloseHandle(hp);
        Console.WriteLine("pid=" + proc.Id + " hwnd=" + target + " title=[" + title + "]");
        Console.WriteLine("window=" + (wr.R - wr.L) + "x" + (wr.B - wr.T) + " at " + wr.L + "," + wr.T + " client=" + (cr.R - cr.L) + "x" + (cr.B - cr.T));
        Console.WriteLine("dpi=" + GetDpiForWindow(target) + " processAwareness=" + (hr == 0 ? aw.ToString() : "err"));
        ShowWindow(target, 9); SetForegroundWindow(target);
        System.Threading.Thread.Sleep(300);
        int w = wr.R - wr.L, hh = wr.B - wr.T;
        using (var bmp = new Bitmap(w, hh))
        using (var g = Graphics.FromImage(bmp))
        {
            IntPtr hdc = g.GetHdc();
            bool ok = PrintWindow(target, hdc, 2);
            g.ReleaseHdc(hdc);
            bmp.Save(outPath, ImageFormat.Png);
            Console.WriteLine("saved " + outPath + " " + w + "x" + hh + " printwindow=" + ok);
        }
    }
}