using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;
using Pb.Core;

namespace Pb.App
{
    /// <summary>托盘左键悬浮面板：无边框深色圆角，右下角定位，失焦自动隐藏。</summary>
    internal sealed class FlyoutForm : Form
    {
        private const int HeaderH = 66;
        private const int FooterH = 52;
        private const int Gap = 8;
        private const int Pad = 12;
        private const int MaxHeight = 560;
        private const int EmptyH = 120;

        private readonly BufferedPanel _cards;
        private readonly BufferedPanel _footer;
        private readonly DarkButton _btnSettings;
        private readonly RoundButton _btnRefresh;
        private readonly MessagePanel _empty;
        private readonly List<DeviceCard> _cache = new List<DeviceCard>();

        private List<Peripheral> _devices = new List<Peripheral>();
        private List<ProviderStatus> _statuses = new List<ProviderStatus>();
        private DateTime _at;
        private string _noteText = "";
        private Color _noteColor = Theme.Sub;
        private bool _suppressHide;
        private bool _laidOut;

        public event EventHandler SettingsRequested;
        public event EventHandler RefreshRequested;

        public DateTime LastHiddenAt { get; private set; }

        public FlyoutForm()
        {
            Theme.InitForm(this, "2.4G 外设电量");
            ShowInTaskbar = false;
            TopMost = true;
            StartPosition = FormStartPosition.Manual;
            SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint | ControlStyles.ResizeRedraw, true);
            LastHiddenAt = DateTime.MinValue;

            _cards = new BufferedPanel();
            _cards.BackColor = Theme.Bg;
            Controls.Add(_cards);

            _empty = new MessagePanel();
            _empty.Headline = "未发现支持的外设";
            _empty.Lines = new string[]
            {
                "接收器未插好或未识别，试试换个 USB 口",
                "该品牌/型号暂未支持，只能显示未知电量",
                "部分设备需要以管理员身份运行才能读取"
            };
            _empty.Visible = false;
            Controls.Add(_empty);

            _footer = new BufferedPanel();
            _footer.BackColor = Theme.Bg;
            _footer.Paint += OnFooterPaint;
            Controls.Add(_footer);

            _btnSettings = new DarkButton();
            _btnSettings.Text = "设置";
            _btnSettings.Size = new Size(68, 30);
            _btnSettings.Click += delegate
            {
                _suppressHide = true;
                Hide();
                _suppressHide = false;
                EventHandler h = SettingsRequested;
                if (h != null) h(this, EventArgs.Empty);
            };
            _footer.Controls.Add(_btnSettings);

            DpiChanged += delegate { Relayout(); };

            _btnRefresh = new RoundButton();
            _btnRefresh.Glyph = RoundButton.GlyphKind.Refresh;
            _btnRefresh.GlyphColor = Theme.Accent;
            _btnRefresh.Size = new Size(30, 30);
            _btnRefresh.Click += delegate
            {
                EventHandler h = RefreshRequested;
                if (h != null) h(this, EventArgs.Empty);
                Invalidate();
            };
            Controls.Add(_btnRefresh);
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

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            UpdateRegion();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            float s = Theme.ScaleFactor(this);
            int pad = (int)Math.Round(Pad * s);
            int textX = pad + (int)Math.Round(4 * s);
            int right = Width - pad - (int)Math.Round(42 * s);
            Draw.TLeft(g, AppInfo.Name, Theme.FontTitle, Theme.Text,
                new Rectangle(textX, (int)Math.Round(9 * s), right - textX, (int)Math.Round(24 * s)), true);
            string sub = _at == default(DateTime) ? "正在读取…" : "最近更新 " + Fmt.Time(_at);
            Draw.TLeft(g, sub, Theme.FontSub, Theme.Sub,
                new Rectangle(textX, (int)Math.Round(34 * s), right - textX, (int)Math.Round(18 * s)), true);
            int divY = (int)Math.Round(60 * s);
            Draw.Line(g, Theme.Divider, 1f, pad, divY, Width - pad, divY);
            Draw.StrokeRound(g, Theme.Border, new RectangleF(0.5f, 0.5f, Width - 1f, Height - 1f), Theme.SF(this, 12f), 1f);
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

        public void SetData(IList<Peripheral> devices, IList<ProviderStatus> statuses, DateTime at)
        {
            _devices = devices == null ? new List<Peripheral>() : new List<Peripheral>(devices);
            _statuses = statuses == null ? new List<ProviderStatus>() : new List<ProviderStatus>(statuses);
            _at = at;
            if (!IsHandleCreated) return;
            BuildCards();
            Relayout();
            Invalidate();
        }

        private int MaxVisible
        {
            get
            {
                float s = Theme.ScaleFactor(this);
                int headerH = (int)Math.Round(HeaderH * s);
                int footerH = (int)Math.Round(FooterH * s);
                int gap = (int)Math.Round(Gap * s);
                int pitch = (int)Math.Round(DeviceCard.CompactPitch * s);
                int cap = (int)Math.Round(MaxHeight * s);
                int avail = cap - headerH - footerH - gap;
                int n = (avail + gap) / pitch;
                return n < 1 ? 1 : n;
            }
        }

