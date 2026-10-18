using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;
using Pb.Core;

namespace Pb.App
{
    /// <summary>自绘设备卡片：悬浮面板（紧凑）与主面板（大卡片，可展开详情）。</summary>
    internal sealed class DeviceCard : Control
    {
        public const int CompactHeight = 66;
        public const int CompactPitch = 74;
        public const int LargeHeight = 92;
        public const int LargeExpandedHeight = 292;
        public const int GenericNoteHeight = 32;

        private readonly bool _compact;
        private bool _hover;
        private bool _hoverRename;
        private bool _suppressClick;

        /// <summary>卡片高度（96 DPI 逻辑像素）：通用设备展开时多一行说明。</summary>
        public static int HeightFor(Peripheral d, bool expanded)
        {
            if (!expanded) return LargeHeight;
            return LargeExpandedHeight + ((d != null && d.IsGeneric) ? GenericNoteHeight : 0);
        }

        /// <summary>点击“重命名”按钮。</summary>
        public event EventHandler RenameRequested;

        public DeviceCard(bool compact)
        {
            _compact = compact;
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint
                | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw
                | ControlStyles.SupportsTransparentBackColor, true);
            BackColor = Theme.Bg;
            if (!compact) Cursor = Cursors.Hand;
        }

        public Peripheral Device { get; set; }
        public bool Expanded { get; set; }
        public bool ShowChevron { get; set; }

        private int S(int v)
        {
            return (int)Math.Round(v * Theme.ScaleFactor(this));
        }

        private float SF(float v)
        {
            return v * Theme.ScaleFactor(this);
        }

        protected override void OnMouseEnter(EventArgs e) { _hover = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { _hover = false; _hoverRename = false; Invalidate(); base.OnMouseLeave(e); }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            bool hov = Expanded && !_compact && RenameButtonRect(Width, Height).Contains(e.Location);
            if (hov != _hoverRename) { _hoverRename = hov; Invalidate(); }
            base.OnMouseMove(e);
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (e.Button != MouseButtons.Left) return;
            if (Expanded && !_compact && RenameButtonRect(Width, Height).Contains(e.Location))
            {
                _suppressClick = true;
                EventHandler h = RenameRequested;
                if (h != null) h(this, EventArgs.Empty);
            }
        }

        protected override void OnClick(EventArgs e)
        {
            if (_suppressClick) { _suppressClick = false; return; }
            base.OnClick(e);
        }

        private Rectangle RenameButtonRect(int w, int h)
        {
            return new Rectangle(w - S(14) - S(104), h - S(44), S(104), S(30));
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            Peripheral d = Device;
            int w = Width;
            int h = Height;
            if (w <= 0 || h <= 0) return;

            Color bg = _hover && !_compact ? Theme.CardHover : Theme.Card;
            Draw.FillRound(g, bg, new RectangleF(0, 0, w, h), SF(10f));
            Draw.StrokeRound(g, _hover && !_compact ? Theme.Border : Theme.Divider,
                new RectangleF(0.5f, 0.5f, w - 1f, h - 1f), SF(10f), 1f);

            if (d == null)
            {
                Draw.TLeft(g, "（无数据）", Theme.FontBody, Theme.Sub, new Rectangle(S(16), 0, w - S(32), h), true);
                return;
            }

            if (_compact) PaintCompact(g, d, w, h);
            else PaintLarge(g, d, w, h);
        }

