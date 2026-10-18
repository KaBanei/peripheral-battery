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
        private const int HeaderLogical = 4;
        private const int RowLogical = 19;
        private const int FooterLogical = 4;

        private readonly Timer _timer;
        private readonly ToolTip _tip;
        private readonly ContextMenuStrip _menu;
        private List<Peripheral> _devices = new List<Peripheral>();
        private Screen _screen;
        private bool _shouldShow;
        private int _hoverIndex = -1;
        private string _side = "right";
        private double _yRatio = 0.5;
        private int _opacityPercent = 100;
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
            _timer.Tick += delegate { TickAutoHide(); };

            DpiChanged += delegate
            {
                UpdateSize();
                Reposition();
            };
        }

        public string Side { get { return _side; } }
        public double YRatio { get { return _yRatio; } }

        public void Configure(string side, double yRatio, int opacityPercent)
        {
            if (side == "left" || side == "right") _side = side;
            if (yRatio < 0) yRatio = 0;
            if (yRatio > 1) yRatio = 1;
            if (opacityPercent < 20) opacityPercent = 20;
            if (opacityPercent > 100) opacityPercent = 100;
            _yRatio = yRatio;
            _opacityPercent = opacityPercent;
            ApplyOpacity();
        }

        private void ApplyOpacity()
        {
            try
            {
                double o = _opacityPercent / 100.0;
                if (o < 0.2) o = 0.2;
                if (o > 1) o = 1;
                Opacity = o;
            }
            catch { }
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
            ApplyOpacity();
            if (!Visible) Show();
            // Show() 会触发 WinForms 的 DPI 自动缩放（宽度会被改掉），显示后再应用一次真实尺寸。
            UpdateSize();
            Reposition();
            ApplyOpacity();
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
            ClientSize = new Size(Theme.S(this, 18), Theme.S(this, logical));
            UpdateRegion();
        }

        private void UpdateRegion()
        {
            if (Width <= 0 || Height <= 0) return;
            try
            {
                using (GraphicsPath p = Draw.Round(new RectangleF(0, 0, Width, Height), Theme.SF(this, 8f)))
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
            Draw.FillRound(g, Theme.Card, bg, Theme.SF(this, 8f));
            Draw.StrokeRound(g, Theme.Border, bg, Theme.SF(this, 8f), 1f);

            int rowH = Theme.S(this, RowLogical);
            int top0 = Theme.S(this, HeaderLogical);
            int count = _devices.Count;
            if (count > 8) count = 8;
            int trackW = Theme.S(this, 5);
            int trackH = Theme.S(this, 13);
            int tx = (Width - trackW) / 2;
            if (count == 0)
            {
                DrawEmpty(g, tx, top0 + (rowH - trackH) / 2, trackW, trackH);
            }
            for (int i = 0; i < count; i++)
            {
                Peripheral d = _devices[i];
                int top = top0 + i * rowH;
                if (i == _hoverIndex)
                {
                    Draw.FillRound(g, Theme.CardHover,
                        new RectangleF(1, top + 1, Width - 2, rowH - 2),
                        Theme.SF(this, 4f));
                }
                int ty = top + (rowH - trackH) / 2;
                DrawEmpty(g, tx, ty, trackW, trackH);
                if (d.Percent.HasValue)
                {
                    int value = d.Percent.Value;
                    if (value < 0) value = 0;
                    if (value > 100) value = 100;
                    int fillH = (int)Math.Round(trackH * value / 100.0);
                    if (fillH < 2) fillH = 2;
                    Color c = Theme.BatteryColor(d.Percent, d.State);
                    Draw.FillRound(g, c,
                        new RectangleF(tx, ty + trackH - fillH, trackW, fillH),
                        Theme.SF(this, 2.5f));
                }
                else
                {
                    using (SolidBrush b = new SolidBrush(Theme.Gray))
                    {
                        g.FillRectangle(b, tx + 1, ty + trackH / 2, Math.Max(1, trackW - 2), Math.Max(1, Theme.S(this, 1)));
                    }
                }
            }
            base.OnPaint(e);
        }

        private void DrawEmpty(Graphics g, int x, int y, int w, int h)
        {
            RectangleF r = new RectangleF(x, y, w, h);
            Draw.FillRound(g, Theme.Field, r, Theme.SF(this, 2.5f));
            Draw.StrokeRound(g, Theme.Border, r, Theme.SF(this, 2.5f), 1f);
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

        private void TickAutoHide()
        {
            if (!_shouldShow) return;
            bool fs = ShouldHideForForeground();
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
                ApplyOpacity();
            }
        }

        /// <summary>前台窗口与浮窗在同一屏幕，且最大化或全屏时隐藏。</summary>
        private bool ShouldHideForForeground()
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

                Screen fg = Screen.FromHandle(h);
                Screen mine = _screen ?? Screen.FromControl(this);
                if (fg.DeviceName != mine.DeviceName) return false;

                if (IsZoomed(h)) return true;

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
        [DllImport("user32.dll")] private static extern bool IsZoomed(IntPtr hWnd);
        [DllImport("user32.dll")] private static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);
        [DllImport("user32.dll")] private static extern IntPtr MonitorFromWindow(IntPtr hwnd, uint dwFlags);
        [DllImport("user32.dll")] private static extern bool GetMonitorInfo(IntPtr hMonitor, ref MONITORINFO lpmi);
        [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint pid);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetClassName(IntPtr hWnd, StringBuilder lpClassName, int nMaxCount);
    }
}