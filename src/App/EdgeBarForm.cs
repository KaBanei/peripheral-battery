using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Forms;
using Pb.Core;

namespace Pb.App
{
    /// <summary>
    /// 最小化后的侧边浮窗：贴屏幕右缘，只显示每台设备的电量数字。
    /// 单击恢复主面板；全屏应用在前台时自动隐藏，退出全屏后恢复。
    /// </summary>
    internal sealed class EdgeBarForm : Form
    {
        private const int HeaderLogical = 26;
        private const int RowLogical = 46;
        private const int FooterLogical = 8;

        private readonly Timer _timer;
        private readonly ToolTip _tip;
        private readonly ContextMenuStrip _menu;
        private List<Peripheral> _devices = new List<Peripheral>();
        private Screen _screen;
        private bool _shouldShow;
        private int _hoverIndex = -1;
        private string _side = "right";
        private double _yRatio = 0.5;
        private bool _dragging;
        private Point _dragOffset;
        private Point _dragStartScreen;

        public event EventHandler RestoreRequested;
        public event EventHandler PositionChanged;
        public event EventHandler RefreshRequested;
        public event EventHandler ExitRequested;

        public EdgeBarForm()
        {
            Theme.InitForm(this, "2.4G 电量");
            ShowInTaskbar = false;
            TopMost = true;
            FormBorderStyle = FormBorderStyle.None;
            StartPosition = FormStartPosition.Manual;
            SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint
                | ControlStyles.ResizeRedraw | ControlStyles.SupportsTransparentBackColor, true);

            _tip = new ToolTip();
            _tip.InitialDelay = 350;
            _tip.ReshowDelay = 100;

            _menu = new ContextMenuStrip();
            _menu.Renderer = new DarkMenuRenderer();
            _menu.Font = Theme.FontBody;
            _menu.BackColor = Theme.Card;
            _menu.ForeColor = Theme.Text;
            _menu.ShowImageMargin = false;
            ToolStripMenuItem miOpen = new ToolStripMenuItem("打开主界面");
            miOpen.Click += delegate { Fire(RestoreRequested); };
            _menu.Items.Add(miOpen);
            ToolStripMenuItem miRefresh = new ToolStripMenuItem("立即刷新");
            miRefresh.Click += delegate { Fire(RefreshRequested); };
            _menu.Items.Add(miRefresh);
            _menu.Items.Add(new ToolStripSeparator());
            ToolStripMenuItem miExit = new ToolStripMenuItem("退出");
            miExit.Click += delegate { Fire(ExitRequested); };
            _menu.Items.Add(miExit);
            ContextMenuStrip = _menu;

            _timer = new Timer();
            _timer.Interval = 700;
            _timer.Tick += delegate { TickFullscreen(); };

            DpiChanged += delegate
            {
                UpdateSize();
                Reposition();
            };
        }

        public string Side { get { return _side; } }
        public double YRatio { get { return _yRatio; } }

        public void Configure(string side, double yRatio)
        {
            if (side == "left" || side == "right") _side = side;
            if (yRatio < 0) yRatio = 0;
            if (yRatio > 1) yRatio = 1;
            _yRatio = yRatio;
        }

        private static void Fire(EventHandler h)
        {
            if (h != null) h(null, EventArgs.Empty);
        }

        public void SetData(IList<Peripheral> devices)
        {
            _devices = devices == null ? new List<Peripheral>() : new List<Peripheral>(devices);
            UpdateSize();
            if (Visible || _shouldShow) Reposition();
            Invalidate();
        }

        public void ShowBar(Screen screen)
        {
            _screen = screen ?? Screen.PrimaryScreen;
            _shouldShow = true;
            UpdateSize();
            Reposition();
            if (!Visible) Show();
            // Show() 会触发 WinForms 的 DPI 自动缩放（宽度会被改掉），显示后再应用一次真实尺寸。
            UpdateSize();
            Reposition();
            _timer.Start();
            Invalidate();
        }

        public void HideBar()
        {
            _shouldShow = false;
            _timer.Stop();
            Hide();
        }

        private void UpdateSize()
        {
            int n = _devices.Count;
            if (n < 1) n = 1;
            if (n > 8) n = 8;
            int logical = HeaderLogical + n * RowLogical + FooterLogical;
            ClientSize = new Size(Theme.S(this, 62), Theme.S(this, logical));
            UpdateRegion();
        }

        private void UpdateRegion()
        {
            if (Width <= 0 || Height <= 0) return;
            try
            {
                using (GraphicsPath p = Draw.Round(new RectangleF(0, 0, Width, Height), Theme.SF(this, 11f)))
                {
                    Region old = Region;
                    Region = new Region(p);
                    if (old != null) old.Dispose();
                }
            }
            catch { }
        }

