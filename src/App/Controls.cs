using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;
using Pb.Core;

namespace Pb.App
{
    /// <summary>无边框深色窗口基类：圆角 + 阴影 + 可拖动标题栏 + 关闭按钮。</summary>
    internal class ChromeForm : Form
    {
        public const int TitleBarHeight = 52;
        private readonly string _title;
        protected readonly RoundButton CloseButton;
        protected readonly RoundButton MinButton;
        protected bool ShowMinimizeButton;

        public ChromeForm(string title)
        {
            _title = title;
            Theme.InitForm(this, title);
            SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint | ControlStyles.ResizeRedraw, true);
            CloseButton = new RoundButton();
            CloseButton.Glyph = RoundButton.GlyphKind.Close;
            CloseButton.Size = new Size(30, 30);
            CloseButton.GlyphColor = Theme.Sub;
            CloseButton.Click += delegate { OnCloseClicked(); };
            Controls.Add(CloseButton);

            MinButton = new RoundButton();
            MinButton.Glyph = RoundButton.GlyphKind.Minimize;
            MinButton.Size = new Size(30, 30);
            MinButton.GlyphColor = Theme.Sub;
            MinButton.Visible = false;
            Controls.Add(MinButton);
        }

        protected string TitleText { get { return _title; } }

        protected virtual void OnCloseClicked()
        {
            Close();
        }

        protected override CreateParams CreateParams
        {
            get
            {
                CreateParams cp = base.CreateParams;
                cp.ClassStyle |= Native.CS_DROPSHADOW;
                return cp;
            }
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            UpdateRegion();
        }

        private void UpdateRegion()
        {
            if (Width <= 0 || Height <= 0) return;
            try
            {
                using (GraphicsPath p = Draw.Round(new RectangleF(0, 0, Width, Height), Theme.SF(this, 12f)))
                {
                    Region old = Region;
                    Region = new Region(p);
                    if (old != null) old.Dispose();
                }
            }
            catch { }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;

            int th = Theme.S(this, TitleBarHeight);
            Draw.TLeft(g, _title, Theme.FontTitle, Theme.Text,
                new Rectangle(Theme.S(this, 20), Theme.S(this, 13), Width - Theme.S(this, 90), th - Theme.S(this, 20)), true);
            Draw.Line(g, Theme.Divider, 1f, Theme.S(this, 20), th, Width - Theme.S(this, 20), th);
            Draw.StrokeRound(g, Theme.Border, new RectangleF(0.5f, 0.5f, Width - 1, Height - 1), Theme.SF(this, 12f), 1f);

            base.OnPaint(e);
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (e.Button == MouseButtons.Left && e.Y <= Theme.S(this, TitleBarHeight))
            {
                Native.ReleaseCapture();
                Native.SendMessage(Handle, Native.WM_NCLBUTTONDOWN, (IntPtr)Native.HTCAPTION, IntPtr.Zero);
            }
        }

