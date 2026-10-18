using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Forms;
using Pb.Core;

namespace Pb.App
{
    internal static class AppInfo
    {
        public const string Name = "2.4G 外设电量";
        public static readonly string Version = DetectVersion();

        private static string DetectVersion()
        {
            try
            {
                Assembly asm = Assembly.GetEntryAssembly();
                if (asm != null)
                {
                    Version v = asm.GetName().Version;
                    if (v != null) return v.Major + "." + v.Minor + "." + v.Build;
                }
            }
            catch { }
            return "1.1.1";
        }
    }

    /// <summary>UI 线程调度 + 淡入。后台线程只通过这里回主线程。</summary>
    internal static class Ui
    {
        private static Control _marshal;

        public static void Initialize()
        {
            if (_marshal != null) return;
            _marshal = new Control();
            _marshal.CreateControl();
            IntPtr h = _marshal.Handle;
        }

        public static void Post(Action action)
        {
            if (action == null) return;
            Control m = _marshal;
            if (m == null) { SafeRun(action); return; }
            try
            {
                if (!m.InvokeRequired) { SafeRun(action); return; }
                m.BeginInvoke((MethodInvoker)delegate { SafeRun(action); });
            }
            catch (Exception ex) { Log.Warn("UI 调度失败: " + ex.Message); }
        }

        public static bool SafeRun(Action action)
        {
            try { action(); return true; }
            catch (Exception ex) { Log.Error("UI 回调异常", ex); return false; }
        }

        /// <summary>150ms 淡入（失败时直接显示）。</summary>
        public static void FadeIn(Form form, int ms)
        {
            if (form == null) return;
            try
            {
                form.Opacity = 0.0;
                Timer t = new Timer();
                t.Interval = 15;
                int elapsed = 0;
                EventHandler tick = null;
                tick = delegate
                {
                    elapsed += t.Interval;
                    double p = ms <= 0 ? 1.0 : Math.Min(1.0, (double)elapsed / ms);
                    try { form.Opacity = 0.12 + 0.88 * p; } catch { }
                    if (p >= 1.0)
                    {
                        t.Stop();
                        t.Tick -= tick;
                        t.Dispose();
                        try { form.Opacity = 1.0; } catch { }
                    }
                };
                t.Tick += tick;
                t.Start();
            }
            catch
            {
                try { form.Opacity = 1.0; } catch { }
            }
        }
    }

    /// <summary>Win32 小工具。</summary>
    internal static class Native
    {
        public const int WM_NCLBUTTONDOWN = 0xA1;
        public const int HTCAPTION = 0x2;
        public const int SM_CXSMICON = 49;
        public const int CS_DROPSHADOW = 0x00020000;

        [DllImport("user32.dll", CharSet = CharSet.Auto)]
        public static extern bool DestroyIcon(IntPtr hIcon);

        [DllImport("user32.dll")]
        public static extern int GetSystemMetrics(int nIndex);

        [DllImport("user32.dll")]
        public static extern bool ReleaseCapture();

        [DllImport("user32.dll", CharSet = CharSet.Auto)]
        public static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        public static extern IntPtr FindWindow(string lpClassName, string lpWindowName);

        [DllImport("user32.dll")]
        public static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

        [DllImport("user32.dll")]
        public static extern bool SetForegroundWindow(IntPtr hWnd);

        [DllImport("user32.dll")]
        public static extern uint GetDpiForWindow(IntPtr hWnd);

        [DllImport("user32.dll")]
        public static extern uint GetDpiForSystem();

        [DllImport("user32.dll")]
        public static extern IntPtr MonitorFromWindow(IntPtr hWnd, uint flags);

        [DllImport("shcore.dll")]
        public static extern int GetDpiForMonitor(IntPtr hmonitor, int dpiType, out uint dpiX, out uint dpiY);

        public const uint MONITOR_DEFAULTTOPRIMARY = 1;
        public const int MDT_EFFECTIVE_DPI = 0;

