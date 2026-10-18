using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;
using Pb.Core;

namespace Pb.App
{
    /// <summary>主面板（--show）：大卡片设备列表 + 数据源状态 + 操作按钮。</summary>
    internal sealed class MainForm : ChromeForm
    {
        private const int LogicalWidth = 620;
        private const int LogicalHeight = 660;
        private const int SummaryY = 58;
        private const int ListY = 84;
        private const int BottomBarH = 62;

        private readonly AppConfig _config;
        private readonly ScrollPanel _list;
        private readonly StatusListView _statusView;
        private readonly MessagePanel _empty;
        private readonly DarkButton _btnRefresh;
        private readonly DarkButton _btnSettings;
        private readonly DarkButton _btnCopy;
        private readonly DarkButton _btnExit;
        private readonly List<DeviceCard> _cards = new List<DeviceCard>();
        private readonly HashSet<string> _expanded = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private readonly Timer _toastTimer;
        private readonly Timer _refreshTimer;

        private List<Peripheral> _devices = new List<Peripheral>();
        private List<ProviderStatus> _statuses = new List<ProviderStatus>();
        private DateTime _at;
        private string _toast = "";
        private bool _refreshing;
        private bool _exiting;
        private int _statusTitleY;
        private int _bottomTop;
        private readonly ContextMenuStrip _cardMenu;
        private DeviceCard _menuCard;

        public event EventHandler RefreshRequested;
        public event EventHandler SettingsRequested;
        public event EventHandler ExitRequested;
        public delegate void RenameRequestDelegate(Peripheral device);
        public event RenameRequestDelegate RenameRequested;

        /// <summary>设备稳定键：优先 Id，退化为 MergeKey。</summary>
        internal static string KeyOf(Peripheral d)
        {
            if (d == null) return "";
            return string.IsNullOrEmpty(d.Id) ? d.MergeKey : d.Id;
        }

        public MainForm(AppConfig config) : base("2.4G 外设电量")
        {
            _config = config;
            ShowInTaskbar = true;
            StartPosition = FormStartPosition.CenterScreen;
            ShowMinimizeButton = true;
            Icon = IconRenderer.AppIcon;
            KeyPreview = true;

            _list = new ScrollPanel();
            _list.BackColor = Theme.Bg;
            _list.ScrollChanged += delegate { PositionCards(); };
            Controls.Add(_list);

            _statusView = new StatusListView();
            Controls.Add(_statusView);

            _empty = new MessagePanel();
            _empty.Headline = "未发现支持的外设";
            _empty.Lines = new string[]
            {
                "接收器未插好或未被识别，换个 USB 口再试",
                "该品牌/型号暂未支持，只能显示未知电量",
                "部分设备需要以管理员身份运行才能读取"
            };
            _empty.Visible = false;
            Controls.Add(_empty);

            _btnRefresh = MakeButton("立即刷新", 96);
            _btnRefresh.Primary = false;
            _btnRefresh.Click += delegate { BeginRefresh(); };
            Controls.Add(_btnRefresh);

            _btnSettings = MakeButton("设置", 64);
            _btnSettings.Click += delegate
            {
                EventHandler h = SettingsRequested;
                if (h != null) h(this, EventArgs.Empty);
            };
            Controls.Add(_btnSettings);

            _btnCopy = MakeButton("复制诊断信息", 116);
            _btnCopy.Click += delegate { CopyDiagnostics(); };
            Controls.Add(_btnCopy);

            _btnExit = MakeButton("退出", 64);
            _btnExit.Danger = true;
            _btnExit.Click += delegate
            {
                EventHandler h = ExitRequested;
                if (h != null) h(this, EventArgs.Empty);
            };
            Controls.Add(_btnExit);

            MinButton.Click += delegate { Hide(); };

            _cardMenu = new ContextMenuStrip();
            _cardMenu.Renderer = new DarkMenuRenderer();
            _cardMenu.Font = Theme.FontBody;
            _cardMenu.BackColor = Theme.Card;
            _cardMenu.ForeColor = Theme.Text;
            _cardMenu.ShowImageMargin = false;
            ToolStripMenuItem miRename = new ToolStripMenuItem("重命名…");
            miRename.Click += delegate
            {
                DeviceCard c = _menuCard;
                if (c == null || c.Device == null) return;
                RenameRequestDelegate h = RenameRequested;
                if (h != null) h(c.Device);
            };
            _cardMenu.Items.Add(miRename);
            _cardMenu.Opening += delegate
            {
                Point pt = _list.PointToClient(Cursor.Position);
                _menuCard = _list.GetChildAtPoint(pt) as DeviceCard;
                miRename.Enabled = _menuCard != null && _menuCard.Device != null
                    && !string.IsNullOrEmpty(_menuCard.Device.Id);
            };

            _toastTimer = new Timer();
            _toastTimer.Interval = 2600;
            _toastTimer.Tick += delegate
            {
                _toastTimer.Stop();
                _toast = "";
                Invalidate();
            };

            DpiChanged += delegate
            {
                if (_config.VerboseLog) Log.Info("DPI 变化: " + Theme.DpiDebug(this));
                Relayout();
            };

            _refreshTimer = new Timer();
            _refreshTimer.Interval = 9000;
            _refreshTimer.Tick += delegate
            {
                _refreshTimer.Stop();
                _refreshing = false;
                Invalidate();
            };
        }