        protected void LayoutChrome()
        {
            int y = Theme.S(this, 12);
            int size = Theme.S(this, 30);
            CloseButton.Bounds = new Rectangle(Width - Theme.S(this, 14) - size, y, size, size);
            MinButton.Bounds = new Rectangle(Width - Theme.S(this, 14) - size * 2 - Theme.S(this, 4), y, size, size);
            MinButton.Visible = ShowMinimizeButton;
        }
    }

    /// <summary>圆形图标按钮。</summary>
    internal class RoundButton : Control
    {
        public enum GlyphKind
        {
            Refresh,
            Close,
            Minimize
        }

        private bool _hover;
        private bool _down;

        public GlyphKind Glyph { get; set; }
        public Color GlyphColor { get; set; }

        public RoundButton()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint
                | ControlStyles.OptimizedDoubleBuffer | ControlStyles.SupportsTransparentBackColor, true);
            BackColor = Color.Transparent;
            Cursor = Cursors.Hand;
            TabStop = false;
            GlyphColor = Theme.Sub;
            Size = new Size(30, 30);
        }

        protected override void OnMouseEnter(EventArgs e) { _hover = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { _hover = false; _down = false; Invalidate(); base.OnMouseLeave(e); }
        protected override void OnMouseDown(MouseEventArgs e) { if (e.Button == MouseButtons.Left) { _down = true; Invalidate(); } base.OnMouseDown(e); }
        protected override void OnMouseUp(MouseEventArgs e) { _down = false; Invalidate(); base.OnMouseUp(e); }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            float s = Theme.ScaleFactor(this);
            if (_hover)
            {
                Draw.FillRound(g, _down ? Theme.Field : Theme.Tile, new RectangleF(0, 0, Width, Height), Width / 2f);
            }
            float cx = Width / 2f;
            float cy = Height / 2f;
            float rad = Math.Min(Width, Height) * 0.30f;
            Color c = _hover ? Theme.Text : GlyphColor;
            using (Pen pen = new Pen(c, Math.Max(1.2f, 1.5f * s)))
            {
                pen.StartCap = LineCap.Round;
                pen.EndCap = LineCap.Round;
                pen.LineJoin = LineJoin.Round;
                switch (Glyph)
                {
                    case GlyphKind.Refresh:
                        {
                            g.DrawArc(pen, cx - rad, cy - rad, rad * 2f, rad * 2f, 42f, 276f);
                            double a = 42.0 * Math.PI / 180.0;
                            float px = cx + rad * (float)Math.Cos(a);
                            float py = cy + rad * (float)Math.Sin(a);
                            float tx = -(float)Math.Sin(a);
                            float ty = (float)Math.Cos(a);
                            float ax = (float)Math.Cos(a);
                            float ay = (float)Math.Sin(a);
                            float len = rad * 0.62f;
                            using (SolidBrush b = new SolidBrush(c))
                            {
                                PointF[] tri = new PointF[]
                                {
                                    new PointF(px + tx * len * 0.55f - ax * len * 0.10f, py + ty * len * 0.55f - ay * len * 0.10f),
                                    new PointF(px - tx * len * 0.55f - ax * len * 0.10f, py - ty * len * 0.55f - ay * len * 0.10f),
                                    new PointF(px - tx * len * 0.10f + ax * len * 0.55f, py - ty * len * 0.10f + ay * len * 0.55f)
                                };
                                g.FillPolygon(b, tri);
                            }
                            break;
                        }
                    case GlyphKind.Close:
                        {
                            float d = rad * 0.78f;
                            g.DrawLine(pen, cx - d, cy - d, cx + d, cy + d);
                            g.DrawLine(pen, cx + d, cy - d, cx - d, cy + d);
                            break;
                        }
                    case GlyphKind.Minimize:
                        {
                            float d = rad * 0.80f;
                            g.DrawLine(pen, cx - d, cy, cx + d, cy);
                            break;
                        }
                }
            }
        }
    }

    /// <summary>圆角按钮。</summary>
    internal class DarkButton : Control
    {
        private bool _hover;
        private bool _down;

        public bool Primary { get; set; }
        public bool Danger { get; set; }

        public DarkButton()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint
                | ControlStyles.OptimizedDoubleBuffer | ControlStyles.SupportsTransparentBackColor, true);
            BackColor = Color.Transparent;
            Font = Theme.FontBody;
            ForeColor = Theme.Text;
            Cursor = Cursors.Hand;
            TabStop = false;
            Size = new Size(88, 32);
        }

        protected override void OnMouseEnter(EventArgs e) { _hover = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { _hover = false; _down = false; Invalidate(); base.OnMouseLeave(e); }
        protected override void OnMouseDown(MouseEventArgs e) { if (e.Button == MouseButtons.Left) { _down = true; Invalidate(); } base.OnMouseDown(e); }
        protected override void OnMouseUp(MouseEventArgs e) { _down = false; Invalidate(); base.OnMouseUp(e); }
        protected override void OnEnabledChanged(EventArgs e) { Invalidate(); base.OnEnabledChanged(e); }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            Color fill;
            Color text;
            if (!Enabled)
            {
                fill = Theme.Tile;
                text = Color.FromArgb(110, Theme.Sub);
            }
            else if (Primary)
            {
                fill = _down ? Theme.Accent : (_hover ? Color.FromArgb(255, 100, 175, 255) : Theme.Accent);
                text = Theme.OnAccent;
            }
            else if (Danger)
            {
                fill = _hover ? Color.FromArgb(60, Theme.Red) : Theme.Tile;
                text = _hover ? Theme.Red : Theme.Text;
            }
            else
            {
                fill = _hover ? Theme.CardHover : Theme.Field;
                text = _hover ? Theme.Text : Theme.Text;
            }
            Draw.FillRound(g, fill, new RectangleF(0, 0, Width, Height), Theme.SF(this, 8f));
            if (!Primary && !Danger)
            {
                Draw.StrokeRound(g, Theme.Border, new RectangleF(0.5f, 0.5f, Width - 1, Height - 1), Theme.SF(this, 8f), 1f);
            }
            Draw.TCenter(g, Text, Font, text, new Rectangle(0, 0, Width, Height));
        }
    }

    /// <summary>自绘复选框（深色，纯 Control 绘制）。</summary>
    internal class DarkCheckBox : Control
    {
        private bool _checked;
        private bool _hover;

        public event EventHandler CheckedChanged;

        public DarkCheckBox()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint
                | ControlStyles.OptimizedDoubleBuffer | ControlStyles.SupportsTransparentBackColor, true);
            BackColor = Color.Transparent;
            ForeColor = Theme.Text;
            Font = Theme.FontBody;
            Cursor = Cursors.Hand;
            TabStop = false;
            Size = new Size(200, 24);
        }

        public bool Checked
        {
            get { return _checked; }
            set
            {
                if (_checked == value) return;
                _checked = value;
                Invalidate();
                EventHandler h = CheckedChanged;
                if (h != null) h(this, EventArgs.Empty);
            }
        }

        protected override void OnMouseEnter(EventArgs e) { _hover = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { _hover = false; Invalidate(); base.OnMouseLeave(e); }

        protected override void OnClick(EventArgs e)
        {
            base.OnClick(e);
            if (!Enabled) return;
            Checked = !_checked;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            int box = Theme.S(this, 16);
            int y = (Height - box) / 2;
            RectangleF r = new RectangleF(0, y, box, box);
            Color fill = _checked ? (Enabled ? Theme.Accent : Theme.Gray) : (_hover ? Theme.Tile : Theme.Field);
            Draw.FillRound(g, fill, r, Theme.SF(this, 4f));
            if (!_checked)
            {
                Draw.StrokeRound(g, Theme.Border, new RectangleF(0.5f, y + 0.5f, box - 1, box - 1), Theme.SF(this, 4f), 1f);
            }
            if (_checked)
            {
                using (Pen pen = new Pen(Theme.OnAccent, Math.Max(1.4f, Theme.SF(this, 1.9f))))
                {
                    pen.StartCap = LineCap.Round;
                    pen.EndCap = LineCap.Round;
                    g.DrawLine(pen, r.X + box * 0.24f, r.Y + box * 0.52f, r.X + box * 0.44f, r.Y + box * 0.72f);
                    g.DrawLine(pen, r.X + box * 0.44f, r.Y + box * 0.72f, r.X + box * 0.78f, r.Y + box * 0.30f);
                }
            }
            int tx = box + Theme.S(this, 8);
            Draw.TLeft(g, Text, Font, Enabled ? Theme.Text : Theme.Sub, new Rectangle(tx, 0, Width - tx, Height), true);
        }
    }

    /// <summary>分段选择器。</summary>
    internal class SegmentControl : Control
    {
        private string[] _items = new string[0];
        private int _selected;
        private int _hoverIndex = -1;

        public event EventHandler SelectionChanged;

        public SegmentControl()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint
                | ControlStyles.OptimizedDoubleBuffer | ControlStyles.SupportsTransparentBackColor, true);
            BackColor = Color.Transparent;
            Font = Theme.FontBody;
            Cursor = Cursors.Hand;
            TabStop = false;
            Size = new Size(200, 30);
        }

        public string[] Items
        {
            get { return _items; }
            set
            {
                _items = value == null ? new string[0] : value;
                if (_selected >= _items.Length) _selected = Math.Max(0, _items.Length - 1);
                Invalidate();
            }
        }

        public int SelectedIndex
        {
            get { return _selected; }
            set
            {
                int v = value;
                if (v < 0) v = 0;
                if (v >= _items.Length) v = Math.Max(0, _items.Length - 1);
                if (v == _selected) return;
                _selected = v;
                Invalidate();
                EventHandler h = SelectionChanged;
                if (h != null) h(this, EventArgs.Empty);
            }
        }

        private int IndexAt(int x)
        {
            if (_items.Length == 0) return -1;
            int pad = Theme.S(this, 3);
            int inner = Width - pad * 2;
            if (inner <= 0) return -1;
            int idx = (x - pad) * _items.Length / inner;
            if (idx < 0) idx = 0;
            if (idx >= _items.Length) idx = _items.Length - 1;
            return idx;
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            int idx = IndexAt(e.X);
            if (idx != _hoverIndex) { _hoverIndex = idx; Invalidate(); }
            base.OnMouseMove(e);
        }

        protected override void OnMouseLeave(EventArgs e) { _hoverIndex = -1; Invalidate(); base.OnMouseLeave(e); }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (e.Button != MouseButtons.Left) return;
            int idx = IndexAt(e.X);
            if (idx >= 0) SelectedIndex = idx;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            Draw.FillRound(g, Theme.Field, new RectangleF(0, 0, Width, Height), Theme.SF(this, 8f));
            Draw.StrokeRound(g, Theme.Border, new RectangleF(0.5f, 0.5f, Width - 1, Height - 1), Theme.SF(this, 8f), 1f);
            if (_items.Length == 0) return;
            int pad = Theme.S(this, 3);
            int inner = Width - pad * 2;
            int segW = inner / _items.Length;
            for (int i = 0; i < _items.Length; i++)
            {
                int x = pad + i * segW;
                int w = (i == _items.Length - 1) ? (Width - pad - x) : segW;
                RectangleF cell = new RectangleF(x, pad, w, Height - pad * 2);
                if (i == _selected)
                {
                    Draw.FillRound(g, Theme.AccentSoft, cell, Theme.SF(this, 6f));
                    Draw.StrokeRound(g, Color.FromArgb(90, Theme.Accent), cell, Theme.SF(this, 6f), 1f);
                }
                else if (i == _hoverIndex)
                {
                    Draw.FillRound(g, Theme.Card, cell, Theme.SF(this, 6f));
                }
                Draw.TCenter(g, _items[i], Font, i == _selected ? Theme.Accent : Theme.Sub,
                    new Rectangle(x, 0, w, Height));
            }
        }
    }

    /// <summary>数值步进器。</summary>
    internal class Stepper : Control
    {
        private int _value;
        private int _hoverBtn = -1;
        private int _min;
        private int _max = 100;
        private int _step = 1;
        private string _suffix = "";

        public event EventHandler ValueChanged;

        public Stepper()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint
                | ControlStyles.OptimizedDoubleBuffer | ControlStyles.SupportsTransparentBackColor, true);
            BackColor = Color.Transparent;
            Font = Theme.FontBodyBold;
            Cursor = Cursors.Hand;
            TabStop = false;
            Size = new Size(120, 30);
        }

        public int Minimum { get { return _min; } set { _min = value; } }
        public int Maximum { get { return _max; } set { _max = value; } }
        public int Step { get { return _step; } set { _step = Math.Max(1, value); } }
        public string Suffix { get { return _suffix; } set { _suffix = value == null ? "" : value; Invalidate(); } }

        public int Value
        {
            get { return _value; }
            set
            {
                int v = value;
                if (v < _min) v = _min;
                if (v > _max) v = _max;
                if (v == _value) return;
                _value = v;
                Invalidate();
                EventHandler h = ValueChanged;
                if (h != null) h(this, EventArgs.Empty);
            }
        }

        private Rectangle MinusRect
        {
            get { return new Rectangle(Theme.S(this, 3), Theme.S(this, 3), Theme.S(this, 26), Height - Theme.S(this, 6)); }
        }

        private Rectangle PlusRect
        {
            get { return new Rectangle(Width - Theme.S(this, 29), Theme.S(this, 3), Theme.S(this, 26), Height - Theme.S(this, 6)); }
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            int h = MinusRect.Contains(e.Location) ? 0 : (PlusRect.Contains(e.Location) ? 1 : -1);
            if (h != _hoverBtn) { _hoverBtn = h; Invalidate(); }
            base.OnMouseMove(e);
        }

        protected override void OnMouseLeave(EventArgs e) { _hoverBtn = -1; Invalidate(); base.OnMouseLeave(e); }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (e.Button != MouseButtons.Left) return;
            if (MinusRect.Contains(e.Location)) Value = _value - _step;
            else if (PlusRect.Contains(e.Location)) Value = _value + _step;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            Draw.FillRound(g, Theme.Field, new RectangleF(0, 0, Width, Height), Theme.SF(this, 8f));
            Draw.StrokeRound(g, Theme.Border, new RectangleF(0.5f, 0.5f, Width - 1, Height - 1), Theme.SF(this, 8f), 1f);

            Rectangle minus = MinusRect;
            Rectangle plus = PlusRect;
            if (_hoverBtn == 0) Draw.FillRound(g, Theme.Tile, minus, Theme.SF(this, 6f));
            if (_hoverBtn == 1) Draw.FillRound(g, Theme.Tile, plus, Theme.SF(this, 6f));

            Color glyph = Theme.Sub;
            using (Pen pen = new Pen(glyph, Math.Max(1.4f, Theme.SF(this, 1.7f))))
            {
                pen.StartCap = LineCap.Round;
                pen.EndCap = LineCap.Round;
                float cx1 = minus.X + minus.Width / 2f;
                float cy1 = minus.Y + minus.Height / 2f;
                float d = Theme.SF(this, 5f);
                g.DrawLine(pen, cx1 - d, cy1, cx1 + d, cy1);
                float cx2 = plus.X + plus.Width / 2f;
                float cy2 = plus.Y + plus.Height / 2f;
                g.DrawLine(pen, cx2 - d, cy2, cx2 + d, cy2);
                g.DrawLine(pen, cx2, cy2 - d, cx2, cy2 + d);
            }

            string text = _value + _suffix;
            Draw.TCenter(g, text, Font, Theme.Text,
                new Rectangle(minus.Right, 0, plus.Left - minus.Right, Height));
        }
    }

    /// <summary>空状态/提示文案面板。</summary>
    internal class MessagePanel : Control
    {
        public string Headline { get; set; }
        public string[] Lines { get; set; }

        public MessagePanel()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint
                | ControlStyles.OptimizedDoubleBuffer | ControlStyles.SupportsTransparentBackColor, true);
            BackColor = Color.Transparent;
            Lines = new string[0];
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            float s = Theme.ScaleFactor(this);
            int lineH = (int)Math.Round(20 * s);
            int headH = (int)Math.Round(26 * s);
            int total = headH + Lines.Length * lineH;
            int y = Math.Max(0, (Height - total) / 2);
            int pad = Theme.S(this, 24);
            Draw.TCenter(g, Headline, Theme.FontH1, Theme.Text, new Rectangle(pad, y, Width - pad * 2, headH));
            y += headH;
            for (int i = 0; i < Lines.Length; i++)
            {
                Draw.TCenter(g, "· " + Lines[i], Theme.FontSub, Theme.Sub,
                    new Rectangle(pad, y, Width - pad * 2, lineH));
                y += lineH;
            }
        }
    }

    /// <summary>数据源状态列表。</summary>
    internal class StatusListView : Control
    {
        private List<ProviderStatus> _items = new List<ProviderStatus>();

        public StatusListView()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint
                | ControlStyles.OptimizedDoubleBuffer | ControlStyles.SupportsTransparentBackColor, true);
            BackColor = Color.Transparent;
        }

        public List<ProviderStatus> Items
        {
            get { return _items; }
            set { _items = value == null ? new List<ProviderStatus>() : value; Invalidate(); }
        }

        public static int PreferredHeight(float scale, int count)
        {
            int row = (int)Math.Round(26 * scale);
            int n = count < 1 ? 1 : count;
            return row * n + (int)Math.Round(8 * scale);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            float s = Theme.ScaleFactor(this);
            int row = (int)Math.Round(26 * s);
            int dot = (int)Math.Round(6 * s);
            int nameX = (int)Math.Round(20 * s);
            int nameW = (int)Math.Round(150 * s);
            int msX = nameX + nameW + (int)Math.Round(6 * s);
            int msW = (int)Math.Round(66 * s);
            int infoX = msX + msW + (int)Math.Round(6 * s);
            int infoW = Width - infoX - (int)Math.Round(8 * s);
            int y = (int)Math.Round(4 * s);
            for (int i = 0; i < _items.Count; i++)
            {
                ProviderStatus st = _items[i];
                if (y + row > Height) break;
                Color c = !st.Enabled ? Theme.Gray : (st.Ok ? Theme.Green : Theme.Red);
                Draw.Dot(g, Theme.SF(this, 8f), y + row / 2f, dot / 2f, c);
                Draw.TLeft(g, st.DisplayName, Theme.FontBody, Theme.Text, new Rectangle(nameX, y, nameW, row), true);
                string ms = st.DurationMs > 0 ? st.DurationMs + " ms" : "—";
                Draw.TLeft(g, ms, Theme.FontSub, Theme.Sub, new Rectangle(msX, y, msW, row), false);
                string info;
                Color infoColor;
                if (!st.Enabled)
                {
                    info = "已停用";
                    infoColor = Theme.Gray;
                }
                else if (!st.Ok)
                {
                    info = string.IsNullOrEmpty(st.Error) ? "不可用" : st.Error;
                    infoColor = Theme.Red;
                }
                else
                {
                    info = st.Count + " 台";
                    infoColor = Theme.Sub;
                }
                Draw.TLeft(g, info, Theme.FontSub, infoColor, new Rectangle(infoX, y, infoW, row), true);
                y += row;
            }
            if (_items.Count == 0)
            {
                Draw.TLeft(g, "尚未注册数据源", Theme.FontSub, Theme.Sub, new Rectangle(nameX, y, Width - nameX, row), true);
            }
        }
    }

    /// <summary>自绘细滚动条面板（避免系统浅色滚动条破坏深色主题）。</summary>
    internal class ScrollPanel : Panel
    {
        private int _contentHeight;
        private int _offset;

        public event EventHandler ScrollChanged;

        public ScrollPanel()
        {
            SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint
                | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
            DoubleBuffered = true;
            BackColor = Theme.Bg;
        }

        public int ContentHeight
        {
            get { return _contentHeight; }
            set
            {
                _contentHeight = Math.Max(0, value);
                ClampOffset();
                Invalidate();
            }
        }

        public int ScrollOffset { get { return _offset; } }

        private int MaxOffset
        {
            get { return Math.Max(0, _contentHeight - ClientSize.Height); }
        }

        private void ClampOffset()
        {
            int max = MaxOffset;
            if (_offset > max) _offset = max;
            if (_offset < 0) _offset = 0;
        }

        public void ScrollBy(int delta)
        {
            if (MaxOffset <= 0) return;
            int before = _offset;
            _offset += delta;
            ClampOffset();
            if (_offset == before) return;
            Invalidate();
            EventHandler h = ScrollChanged;
            if (h != null) h(this, EventArgs.Empty);
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            ClampOffset();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            if (_contentHeight <= ClientSize.Height) return;
            Graphics g = e.Graphics;
            g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            int trackTop = Theme.S(this, 4);
            int trackH = Height - Theme.S(this, 8);
            if (trackH <= 8) return;
            int thumbH = (int)Math.Round((double)trackH * Height / _contentHeight);
            if (thumbH < Theme.S(this, 28)) thumbH = Theme.S(this, 28);
            int max = MaxOffset;
            int thumbY = trackTop;
            if (max > 0) thumbY = trackTop + (int)Math.Round((double)(trackH - thumbH) * _offset / max);
            int barW = Theme.S(this, 4);
            Draw.FillRound(g, Color.FromArgb(120, Theme.Sub),
                new RectangleF(Width - barW - Theme.S(this, 2), thumbY, barW, thumbH), barW / 2f);
        }
    }

    /// <summary>双缓冲面板。</summary>
    internal class BufferedPanel : Panel
    {
        public BufferedPanel()
        {
            SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint, true);
            DoubleBuffered = true;
            BackColor = Theme.Bg;
        }
    }

    /// <summary>深色右键菜单渲染器。</summary>
    internal sealed class DarkColorTable : ProfessionalColorTable
    {
        public override Color ToolStripDropDownBackground { get { return Theme.Card; } }
        public override Color ImageMarginGradientBegin { get { return Theme.Card; } }
        public override Color ImageMarginGradientMiddle { get { return Theme.Card; } }
        public override Color ImageMarginGradientEnd { get { return Theme.Card; } }
        public override Color MenuBorder { get { return Theme.Border; } }
        public override Color MenuItemBorder { get { return Theme.AccentSoft; } }
        public override Color MenuItemSelected { get { return Theme.AccentSoft; } }
        public override Color MenuItemSelectedGradientBegin { get { return Theme.AccentSoft; } }
        public override Color MenuItemSelectedGradientEnd { get { return Theme.AccentSoft; } }
        public override Color MenuItemPressedGradientBegin { get { return Theme.AccentSoft; } }
        public override Color MenuItemPressedGradientEnd { get { return Theme.AccentSoft; } }
        public override Color SeparatorDark { get { return Theme.Divider; } }
        public override Color SeparatorLight { get { return Theme.Divider; } }
        public override Color CheckBackground { get { return Theme.AccentSoft; } }
        public override Color CheckSelectedBackground { get { return Theme.AccentSoft; } }
    }

    internal sealed class DarkMenuRenderer : ToolStripProfessionalRenderer
    {
        public DarkMenuRenderer() : base(new DarkColorTable())
        {
            RoundedEdges = false;
        }

        protected override void OnRenderToolStripBorder(ToolStripRenderEventArgs e)
        {
            Rectangle r = new Rectangle(0, 0, e.ToolStrip.Width, e.ToolStrip.Height);
            using (Pen pen = new Pen(Theme.Border, 1f))
            {
                e.Graphics.DrawRectangle(pen, 0, 0, r.Width - 1, r.Height - 1);
            }
        }

        protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e)
        {
            e.TextColor = e.Item.Enabled ? (e.Item.Selected ? Theme.Text : Theme.Text) : Theme.Sub;
            base.OnRenderItemText(e);
        }

        protected override void OnRenderItemCheck(ToolStripItemImageRenderEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            Rectangle r = e.ImageRectangle;
            if (r.Width <= 0) r = new Rectangle(8, 2, 16, 16);
            using (Pen pen = new Pen(Theme.Accent, 2f))
            {
                pen.StartCap = LineCap.Round;
                pen.EndCap = LineCap.Round;
                g.DrawLine(pen, r.X + 3, r.Y + r.Height / 2, r.X + r.Width / 2 - 1, r.Y + r.Height - 4);
                g.DrawLine(pen, r.X + r.Width / 2 - 1, r.Y + r.Height - 4, r.Right - 2, r.Y + 3);
            }
        }

        protected override void OnRenderSeparator(ToolStripSeparatorRenderEventArgs e)
        {
            Rectangle r = e.Item.ContentRectangle;
            using (Pen pen = new Pen(Theme.Divider, 1f))
            {
                e.Graphics.DrawLine(pen, r.Left + 6, r.Top + r.Height / 2, r.Right - 6, r.Top + r.Height / 2);
            }
        }
    }
}