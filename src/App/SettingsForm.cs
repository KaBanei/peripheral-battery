using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;
using Pb.Core;
using Pb.Core.Platform;

namespace Pb.App
{
    /// <summary>设置窗口。</summary>
    internal sealed class SettingsForm : ChromeForm
    {
        private const int LogicalWidth = 560;
        private const int LogicalHeight = 764;
        private const int Pad = 16;
        private const int CardPad = 14;
        private const int HeaderH = 26;
        private const int RowH = 44;
        private const int DsRowH = 38;
        private const int GapY = 10;
        private const int BottomH = 64;

        private class Section
        {
            public Rectangle Rect;
            public string Title;
            public string Note;
            public string NoteSub;
        }

        private readonly AppConfig _config;
        private readonly bool _forcedDemo;
        private readonly List<Section> _sections = new List<Section>();

        private readonly Stepper _stepRefresh;
        private readonly Stepper _stepLow;
        private readonly Stepper _stepOpacity;
        private readonly SegmentControl _segTray;
        private readonly DarkCheckBox _chkBalloon;
        private readonly DarkCheckBox _chkAutoStart;
        private readonly DarkCheckBox _chkLogi;
        private readonly DarkCheckBox _chkHid;
        private readonly DarkCheckBox _chkBt;
        private readonly DarkCheckBox _chkXInput;
        private readonly DarkCheckBox _chkDemo;
        private readonly DarkCheckBox _chkVerbose;
        private readonly DarkButton _btnSave;
        private readonly DarkButton _btnCancel;

        private int _dsNoteY;
        private int _dsNoteMaxY;

        public bool RestartRequired { get; private set; }

        public SettingsForm(AppConfig config, bool forcedDemo) : base("设置")
        {
            _config = config;
            _forcedDemo = forcedDemo;
            StartPosition = FormStartPosition.CenterScreen;
            KeyPreview = true;

            _stepRefresh = new Stepper();
            _stepRefresh.Minimum = 5;
            _stepRefresh.Maximum = 300;
            _stepRefresh.Step = 5;
            _stepRefresh.Suffix = " 秒";
            _stepRefresh.Value = config.RefreshSeconds;
            Controls.Add(_stepRefresh);

            _segTray = new SegmentControl();
            _segTray.Items = new string[] { "最低", "平均", "最高" };
            Controls.Add(_segTray);

            _stepOpacity = new Stepper();
            _stepOpacity.Minimum = 20;
            _stepOpacity.Maximum = 100;
            _stepOpacity.Step = 5;
            _stepOpacity.Suffix = " %";
            _stepOpacity.Value = config.EdgeBarOpacity;
            Controls.Add(_stepOpacity);

            _stepLow = new Stepper();
            _stepLow.Minimum = 1;
            _stepLow.Maximum = 90;
            _stepLow.Step = 1;
            _stepLow.Suffix = "%";
            _stepLow.Value = config.LowBatteryThreshold;
            Controls.Add(_stepLow);

            _chkBalloon = MakeCheck("低电量时弹出气泡通知");
            _chkBalloon.Checked = config.NotifyLowBattery;
            Controls.Add(_chkBalloon);

            _chkAutoStart = MakeCheck("随 Windows 启动（静默驻留托盘）");
            _chkAutoStart.Checked = SafeIsAutoStart();
            Controls.Add(_chkAutoStart);

            _chkLogi = MakeCheck("罗技 HID++");
            _chkHid = MakeCheck("标准 HID 电量");
            _chkBt = MakeCheck("蓝牙设备");
            _chkXInput = MakeCheck("Xbox 手柄");
            _chkDemo = MakeCheck("演示模式（模拟设备）");
            Controls.Add(_chkLogi);
            Controls.Add(_chkHid);
            Controls.Add(_chkBt);
            Controls.Add(_chkXInput);
            Controls.Add(_chkDemo);

            _chkVerbose = MakeCheck("记录详细日志");
            _chkVerbose.Checked = config.VerboseLog;
            Controls.Add(_chkVerbose);

            _chkLogi.Checked = config.EnableLogitech;
            _chkHid.Checked = config.EnableStandardHid;
            _chkBt.Checked = config.EnableBluetooth;
            _chkXInput.Checked = config.EnableXInput;
            _chkDemo.Checked = config.EnableDemo || forcedDemo;
            if (forcedDemo)
            {
                _chkDemo.Enabled = false;
                _chkDemo.Text = "演示模式（--demo 强制开启）";
            }

            _btnSave = new DarkButton();
            _btnSave.Text = "保存";
            _btnSave.Primary = true;
            _btnSave.Click += delegate { Save(); };
            Controls.Add(_btnSave);

            DpiChanged += delegate { Relayout(); };

            _btnCancel = new DarkButton();
            _btnCancel.Text = "取消";
            _btnCancel.Click += delegate
            {
                DialogResult = DialogResult.Cancel;
                Close();
            };
            Controls.Add(_btnCancel);
        }