        private DarkButton MakeButton(string text, int logicalWidth)
        {
            DarkButton b = new DarkButton();
            b.Text = text;
            b.Size = new Size(logicalWidth, 32);
            return b;
        }

        protected override void OnLoad(EventArgs e)
        {
            base.OnLoad(e);
            if (_config != null && _config.VerboseLog)
            {
                Log.Info("主面板布局: " + Theme.DpiDebug(this) + " client=" + ClientSize);
            }
            Relayout();
            if (_config != null && _config.VerboseLog)
            {
                Log.Info("主面板尺寸: client=" + ClientSize + " 列表=" + _list.Bounds + " 状态=" + _statusView.Bounds + " 状态标题Y=" + _statusTitleY);
            }
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            if (_list != null) Relayout();
        }

        public void SetData(IList<Peripheral> devices, IList<ProviderStatus> statuses, DateTime at)
        {
            _devices = devices == null ? new List<Peripheral>() : new List<Peripheral>(devices);
            _statuses = statuses == null ? new List<ProviderStatus>() : new List<ProviderStatus>(statuses);
            _at = at;
            _refreshing = false;
            if (_refreshTimer != null) _refreshTimer.Stop();
            if (!IsHandleCreated) return;
            SyncCards();
            _statusView.Items = _statuses;
            Relayout();
            Invalidate();
        }

        private void SyncCards()
        {
            Dictionary<string, DeviceCard> map = new Dictionary<string, DeviceCard>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < _cards.Count; i++)
            {
                DeviceCard c = _cards[i];
                if (c.Device == null) continue;
                map[KeyOf(c.Device)] = c;
            }
            List<DeviceCard> ordered = new List<DeviceCard>();
            for (int i = 0; i < _devices.Count; i++)
            {
                Peripheral d = _devices[i];
                string key = KeyOf(d);
                DeviceCard c;
                if (!map.TryGetValue(key, out c))
                {
                    c = new DeviceCard(false);
                    c.Click += OnCardClick;
                    c.RenameRequested += OnCardRename;
                    c.ContextMenuStrip = _cardMenu;
                    _list.Controls.Add(c);
                }
                else
                {
                    map.Remove(key);
                }
                c.Device = d;
                c.Expanded = _expanded.Contains(key);
                c.ShowChevron = true;
                ordered.Add(c);
            }
            foreach (DeviceCard leftover in map.Values)
            {
                _list.Controls.Remove(leftover);
                leftover.Dispose();
            }
            _cards.Clear();
            _cards.AddRange(ordered);

            bool empty = _devices.Count == 0;
            _empty.Visible = empty;
            _list.Visible = !empty;
        }

        private void OnCardClick(object sender, EventArgs e)
        {
            DeviceCard c = sender as DeviceCard;
            if (c != null && c.Device != null && _config.VerboseLog)
            {
                Log.Info("卡片点击: " + c.Device.Name + " 当前展开=" + c.Expanded);
            }
            if (c == null || c.Device == null) return;
            string key = KeyOf(c.Device);
            if (_expanded.Contains(key)) _expanded.Remove(key);
            else _expanded.Add(key);
            c.Expanded = _expanded.Contains(key);
            // 展开后卡片变高，需要重新计算列表可视高度，否则内容会被面板裁掉
            Relayout();
        }