        /// <summary>真实监视器 DPI（不受进程 DPI 感知状态影响）。</summary>
        public static int MonitorDpi(IntPtr hwnd)
        {
            int dpi = 0;
            try
            {
                IntPtr mon = MonitorFromWindow(hwnd, MONITOR_DEFAULTTOPRIMARY);
                if (mon == IntPtr.Zero) return 0;
                uint dx;
                uint dy;
                if (GetDpiForMonitor(mon, MDT_EFFECTIVE_DPI, out dx, out dy) == 0 && dx > 0) dpi = (int)dx;
            }
            catch { }
            return dpi;
        }

        public static int WindowDpi(IntPtr hwnd)
        {
            int dpi = 0;
            try
            {
                if (hwnd != IntPtr.Zero)
                {
                    uint d = GetDpiForWindow(hwnd);
                    if (d >= 72 && d <= 480) dpi = (int)d;
                }
            }
            catch { }
            return dpi;
        }

        public static int SystemDpi()
        {
            int dpi = 0;
            try
            {
                uint d = GetDpiForSystem();
                if (d >= 72 && d <= 480) dpi = (int)d;
            }
            catch { }
            int mon = MonitorDpi(IntPtr.Zero);
            if (mon > dpi) dpi = mon;
            if (dpi <= 0) dpi = 96;
            return dpi;
        }
    }

    /// <summary>GDI+ 绘制辅助。</summary>
    internal static class Draw
    {
        public static GraphicsPath Round(RectangleF r, float radius)
        {
            GraphicsPath p = new GraphicsPath();
            float d = Math.Min(radius * 2f, Math.Min(r.Width, r.Height));
            if (d <= 1f || float.IsNaN(d))
            {
                p.AddRectangle(r);
                return p;
            }
            p.AddArc(r.X, r.Y, d, d, 180, 90);
            p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
            p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
            p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
            p.CloseFigure();
            return p;
        }

        public static void FillRound(Graphics g, Color c, RectangleF r, float radius)
        {
            if (r.Width <= 0 || r.Height <= 0) return;
            SmoothingMode old = g.SmoothingMode;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            using (GraphicsPath p = Round(r, radius))
            using (SolidBrush b = new SolidBrush(c))
            {
                g.FillPath(b, p);
            }
            g.SmoothingMode = old;
        }

        public static void StrokeRound(Graphics g, Color c, RectangleF r, float radius, float width)
        {
            if (r.Width <= 0 || r.Height <= 0) return;
            SmoothingMode old = g.SmoothingMode;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            using (GraphicsPath p = Round(r, radius))
            using (Pen pen = new Pen(c, width))
            {
                pen.Alignment = PenAlignment.Inset;
                g.DrawPath(pen, p);
            }
            g.SmoothingMode = old;
        }

        public static void Dot(Graphics g, float cx, float cy, float radius, Color c)
        {
            SmoothingMode old = g.SmoothingMode;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            using (SolidBrush b = new SolidBrush(c))
            {
                g.FillEllipse(b, cx - radius, cy - radius, radius * 2f, radius * 2f);
            }
            g.SmoothingMode = old;
        }

        public static void Line(Graphics g, Color c, float width, float x1, float y1, float x2, float y2)
        {
            using (Pen pen = new Pen(c, width))
            {
                g.DrawLine(pen, x1, y1, x2, y2);
            }
        }

        private const TextFormatFlags BaseFlags = TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine
            | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis;

        public static void TLeft(Graphics g, string s, Font f, Color c, Rectangle r, bool ellipsis)
        {
            if (string.IsNullOrEmpty(s) || r.Width <= 2) return;
            TextFormatFlags flags = BaseFlags;
            if (!ellipsis) flags &= ~TextFormatFlags.EndEllipsis;
            TextRenderer.DrawText(g, s, f, r, c, flags);
        }

        public static void TRight(Graphics g, string s, Font f, Color c, Rectangle r)
        {
            if (string.IsNullOrEmpty(s) || r.Width <= 2) return;
            TextRenderer.DrawText(g, s, f, r, c, BaseFlags | TextFormatFlags.Right);
        }

        public static void TCenter(Graphics g, string s, Font f, Color c, Rectangle r)
        {
            if (string.IsNullOrEmpty(s) || r.Width <= 2) return;
            TextRenderer.DrawText(g, s, f, r, c, BaseFlags | TextFormatFlags.HorizontalCenter);
        }