        private DarkCheckBox MakeCheck(string text)
        {
            DarkCheckBox c = new DarkCheckBox();
            c.Text = text;
            c.BackColor = Theme.Card;
            c.Size = new Size(200, 24);
            return c;
        }

        private static bool SafeIsAutoStart()
        {
            try { return AutoStart.IsEnabled; } catch { return false; }
        }

        protected override void OnLoad(EventArgs e)
        {
            base.OnLoad(e);
            _segTray.SelectedIndex = TrayModeIndex(_config.TrayDisplay);
            Relayout();
        }

        private int S(int v)
        {
            return (int)Math.Round(v * Theme.ScaleFactor(this));
        }

        private void Relayout()
        {
            if (!IsHandleCreated) return;
            ClientSize = new Size(S(LogicalWidth), S(LogicalHeight));
            LayoutChrome();

            _sections.Clear();
            int x = S(Pad);
            int cw = ClientSize.Width - S(Pad) * 2;
            int y = S(62);

            // 常规
            Section s1 = AddSection(x, y, cw, "常规", 3, RowH, null, null);
            y = s1.Rect.Bottom + S(GapY);
            int row = s1.Rect.Top + S(HeaderH);
            _stepRefresh.Bounds = new Rectangle(x + cw - S(CardPad) - S(118), row + S(7), S(118), S(30));
            row += S(RowH);
            _segTray.Bounds = new Rectangle(x + cw - S(CardPad) - S(210), row + S(7), S(210), S(30));
            row += S(RowH);
            _stepOpacity.Bounds = new Rectangle(x + cw - S(CardPad) - S(118), row + S(7), S(118), S(30));

            // 提醒
            Section s2 = AddSection(x, y, cw, "提醒", 2, RowH, null, null);
            y = s2.Rect.Bottom + S(GapY);
            row = s2.Rect.Top + S(HeaderH);
            _stepLow.Bounds = new Rectangle(x + cw - S(CardPad) - S(118), row + S(7), S(118), S(30));
            row += S(RowH);
            _chkBalloon.Bounds = new Rectangle(x + S(CardPad), row + S(10), cw - S(CardPad) * 2, S(24));

            // 启动
            Section s3 = AddSection(x, y, cw, "启动", 1, RowH, null, null);
            y = s3.Rect.Bottom + S(GapY);
            row = s3.Rect.Top + S(HeaderH);
            _chkAutoStart.Bounds = new Rectangle(x + S(CardPad), row + S(10), cw - S(CardPad) * 2, S(24));

            // 数据源
            Section s4 = AddSection(x, y, cw, "数据源", 0, DsRowH, "重启后生效", "勾选或取消数据源后，需要重新启动程序才会重新加载。");
            y = s4.Rect.Bottom + S(GapY);
            int dsTop = s4.Rect.Top + S(HeaderH);
            int colW = (cw - S(CardPad) * 2 - S(12)) / 2;
            int r0 = dsTop;
            _chkLogi.Bounds = new Rectangle(x + S(CardPad), r0 + S(8), colW, S(24));
            _chkHid.Bounds = new Rectangle(x + S(CardPad) + colW + S(12), r0 + S(8), colW, S(24));
            int r1 = r0 + S(DsRowH);
            _chkBt.Bounds = new Rectangle(x + S(CardPad), r1 + S(8), colW, S(24));
            _chkXInput.Bounds = new Rectangle(x + S(CardPad) + colW + S(12), r1 + S(8), colW, S(24));
            int r2 = r1 + S(DsRowH);
            _chkDemo.Bounds = new Rectangle(x + S(CardPad), r2 + S(8), colW + S(12), S(24));
            _dsNoteY = r2 + S(DsRowH) + S(4);
            _dsNoteMaxY = s4.Rect.Bottom - S(6);

            // 高级
            Section s5 = AddSection(x, y, cw, "高级", 1, RowH, null, null);
            y = s5.Rect.Bottom + S(GapY);
            row = s5.Rect.Top + S(HeaderH);
            _chkVerbose.Bounds = new Rectangle(x + S(CardPad), row + S(10), cw - S(CardPad) * 2, S(24));

            int bottomY = ClientSize.Height - S(BottomH);
            _btnSave.Bounds = new Rectangle(ClientSize.Width - S(Pad) - S(88), bottomY + S(16), S(88), S(32));
            _btnCancel.Bounds = new Rectangle(ClientSize.Width - S(Pad) - S(88) - S(8) - S(76), bottomY + S(16), S(76), S(32));

            if (_config.VerboseLog)
            {
                Log.Info("设置分区: 常规=" + s1.Rect + " 提醒=" + s2.Rect + " 启动=" + s3.Rect + " 数据源=" + s4.Rect + " 高级=" + s5.Rect);
                Log.Info("设置控件: logi=" + _chkLogi.Bounds + " hid=" + _chkHid.Bounds + " bt=" + _chkBt.Bounds
                    + " xi=" + _chkXInput.Bounds + " demo=" + _chkDemo.Bounds + " balloon=" + _chkBalloon.Bounds
                    + " auto=" + _chkAutoStart.Bounds + " verbose=" + _chkVerbose.Bounds
                    + " stepRefresh=" + _stepRefresh.Bounds + " segTray=" + _segTray.Bounds);
            }

            Invalidate();
        }

