using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Text;

class Cap2
{
    public delegate bool EnumProc(IntPtr h, IntPtr l);
    [DllImport("user32.dll")] static extern bool EnumWindows(EnumProc cb, IntPtr l);
    [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern int GetWindowText(IntPtr h, StringBuilder s, int n);
    [DllImport("user32.dll")] static extern bool IsWindowVisible(IntPtr h);
    [DllImport("user32.dll")] static extern bool GetWindowRect(IntPtr h, out RECT r);
    [DllImport("user32.dll")] static extern uint GetDpiForWindow(IntPtr h);
    [DllImport("user32.dll")] static extern bool PrintWindow(IntPtr h, IntPtr hdc, uint flags);
    [DllImport("user32.dll")] static extern bool SetForegroundWindow(IntPtr h);
    [DllImport("user32.dll")] static extern bool ShowWindow(IntPtr h, int c);
    [StructLayout(LayoutKind.Sequential)] public struct RECT { public int L, T, R, B; }

    static void Main(string[] args)
    {
        string name = args.Length > 0 ? args[0] : "PeripheralBattery";
        string outPath = args.Length > 1 ? args[1] : "shot.png";
        string filter = args.Length > 2 ? args[2] : null;
        var proc = System.Diagnostics.Process.GetProcessesByName(name);
        if (proc.Length == 0) { Console.WriteLine("process not found"); return; }
        var rows = new List<string>();
        IntPtr target = IntPtr.Zero; string title = ""; int best = -1;
        foreach (var p in proc)
        {
            EnumWindows((h, l) =>
            {
                uint pid; GetWindowThreadProcessId(h, out pid);
                if (pid != p.Id || !IsWindowVisible(h)) return true;
                var t = new StringBuilder(256); GetWindowText(h, t, 256);
                string tt = t.ToString();
                if (filter != null && tt.IndexOf(filter, StringComparison.OrdinalIgnoreCase) < 0) return true;
                RECT r; GetWindowRect(h, out r);
                int a = (r.R - r.L) * (r.B - r.T);
                rows.Add(h + "|" + tt + "|" + (r.R - r.L) + "x" + (r.B - r.T) + "|" + r.L + "," + r.T + "|dpi=" + GetDpiForWindow(h));
                int score = a + (tt.Length > 0 ? 100000000 : 0);
                if (score > best) { best = score; target = h; title = tt; }
                return true;
            }, IntPtr.Zero);
        }
        foreach (var row in rows) Console.WriteLine(row);
        if (target == IntPtr.Zero) { Console.WriteLine("no matching window"); return; }
        ShowWindow(target, 9); SetForegroundWindow(target);
        System.Threading.Thread.Sleep(400);
        RECT wr; GetWindowRect(target, out wr);
        int w = wr.R - wr.L, hh = wr.B - wr.T;
        using (var bmp = new Bitmap(w, hh))
        {
            using (var g = Graphics.FromImage(bmp))
            {
                IntPtr hdc = g.GetHdc();
                PrintWindow(target, hdc, 2);
                g.ReleaseHdc(hdc);
            }
            // sample: if the whole image is black, fall back to screen copy
            bool black = true;
            for (int y = 0; y < hh && black; y += Math.Max(1, hh / 20))
                for (int x = 0; x < w; x += Math.Max(1, w / 20))
                {
                    Color c = bmp.GetPixel(x, y);
                    if (c.R + c.G + c.B > 30) { black = false; break; }
                }
            if (black)
            {
                using (var g2 = Graphics.FromImage(bmp)) g2.CopyFromScreen(wr.L, wr.T, 0, 0, new Size(w, hh));
                Console.WriteLine("printwindow black, used screen copy");
            }
            bmp.Save(outPath, ImageFormat.Png);
            Console.WriteLine("saved " + outPath + " " + w + "x" + hh + " title=[" + title + "] dpi=" + GetDpiForWindow(target));
        }
    }
}