        private void Reposition()
        {
            Screen scr = _screen ?? Screen.PrimaryScreen;
            Rectangle wa = scr.WorkingArea;
            int gap = Theme.S(this, 6);
            int x = _side == "left" ? wa.Left + gap : wa.Right - Width - gap;
            int y = wa.Top + (int)Math.Round((wa.Height - Height) * _yRatio);
            if (x < wa.Left) x = wa.Left;
            if (x + Width > wa.Right) x = wa.Right - Width;
            if (y < wa.Top) y = wa.Top;
            if (y + Height > wa.Bottom) y = wa.Bottom - Height;
            Location = new Point(x, y);
        }

        private void SnapToNearestEdge()
        {
            Point centerPoint = new Point(Left + Width / 2, Top + Height / 2);
            Screen scr = Screen.FromPoint(centerPoint);
            Rectangle wa = scr.WorkingArea;
            int center = Left + Width / 2;
            _side = center < wa.Left + wa.Width / 2 ? "left" : "right";
            _screen = scr;
            int y = Top;
            if (y < wa.Top) y = wa.Top;
            if (y + Height > wa.Bottom) y = wa.Bottom - Height;
            _yRatio = wa.Height > Height ? (double)(y - wa.Top) / (wa.Height - Height) : 0;
            Reposition();
            Fire(PositionChanged);
        }

        protected override bool ShowWithoutActivation { get { return true; } }