        private Section AddSection(int x, int y, int cw, string title, int rows, int rowH, string note, string noteSub)
        {
            int h = S(HeaderH) + rows * S(rowH);
            if (rows == 0)
            {
                // 数据源分区：3 行复选框 + 底部注释
                h = S(HeaderH) + 3 * S(DsRowH) + S(30);
            }
            Section s = new Section();
            s.Rect = new Rectangle(x, y, cw, h);
            s.Title = title;
            s.Note = note;
            s.NoteSub = noteSub;
            _sections.Add(s);
            return s;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            int cw = ClientSize.Width;
            for (int i = 0; i < _sections.Count; i++)
            {
                Section s = _sections[i];
                Draw.FillRound(g, Theme.Card, s.Rect, Theme.SF(this, 10f));
                Draw.StrokeRound(g, Theme.Divider, new RectangleF(s.Rect.X + 0.5f, s.Rect.Y + 0.5f,
                    s.Rect.Width - 1f, s.Rect.Height - 1f), Theme.SF(this, 10f), 1f);
                Draw.TLeft(g, s.Title, Theme.FontBodyBold, Theme.Sub,
                    new Rectangle(s.Rect.X + S(CardPad), s.Rect.Y + S(6), S(200), S(18)), false);
                if (!string.IsNullOrEmpty(s.Note))
                {
                    Draw.TRight(g, s.Note, Theme.FontSmall, Theme.Yellow,
                        new Rectangle(s.Rect.Right - S(CardPad) - S(120), s.Rect.Y + S(6), S(120), S(18)));
                }
                if (!string.IsNullOrEmpty(s.NoteSub))
                {
                    Draw.TLeft(g, s.NoteSub, Theme.FontSmall, Theme.Sub,
                        new Rectangle(s.Rect.X + S(CardPad), _dsNoteY, s.Rect.Width - S(CardPad) * 2, S(16)), true);
                }
            }

            // 行标题
            DrawRowTitle(g, _sections[0].Rect, 0, "刷新间隔", "自动扫描所有数据源的频率（5-300 秒）");
            DrawRowTitle(g, _sections[0].Rect, 1, "托盘显示", "多台设备时，托盘图标显示哪个电量");
            DrawRowTitle(g, _sections[0].Rect, 2, "浮窗透明度", "最小化侧边电量条的透明度（20–100%）");
            DrawRowTitle(g, _sections[1].Rect, 0, "低电量阈值", "电量低于该值时提醒");
            DrawRowTitle(g, _sections[1].Rect, 1, "", "");
            DrawRowTitle(g, _sections[2].Rect, 0, "", "");
            DrawRowTitle(g, _sections[4].Rect, 0, "", "");

            int bottomY = ClientSize.Height - S(BottomH);
            Draw.Line(g, Theme.Divider, 1f, S(Pad), bottomY, cw - S(Pad), bottomY);
            Draw.TLeft(g, "设置会立即保存到本机配置文件；数据源开关除外。", Theme.FontSmall, Theme.Sub,
                new Rectangle(S(Pad), bottomY + S(22), cw - S(Pad) * 2 - S(200), S(18)), true);
        }