        private void PaintCompact(Graphics g, Peripheral d, int w, int h)
        {
            Color color = Theme.BatteryColor(d.Percent, d.State);
            int tile = S(36);
            int tileX = S(12);
            int tileY = (h - tile) / 2;
            Draw.FillRound(g, Theme.Tile, new RectangleF(tileX, tileY, tile, tile), SF(10f));
            Draw.Kind(g, d.Kind, new RectangleF(tileX + tile * 0.22f, tileY + tile * 0.22f, tile * 0.56f, tile * 0.56f),
                color, Math.Max(1.2f, SF(1.7f)));

            int rightW = S(112);
            int rightX = w - rightW - S(14);
            int textX = tileX + tile + S(10);
            int textW = rightX - textX - S(8);

            Draw.TLeft(g, d.Name, Theme.FontBodyBold, Theme.Text, new Rectangle(textX, S(11), textW, S(20)), true);
            Draw.TLeft(g, Fmt.SubLine(d), Theme.FontSub, Theme.Sub, new Rectangle(textX, S(33), textW, S(18)), true);

            Draw.TRight(g, Fmt.Percent(d.Percent), Theme.FontNum, color, new Rectangle(rightX, S(7), rightW, S(28)));

            RectangleF bar = new RectangleF(rightX, S(39), rightW, SF(6f));
            Draw.FillRound(g, Theme.Divider, bar, SF(3f));
            if (d.Percent.HasValue)
            {
                float ratio = Math.Max(0f, Math.Min(100f, d.Percent.Value)) / 100f;
                float bw = bar.Width * ratio;
                if (bw > SF(3f)) Draw.FillRound(g, color, new RectangleF(bar.X, bar.Y, bw, bar.Height), SF(3f));
            }

            string st = d.Percent.HasValue ? Theme.StateText(d.State) : "未知";
            if (d.State == BatteryState.Unknown && d.Percent.HasValue) st = "";
            if (st.Length > 0)
            {
                Size sz = TextRenderer.MeasureText(st, Theme.FontSmall, new Size(0, 0), TextFormatFlags.NoPadding);
                int tx = rightX + rightW - sz.Width;
                int ty = S(48);
                if (d.State == BatteryState.Charging)
                {
                    Draw.Bolt(g, new RectangleF(tx - SF(11f), ty + SF(1.5f), SF(8f), SF(12f)), color);
                }
                Draw.TLeft(g, st, Theme.FontSmall, color, new Rectangle(tx, ty, sz.Width + S(2), S(14)), false);
            }
        }

        private void PaintLarge(Graphics g, Peripheral d, int w, int h)
        {
            Color color = Theme.BatteryColor(d.Percent, d.State);
            int tile = S(48);
            int tileX = S(14);
            int tileY = S(16);
            Draw.FillRound(g, Theme.Tile, new RectangleF(tileX, tileY, tile, tile), SF(12f));
            Draw.Kind(g, d.Kind, new RectangleF(tileX + tile * 0.22f, tileY + tile * 0.22f, tile * 0.56f, tile * 0.56f),
                color, Math.Max(1.4f, SF(1.9f)));

            int rightW = S(168);
            int rightX = w - rightW - S(40);
            int textX = tileX + tile + S(14);
            int textW = rightX - textX - S(10);

            Draw.TLeft(g, d.Name, Theme.FontH1, Theme.Text, new Rectangle(textX, S(13), textW, S(22)), true);
            Draw.TLeft(g, Fmt.SubLine(d), Theme.FontSub, Theme.Sub, new Rectangle(textX, S(37), textW, S(20)), true);

            Draw.TRight(g, Fmt.Percent(d.Percent), Theme.FontNumBig, color, new Rectangle(rightX, S(10), rightW, S(32)));

            RectangleF bar = new RectangleF(rightX, S(48), rightW, SF(8f));
            Draw.FillRound(g, Theme.Divider, bar, SF(4f));
            if (d.Percent.HasValue)
            {
                float ratio = Math.Max(0f, Math.Min(100f, d.Percent.Value)) / 100f;
                float bw = bar.Width * ratio;
                if (bw > SF(4f)) Draw.FillRound(g, color, new RectangleF(bar.X, bar.Y, bw, bar.Height), SF(4f));
            }

            string st = d.Percent.HasValue ? Theme.StateText(d.State) : "未知";
            if (d.State == BatteryState.Unknown && d.Percent.HasValue) st = "";
            if (st.Length > 0)
            {
                Size sz = TextRenderer.MeasureText(st, Theme.FontSmall, new Size(0, 0), TextFormatFlags.NoPadding);
                int tx = rightX + rightW - sz.Width;
                int ty = S(61);
                if (d.State == BatteryState.Charging)
                {
                    Draw.Bolt(g, new RectangleF(tx - SF(11f), ty + SF(1.5f), SF(8f), SF(12f)), color);
                }
                Draw.TLeft(g, st, Theme.FontSmall, color, new Rectangle(tx, ty, sz.Width + S(2), S(14)), false);
            }

            if (ShowChevron)
            {
                Draw.Chevron(g, new RectangleF(w - S(26), S(38), SF(10f), SF(6f)), Theme.Sub, !Expanded, Math.Max(1.4f, SF(1.6f)));
            }

            if (Expanded) PaintDetails(g, d, w, h);
        }