        private void OnCardRename(object sender, EventArgs e)
        {
            DeviceCard c = sender as DeviceCard;
            if (c == null || c.Device == null) return;
            RenameRequestDelegate h = RenameRequested;
            if (h != null) h(c.Device);
        }

        private void Relayout()
        {
            if (!IsHandleCreated) return;
            float s = Theme.ScaleFactor(this);
            int w = (int)Math.Round(LogicalWidth * s);
            int h = (int)Math.Round(LogicalHeight * s);
            ClientSize = new Size(w, h);
            LayoutChrome();

            int pad = (int)Math.Round(12 * s);
            int listX = pad;
            int listW = w - pad * 2;
            int statusH = StatusListView.PreferredHeight(s, _statuses.Count);
            int maxStatus = (int)Math.Round(220 * s);
            if (statusH > maxStatus) statusH = maxStatus;
            int bottomTop = h - (int)Math.Round(BottomBarH * s);
            _bottomTop = bottomTop;
            int titleH = (int)Math.Round(20 * s);
            int statusBlockH = titleH + (int)Math.Round(4 * s) + statusH;
            int listY = (int)Math.Round(ListY * s);
            int availList = bottomTop - (int)Math.Round(8 * s) - statusBlockH - (int)Math.Round(12 * s) - listY;

            int cardGap = (int)Math.Round(10 * s);
            int contentH = 0;
            for (int i = 0; i < _cards.Count; i++)
            {
                contentH += (int)Math.Round(DeviceCard.HeightFor(_cards[i].Device, _cards[i].Expanded) * s) + cardGap;
            }
            if (contentH == 0) contentH = (int)Math.Round(150 * s);

            int listH = contentH + (int)Math.Round(8 * s);
            int minList = (int)Math.Round(200 * s);
            if (listH < minList) listH = minList;
            if (listH > availList) listH = availList;
            int floorList = (int)Math.Round(120 * s);
            if (listH < floorList) listH = floorList;

            _statusTitleY = listY + listH + (int)Math.Round(12 * s);
            int maxTitleY = bottomTop - (int)Math.Round(8 * s) - statusBlockH;
            if (_statusTitleY > maxTitleY) _statusTitleY = maxTitleY;

            _list.Bounds = new Rectangle(listX, listY, listW, listH);
            _statusView.Bounds = new Rectangle(listX, _statusTitleY + titleH + (int)Math.Round(4 * s), listW, statusH);
            _empty.Bounds = _list.Bounds;

            int btnY = h - (int)Math.Round(50 * s);
            int btnH = (int)Math.Round(32 * s);
            int gap = (int)Math.Round(8 * s);
            int x = pad;
            _btnRefresh.Bounds = new Rectangle(x, btnY, (int)Math.Round(96 * s), btnH);
            x += (int)Math.Round(96 * s) + gap;
            _btnSettings.Bounds = new Rectangle(x, btnY, (int)Math.Round(64 * s), btnH);
            x += (int)Math.Round(64 * s) + gap;
            _btnCopy.Bounds = new Rectangle(x, btnY, (int)Math.Round(116 * s), btnH);
            x += (int)Math.Round(116 * s) + gap;
            _btnExit.Bounds = new Rectangle(x, btnY, (int)Math.Round(64 * s), btnH);

            RelayoutCards();
            Invalidate();
        }

        private void RelayoutCards()
        {
            if (_list == null || !IsHandleCreated) return;
            float s = Theme.ScaleFactor(this);
            int gap = (int)Math.Round(10 * s);
            int total = 0;
            for (int i = 0; i < _cards.Count; i++)
            {
                total += (int)Math.Round(DeviceCard.HeightFor(_cards[i].Device, _cards[i].Expanded) * s) + gap;
            }
            _list.ContentHeight = total + (int)Math.Round(8 * s);
            PositionCards();
        }

        private void PositionCards()
        {
            if (_list == null) return;
            float s = Theme.ScaleFactor(this);
            int gap = (int)Math.Round(10 * s);
            int cardW = _list.ClientSize.Width - (int)Math.Round(12 * s);
            int y = 0;
            for (int i = 0; i < _cards.Count; i++)
            {
                DeviceCard c = _cards[i];
                int ch = (int)Math.Round(DeviceCard.HeightFor(c.Device, c.Expanded) * s);
                c.Bounds = new Rectangle(0, y - _list.ScrollOffset, cardW, ch);
                y += ch + gap;
            }
        }