        private void DrawRowTitle(Graphics g, Rectangle card, int index, string title, string sub)
        {
            if (string.IsNullOrEmpty(title)) return;
            int rowY = card.Top + S(HeaderH) + index * S(RowH);
            int cw = card.Width;
            int tx = card.X + S(CardPad);
            int tw = cw - S(CardPad) * 2 - S(230);
            if (tw < S(80)) tw = S(80);
            Draw.TLeft(g, title, Theme.FontBody, Theme.Text, new Rectangle(tx, rowY + S(6), tw, S(18)), true);
            if (!string.IsNullOrEmpty(sub))
            {
                Draw.TLeft(g, sub, Theme.FontSmall, Theme.Sub, new Rectangle(tx, rowY + S(23), tw + S(60), S(16)), true);
            }
        }

        private void Save()
        {
            bool dsChanged = (_config.EnableLogitech != _chkLogi.Checked)
                || (_config.EnableStandardHid != _chkHid.Checked)
                || (_config.EnableBluetooth != _chkBt.Checked)
                || (_config.EnableXInput != _chkXInput.Checked);
            bool demoChanged = _config.EnableDemo != (_chkDemo.Checked || _forcedDemo);

            _config.RefreshSeconds = _stepRefresh.Value;
            _config.TrayDisplay = TrayMode(_segTray.SelectedIndex);
            _config.EdgeBarOpacity = _stepOpacity.Value;
            _config.LowBatteryThreshold = _stepLow.Value;
            _config.NotifyLowBattery = _chkBalloon.Checked;
            _config.StartWithWindows = _chkAutoStart.Checked;
            _config.EnableLogitech = _chkLogi.Checked;
            _config.EnableStandardHid = _chkHid.Checked;
            _config.EnableBluetooth = _chkBt.Checked;
            _config.EnableXInput = _chkXInput.Checked;
            _config.EnableDemo = _forcedDemo ? true : _chkDemo.Checked;
            _config.VerboseLog = _chkVerbose.Checked;
            if (_forcedDemo) _config.EnableDemo = true;
            _config.Normalize();

            try { _config.Save(); } catch { }
            bool autoOk = true;
            try { autoOk = AutoStart.SetEnabled(_config.StartWithWindows); } catch { autoOk = false; }
            if (_config.StartWithWindows && !autoOk)
            {
                MessageBox.Show("开机启动写入失败。\n可能被安全软件或系统策略拦截，请尝试以当前用户身份运行本程序，或改用任务计划程序。",
                    "开机启动", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }

            RestartRequired = dsChanged || demoChanged;
            DialogResult = DialogResult.OK;
            Close();
        }

        private static string TrayMode(int index)
        {
            if (index == 1) return "average";
            if (index == 2) return "highest";
            return "worst";
        }

        private static int TrayModeIndex(string mode)
        {
            if (mode == "average") return 1;
            if (mode == "highest") return 2;
            return 0;
        }

        public void PopulateTrayMode()
        {
            _segTray.SelectedIndex = TrayModeIndex(_config.TrayDisplay);
        }

        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            if (keyData == Keys.Escape)
            {
                DialogResult = DialogResult.Cancel;
                Close();
                return true;
            }
            return base.ProcessCmdKey(ref msg, keyData);
        }
    }
}