        protected override CreateParams CreateParams
        {
            get
            {
                CreateParams cp = base.CreateParams;
                cp.ExStyle |= 0x08000000; // WS_EX_NOACTIVATE
                cp.ExStyle |= 0x00000008; // WS_EX_TOPMOST
                cp.ClassStyle |= 0x00020000; // CS_DROPSHADOW
                return cp;
            }
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            UpdateRegion();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            RectangleF bg = new RectangleF(0.5f, 0.5f, Width - 1f, Height - 1f);
            Draw.FillRound(g, Theme.Card, bg, Theme.SF(this, 11f));
            Draw.StrokeRound(g, Theme.Border, bg, Theme.SF(this, 11f), 1f);

            DrawBattery(g, Width / 2, Theme.S(this, 11), Theme.S(this, 20), Theme.S(this, 10));

            int rowH = Theme.S(this, RowLogical);
            int top0 = Theme.S(this, HeaderLogical);
            int count = _devices.Count;
            if (count > 8) count = 8;
            if (count == 0)
            {
                TextRenderer.DrawText(g, "--", Theme.FontNum,
                    new Rectangle(0, top0, Width, rowH), Theme.Sub,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
            }
            for (int i = 0; i < count; i++)
            {
                Peripheral d = _devices[i];
                int top = top0 + i * rowH;
                if (i == _hoverIndex)
                {
                    Draw.FillRound(g, Theme.CardHover,
                        new RectangleF(Theme.S(this, 3), top + Theme.S(this, 3), Width - Theme.S(this, 6), rowH - Theme.S(this, 6)),
                        Theme.SF(this, 8f));
                }
                Color c = Theme.BatteryColor(d.Percent, d.State);
                Draw.FillRound(g, c,
                    new RectangleF(Theme.S(this, 8), top + Theme.S(this, 9), Theme.S(this, 3), Theme.S(this, 24)),
                    Theme.SF(this, 1.5f));

                string pct = d.Percent.HasValue ? d.Percent.Value.ToString() : "--";
                TextRenderer.DrawText(g, pct, Theme.FontNum,
                    new Rectangle(Theme.S(this, 12), top + Theme.S(this, 4), Width - Theme.S(this, 16), Theme.S(this, 23)),
                    c, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
                TextRenderer.DrawText(g, KindShort(d.Kind), Theme.FontSmall,
                    new Rectangle(Theme.S(this, 12), top + Theme.S(this, 27), Width - Theme.S(this, 16), Theme.S(this, 12)),
                    Theme.Sub, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
                if (i < count - 1)
                {
                    using (Pen pen = new Pen(Theme.Divider, 1f))
                    {
                        g.DrawLine(pen, Theme.S(this, 16), top + rowH - 1, Width - Theme.S(this, 16), top + rowH - 1);
                    }
                }
            }

            using (SolidBrush b = new SolidBrush(Theme.Gray))
            {
                int dot = Theme.S(this, 2);
                int cy = Height - Theme.S(this, 7);
                g.FillEllipse(b, Width / 2 - dot * 3, cy, dot, dot);
                g.FillEllipse(b, Width / 2, cy, dot, dot);
                g.FillEllipse(b, Width / 2 + dot * 3, cy, dot, dot);
            }
            base.OnPaint(e);
        }

        private void DrawBattery(Graphics g, int cx, int cy, int w, int h)
        {
            RectangleF body = new RectangleF(cx - w / 2f, cy - h / 2f, w, h);
            Draw.StrokeRound(g, Theme.Sub, body, Theme.SF(this, 2f), 1.2f);
            using (SolidBrush b = new SolidBrush(Theme.Sub))
            {
                g.FillRectangle(b, cx + w / 2f + 1, cy - Theme.S(this, 2), Theme.S(this, 2), Theme.S(this, 4));
            }
        }

        private static string KindShort(DeviceKind k)
        {
            switch (k)
            {
                case DeviceKind.Mouse: return "鼠标";
                case DeviceKind.Keyboard: return "键盘";
                case DeviceKind.Headset: return "耳机";
                case DeviceKind.Gamepad: return "手柄";
                default: return "设备";
            }
        }

        private int HitRow(int y)
        {
            int top0 = Theme.S(this, HeaderLogical);
            int rowH = Theme.S(this, RowLogical);
            if (y < top0) return -1;
            int i = (y - top0) / rowH;
            int count = _devices.Count;
            if (count > 8) count = 8;
            if (count == 0) count = 1;
            if (i < 0 || i >= count) return -1;
            return i;
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            base.OnMouseLeave(e);
            _hoverIndex = -1;
            Invalidate();
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (e.Button == MouseButtons.Left)
            {
                _dragOffset = e.Location;
                _dragStartScreen = PointToScreen(e.Location);
                _dragging = false;
                Capture = true;
                Cursor = Cursors.SizeAll;
            }
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            if (e.Button != MouseButtons.Left) return;
            bool moved = _dragging;
            _dragging = false;
            Capture = false;
            Cursor = Cursors.Hand;
            if (moved) SnapToNearestEdge();
            else Fire(RestoreRequested);
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            if (Capture && (Control.MouseButtons & MouseButtons.Left) != 0)
            {
                Point screen = PointToScreen(e.Location);
                if (!_dragging)
                {
                    int dx = screen.X - _dragStartScreen.X;
                    int dy = screen.Y - _dragStartScreen.Y;
                    if (Math.Abs(dx) > 6 || Math.Abs(dy) > 6) _dragging = true;
                }
                if (_dragging)
                {
                    Location = new Point(screen.X - _dragOffset.X, screen.Y - _dragOffset.Y);
                    return;
                }
            }
            int i = HitRow(e.Y);
            if (i != _hoverIndex)
            {
                _hoverIndex = i;
                Invalidate();
            }
            if (i >= 0 && i < _devices.Count)
            {
                Peripheral d = _devices[i];
                _tip.SetToolTip(this, d.Name + "  " + Fmt.Percent(d.Percent) + "  " + Theme.StateText(d.State));
            }
            Cursor = i >= 0 ? Cursors.Hand : Cursors.Default;
        }

        private void TickFullscreen()
        {
            if (!_shouldShow) return;
            bool fs = IsForegroundFullscreen();
            if (fs)
            {
                if (Visible) Hide();
            }
            else if (!Visible)
            {
                UpdateSize();
                Reposition();
                Show();
                UpdateSize();
                Reposition();
            }
        }

        private static bool IsForegroundFullscreen()
        {
            try
            {
                IntPtr h = GetForegroundWindow();
                if (h == IntPtr.Zero) return false;
                uint pid;
                GetWindowThreadProcessId(h, out pid);
                if (pid == (uint)Process.GetCurrentProcess().Id) return false;
                string cls = ClassName(h);
                if (cls == "Progman" || cls == "WorkerW" || cls == "Shell_TrayWnd") return false;
                RECT r;
                if (!GetWindowRect(h, out r)) return false;
                IntPtr mon = MonitorFromWindow(h, 2);
                MONITORINFO mi = new MONITORINFO();
                mi.cbSize = Marshal.SizeOf(typeof(MONITORINFO));
                if (!GetMonitorInfo(mon, ref mi)) return false;
                return r.Left <= mi.rcMonitor.Left + 1
                    && r.Top <= mi.rcMonitor.Top + 1
                    && r.Right >= mi.rcMonitor.Right - 1
                    && r.Bottom >= mi.rcMonitor.Bottom - 1;
            }
            catch { return false; }
        }

        private static string ClassName(IntPtr h)
        {
            try
            {
                StringBuilder sb = new StringBuilder(256);
                GetClassName(h, sb, sb.Capacity);
                return sb.ToString();
            }
            catch { return ""; }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                try { _timer.Dispose(); } catch { }
                try { _tip.Dispose(); } catch { }
                try { _menu.Dispose(); } catch { }
            }
            base.Dispose(disposing);
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct RECT { public int Left, Top, Right, Bottom; }

        [StructLayout(LayoutKind.Sequential)]
        private struct MONITORINFO
        {
            public int cbSize;
            public RECT rcMonitor;
            public RECT rcWork;
            public uint dwFlags;
        }

        [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
        [DllImport("user32.dll")] private static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);
        [DllImport("user32.dll")] private static extern IntPtr MonitorFromWindow(IntPtr hwnd, uint dwFlags);
        [DllImport("user32.dll")] private static extern bool GetMonitorInfo(IntPtr hMonitor, ref MONITORINFO lpmi);
        [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint pid);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetClassName(IntPtr hWnd, StringBuilder lpClassName, int nMaxCount);
    }
}