        private void BuildCards()
        {
            int visible = Math.Min(_devices.Count, MaxVisible);
            while (_cache.Count > visible)
            {
                DeviceCard c = _cache[_cache.Count - 1];
                _cache.RemoveAt(_cache.Count - 1);
                _cards.Controls.Remove(c);
                c.Dispose();
            }
            while (_cache.Count < visible)
            {
                DeviceCard c = new DeviceCard(true);
                _cache.Add(c);
                _cards.Controls.Add(c);
            }
            for (int i = 0; i < visible; i++)
            {
                _cache[i].Device = _devices[i];
                _cache[i].Invalidate();
            }

            bool anyError = false;
            for (int i = 0; i < _statuses.Count; i++)
            {
                if (_statuses[i].Enabled && !_statuses[i].Ok) anyError = true;
            }
            if (anyError)
            {
                int err = 0;
                for (int i = 0; i < _statuses.Count; i++)
                {
                    if (_statuses[i].Enabled && !_statuses[i].Ok) err++;
                }
                _noteText = err + " 项数据源不可用";
                _noteColor = Theme.Yellow;
            }
            else if (_devices.Count > visible)
            {
                _noteText = "共 " + _devices.Count + " 台设备";
                _noteColor = Theme.Sub;
            }
            else
            {
                _noteText = "";
            }
            _footer.Invalidate();
        }

        public void Relayout()
        {
            float s = Theme.ScaleFactor(this);
            int w = (int)Math.Round(360 * s);
            int headerH = (int)Math.Round(HeaderH * s);
            int footerH = (int)Math.Round(FooterH * s);
            int gap = (int)Math.Round(Gap * s);
            int pad = (int)Math.Round(Pad * s);
            int pitch = (int)Math.Round(DeviceCard.CompactPitch * s);
            int cardH = (int)Math.Round(DeviceCard.CompactHeight * s);
            int visible = Math.Min(_devices.Count, MaxVisible);

            int cardsH;
            if (_devices.Count == 0) cardsH = (int)Math.Round(EmptyH * s);
            else cardsH = visible * pitch - gap;

            int h = headerH + cardsH + gap + footerH;

            ClientSize = new Size(w, h);
            _cards.Bounds = new Rectangle(pad, headerH, w - pad * 2, cardsH);
            _footer.Bounds = new Rectangle(0, h - footerH, w, footerH);
            _empty.Bounds = new Rectangle(pad, headerH, w - pad * 2, cardsH);

            bool empty = _devices.Count == 0;
            _empty.Visible = empty;
            _cards.Visible = !empty;

            for (int i = 0; i < _cache.Count; i++)
            {
                _cache[i].Bounds = new Rectangle(0, i * pitch, _cards.Width, cardH);
            }

            _btnRefresh.Bounds = new Rectangle(w - pad - (int)Math.Round(30 * s) - (int)Math.Round(4 * s),
                (int)Math.Round(14 * s), (int)Math.Round(30 * s), (int)Math.Round(30 * s));
            int btnH = (int)Math.Round(30 * s);
            int btnW = (int)Math.Round(68 * s);
            _btnSettings.Bounds = new Rectangle(w - pad - btnW, (footerH - btnH) / 2, btnW, btnH);
            _laidOut = true;
            if (Visible) PositionAtTray();
        }

        private void OnFooterPaint(object sender, PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            float s = Theme.ScaleFactor(this);
            int pad = (int)Math.Round(Pad * s);
            Draw.Line(g, Theme.Divider, 1f, pad, 0.5f, _footer.Width - pad, 0.5f);
            if (_noteText.Length == 0) return;
            int noteW = _btnSettings.Left - pad - (int)Math.Round(8 * s);
            Draw.TLeft(g, _noteText, Theme.FontSmall, _noteColor,
                new Rectangle(pad, 0, noteW, _footer.Height), true);
        }

        private void PositionAtTray()
        {
            Rectangle wa = Screen.PrimaryScreen.WorkingArea;
            int margin = Theme.S(this, 8);
            int x = wa.Right - Width - margin;
            int y = wa.Bottom - Height - margin;
            if (x < wa.Left) x = wa.Left;
            if (y < wa.Top) y = wa.Top;
            Location = new Point(x, y);
        }

        public void ShowAtTray()
        {
            if (!_laidOut) Relayout();
            PositionAtTray();
            if (!Visible)
            {
                Show();
                Ui.FadeIn(this, 150);
            }
            BringToFront();
            try { Activate(); } catch { }
        }

        protected override void OnDeactivate(EventArgs e)
        {
            base.OnDeactivate(e);
            if (_suppressHide) return;
            LastHiddenAt = DateTime.Now;
            Hide();
        }

        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            if (keyData == Keys.Escape)
            {
                Hide();
                return true;
            }
            return base.ProcessCmdKey(ref msg, keyData);
        }
    }
}