        protected override void OnMouseWheel(MouseEventArgs e)
        {
            Point p = PointToClient(Cursor.Position);
            if (_list != null && _list.Bounds.Contains(p))
            {
                _list.ScrollBy(-e.Delta);
                return;
            }
            base.OnMouseWheel(e);
        }

        public void BeginRefresh()
        {
            _refreshing = true;
            _toast = "";
            Invalidate();
            EventHandler h = RefreshRequested;
            if (h != null) h(this, EventArgs.Empty);
            _refreshTimer.Stop();
            _refreshTimer.Start();
        }

        private void CopyDiagnostics()
        {
            try
            {
                Clipboard.SetText(Diag.Build(_config, _devices, _statuses, _at));
                ShowToast("诊断信息已复制到剪贴板");
            }
            catch (Exception ex)
            {
                Log.Warn("复制诊断信息失败: " + ex.Message);
                ShowToast("复制失败：" + ex.Message);
            }
        }

        private void ShowToast(string text)
        {
            _toast = text;
            _toastTimer.Stop();
            _toastTimer.Start();
            Invalidate();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            float s = Theme.ScaleFactor(this);
            int pad = (int)Math.Round(20 * s);
            int w = ClientSize.Width;

            string summary;
            if (_refreshing) summary = "正在刷新…";
            else if (_devices.Count == 0) summary = "未发现支持的外设" + ( _at == default(DateTime) ? "" : " · 最近更新 " + Fmt.Time(_at) );
            else summary = _devices.Count + " 台设备 · 最近更新 " + Fmt.Time(_at);
            Draw.TLeft(g, summary, Theme.FontSub, Theme.Sub,
                new Rectangle(pad, (int)Math.Round(SummaryY * s), w - pad * 2, (int)Math.Round(20 * s)), true);

            int errCount = 0;
            for (int i = 0; i < _statuses.Count; i++)
            {
                if (_statuses[i].Enabled && !_statuses[i].Ok) errCount++;
            }
            string statusTitle = "数据源";
            Draw.TLeft(g, statusTitle, Theme.FontBodyBold, Theme.Text,
                new Rectangle(pad, _statusTitleY, (int)Math.Round(120 * s), (int)Math.Round(20 * s)), false);
            if (errCount > 0)
            {
                Draw.TLeft(g, errCount + " 项不可用", Theme.FontSmall, Theme.Red,
                    new Rectangle(pad + (int)Math.Round(56 * s), _statusTitleY + (int)Math.Round(2 * s),
                        (int)Math.Round(160 * s), (int)Math.Round(18 * s)), false);
            }
            else if (_statuses.Count > 0)
            {
                Draw.TLeft(g, _statuses.Count + " 个数据源正常", Theme.FontSmall, Theme.Green,
                    new Rectangle(pad + (int)Math.Round(56 * s), _statusTitleY + (int)Math.Round(2 * s),
                        (int)Math.Round(160 * s), (int)Math.Round(18 * s)), false);
            }

            Draw.Line(g, Theme.Divider, 1f, pad, _bottomTop, w - pad, _bottomTop);
            Draw.TRight(g, "v" + AppInfo.Version, Theme.FontSmall, Theme.Sub,
                new Rectangle(w - (int)Math.Round(120 * s), _bottomTop + (int)Math.Round(17 * s),
                    (int)Math.Round(100 * s), (int)Math.Round(28 * s)));

            if (_toast.Length > 0)
            {
                int tw = w - pad * 2;
                Draw.TLeft(g, _toast, Theme.FontSub, Theme.Green,
                    new Rectangle(pad, _bottomTop - (int)Math.Round(22 * s), tw, (int)Math.Round(18 * s)), true);
            }
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            if (!_exiting && e.CloseReason == CloseReason.UserClosing)
            {
                e.Cancel = true;
                Hide();
                return;
            }
            base.OnFormClosing(e);
        }

        public void CloseForExit()
        {
            _exiting = true;
            Close();
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

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                if (_toastTimer != null) { _toastTimer.Stop(); _toastTimer.Dispose(); }
                if (_refreshTimer != null) { _refreshTimer.Stop(); _refreshTimer.Dispose(); }
            }
            base.Dispose(disposing);
        }
    }
}