        private void PaintDetails(Graphics g, Peripheral d, int w, int h)
        {
            int top = S(84);
            Draw.Line(g, Theme.Divider, 1f, S(14), top, w - S(14), top);

            int contentW = w - S(28);
            int colW = contentW / 2;
            int col1 = S(14);
            int col2 = S(14) + colW;
            int labelW = S(62);
            int valueW = colW - labelW - S(10);
            int rowH = S(22);
            int y = S(94);

            Detail(g, col1, y, labelW, valueW, "品牌", string.IsNullOrEmpty(d.Brand) ? "—" : d.Brand);
            Detail(g, col2, y, labelW, valueW, "连接方式", string.IsNullOrEmpty(d.Connection) ? "—" : d.Connection);
            y += rowH;
            Detail(g, col1, y, labelW, valueW, "VID:PID", Fmt.IdPair(d.Vid, d.Pid));
            Detail(g, col2, y, labelW, valueW, "数据源", string.IsNullOrEmpty(d.Provider) ? "—" : d.Provider);
            y += rowH;
            Detail(g, col1, y, labelW, valueW, "电量", Fmt.Percent(d.Percent));
            Detail(g, col2, y, labelW, valueW, "电压", Fmt.Mv(d.MilliVolts));
            y += rowH;
            Detail(g, col1, y, labelW, valueW, "信号", Fmt.Signal(d.Signal));
            Detail(g, col2, y, labelW, valueW, "最后更新", Fmt.Time(d.Updated));
            y += rowH + S(4);

            Draw.TLeft(g, "HID 路径", Theme.FontSmall, Theme.Sub, new Rectangle(col1, y, labelW + S(30), S(18)), false);
            string path = string.IsNullOrEmpty(d.DevicePath) ? "（无）" : d.DevicePath;
            Draw.TWrap(g, path, Theme.FontSub, Theme.Text, new Rectangle(col1, y + S(16), contentW, S(32)));

            if (d.IsGeneric)
            {
                Draw.TLeft(g, "该设备未暴露电量协议，仅识别到设备；可重命名便于区分。", Theme.FontSmall, Theme.Sub,
                    new Rectangle(col1, y + S(52), contentW, S(30)), true);
            }

            Rectangle btn = RenameButtonRect(w, h);
            Color btnBg = _hoverRename ? Theme.CardHover : Theme.Field;
            Draw.FillRound(g, btnBg, new RectangleF(btn.X, btn.Y, btn.Width, btn.Height), SF(8f));
            Draw.StrokeRound(g, _hoverRename ? Theme.Accent : Theme.Border,
                new RectangleF(btn.X + 0.5f, btn.Y + 0.5f, btn.Width - 1f, btn.Height - 1f), SF(8f), 1f);
            Draw.TCenter(g, "重命名", Theme.FontBody, _hoverRename ? Theme.Accent : Theme.Text, btn);
        }

        private void Detail(Graphics g, int x, int y, int labelW, int valueW, string label, string value)
        {
            Draw.TLeft(g, label, Theme.FontSmall, Theme.Sub, new Rectangle(x, y, labelW, S(20)), false);
            Draw.TLeft(g, value, Theme.FontBody, Theme.Text, new Rectangle(x + labelW, y, valueW, S(20)), true);
        }
    }
}