        public static void TWrap(Graphics g, string s, Font f, Color c, Rectangle r)
        {
            if (string.IsNullOrEmpty(s) || r.Width <= 2 || r.Height <= 2) return;
            TextRenderer.DrawText(g, s, f, r, c, TextFormatFlags.NoPrefix | TextFormatFlags.WordBreak | TextFormatFlags.EndEllipsis);
        }

        /// <summary>设备类型几何图标。</summary>
        public static void Kind(Graphics g, DeviceKind kind, RectangleF r, Color color, float stroke)
        {
            SmoothingMode old = g.SmoothingMode;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            float x = r.X;
            float y = r.Y;
            float w = r.Width;
            float h = r.Height;
            using (Pen pen = new Pen(color, stroke))
            {
                pen.StartCap = LineCap.Round;
                pen.EndCap = LineCap.Round;
                pen.LineJoin = LineJoin.Round;
                switch (kind)
                {
                    case DeviceKind.Mouse:
                        {
                            RectangleF body = new RectangleF(x + w * 0.28f, y + h * 0.10f, w * 0.44f, h * 0.80f);
                            using (GraphicsPath p = Round(body, body.Width * 0.5f))
                            {
                                g.DrawPath(pen, p);
                            }
                            g.DrawLine(pen, x + w * 0.5f, y + h * 0.12f, x + w * 0.5f, y + h * 0.36f);
                            break;
                        }
                    case DeviceKind.Keyboard:
                        {
                            RectangleF body = new RectangleF(x + w * 0.04f, y + h * 0.26f, w * 0.92f, h * 0.48f);
                            using (GraphicsPath p = Round(body, h * 0.10f))
                            {
                                g.DrawPath(pen, p);
                            }
                            g.DrawLine(pen, x + w * 0.24f, y + h * 0.60f, x + w * 0.76f, y + h * 0.60f);
                            break;
                        }
                    case DeviceKind.Headset:
                        {
                            g.DrawArc(pen, x + w * 0.16f, y + h * 0.14f, w * 0.68f, h * 0.64f, 180f, 180f);
                            using (SolidBrush b = new SolidBrush(color))
                            {
                                g.FillRectangle(b, x + w * 0.12f, y + h * 0.48f, w * 0.16f, h * 0.34f);
                                g.FillRectangle(b, x + w * 0.72f, y + h * 0.48f, w * 0.16f, h * 0.34f);
                            }
                            break;
                        }
                    case DeviceKind.Gamepad:
                        {
                            RectangleF body = new RectangleF(x + w * 0.04f, y + h * 0.30f, w * 0.92f, h * 0.40f);
                            using (GraphicsPath p = Round(body, h * 0.20f))
                            {
                                g.DrawPath(pen, p);
                            }
                            using (SolidBrush b = new SolidBrush(color))
                            {
                                g.FillEllipse(b, x + w * 0.24f, y + h * 0.44f, w * 0.12f, h * 0.12f);
                                g.FillEllipse(b, x + w * 0.64f, y + h * 0.44f, w * 0.12f, h * 0.12f);
                            }
                            break;
                        }
                    case DeviceKind.Presenter:
                        {
                            RectangleF body = new RectangleF(x + w * 0.34f, y + h * 0.10f, w * 0.32f, h * 0.80f);
                            using (GraphicsPath p = Round(body, w * 0.16f))
                            {
                                g.DrawPath(pen, p);
                            }
                            using (SolidBrush b = new SolidBrush(color))
                            {
                                g.FillEllipse(b, x + w * 0.44f, y + h * 0.22f, w * 0.12f, w * 0.12f);
                            }
                            break;
                        }
                    case DeviceKind.Numpad:
                        {
                            using (SolidBrush b = new SolidBrush(color))
                            {
                                for (int i = 0; i < 3; i++)
                                {
                                    for (int j = 0; j < 3; j++)
                                    {
                                        g.FillEllipse(b, x + w * (0.20f + i * 0.26f), y + h * (0.20f + j * 0.26f), w * 0.12f, w * 0.12f);
                                    }
                                }
                            }
                            break;
                        }
                    case DeviceKind.Other:
                        {
                            g.DrawEllipse(pen, x + w * 0.18f, y + h * 0.18f, w * 0.64f, h * 0.64f);
                            using (SolidBrush b = new SolidBrush(color))
                            {
                                g.FillEllipse(b, x + w * 0.42f, y + h * 0.42f, w * 0.16f, h * 0.16f);
                            }
                            break;
                        }
                    default:
                        {
                            RectangleF body = new RectangleF(x + w * 0.20f, y + h * 0.22f, w * 0.60f, h * 0.56f);
                            using (GraphicsPath p = Round(body, h * 0.18f))
                            {
                                g.DrawPath(pen, p);
                            }
                            break;
                        }
                }
            }
            g.SmoothingMode = old;
        }

        /// <summary>闪电（充电）。</summary>
        public static void Bolt(Graphics g, RectangleF r, Color color)
        {
            SmoothingMode old = g.SmoothingMode;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            PointF[] pts = new PointF[]
            {
                new PointF(r.X + r.Width * 0.60f, r.Y + r.Height * 0.00f),
                new PointF(r.X + r.Width * 0.18f, r.Y + r.Height * 0.58f),
                new PointF(r.X + r.Width * 0.45f, r.Y + r.Height * 0.58f),
                new PointF(r.X + r.Width * 0.36f, r.Y + r.Height * 1.00f),
                new PointF(r.X + r.Width * 0.84f, r.Y + r.Height * 0.40f),
                new PointF(r.X + r.Width * 0.54f, r.Y + r.Height * 0.40f)
            };
            using (SolidBrush b = new SolidBrush(color))
            {
                g.FillPolygon(b, pts);
            }
            g.SmoothingMode = old;
        }

        public static void Chevron(Graphics g, RectangleF r, Color color, bool down, float stroke)
        {
            SmoothingMode old = g.SmoothingMode;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            using (Pen pen = new Pen(color, stroke))
            {
                pen.StartCap = LineCap.Round;
                pen.EndCap = LineCap.Round;
                if (down)
                {
                    g.DrawLine(pen, r.X, r.Y + r.Height * 0.25f, r.X + r.Width * 0.5f, r.Y + r.Height * 0.75f);
                    g.DrawLine(pen, r.X + r.Width * 0.5f, r.Y + r.Height * 0.75f, r.Right, r.Y + r.Height * 0.25f);
                }
                else
                {
                    g.DrawLine(pen, r.X, r.Y + r.Height * 0.70f, r.X + r.Width * 0.5f, r.Y + r.Height * 0.25f);
                    g.DrawLine(pen, r.X + r.Width * 0.5f, r.Y + r.Height * 0.25f, r.Right, r.Y + r.Height * 0.70f);
                }
            }
            g.SmoothingMode = old;
        }
    }

    /// <summary>文本格式化。</summary>
    internal static class Fmt
    {
        public static string Percent(int? p)
        {
            return p.HasValue ? p.Value + "%" : "—";
        }

        public static string Time(DateTime t)
        {
            try { return t.ToString("HH:mm:ss"); } catch { return "--:--:--"; }
        }

        public static string Signal(int? s)
        {
            return s.HasValue ? s.Value + "%" : "—";
        }

        public static string Mv(int? mv)
        {
            return mv.HasValue ? mv.Value + " mV" : "—";
        }

        public static string Hex4(int v)
        {
            return "0x" + v.ToString("X4");
        }

        public static string IdPair(int vid, int pid)
        {
            return Hex4(vid) + ":" + Hex4(pid);
        }

        public static string Clip(string s, int max)
        {
            if (s == null) return "";
            if (max <= 0) return "";
            return s.Length <= max ? s : s.Substring(0, max - 1) + "…";
        }

        public static string SubLine(Peripheral d)
        {
            if (d == null) return "";
            string brand = string.IsNullOrEmpty(d.Brand) ? "未知品牌" : d.Brand;
            string conn = string.IsNullOrEmpty(d.Connection) ? "未知连接" : d.Connection;
            string s = brand + " · " + conn;
            if (d.Simulated) s += " · 演示";
            return s;
        }
    }
}