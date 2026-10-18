using System;
using System.Diagnostics;
using System.Drawing;
using System.Windows.Forms;
using Pb.Core;

namespace Pb.App
{
    /// <summary>关于窗口。</summary>
    internal sealed class AboutForm : ChromeForm
    {
        private const int LogicalWidth = 460;
        private const int LogicalHeight = 430;
        private readonly DarkButton _btnOpenLog;
        private readonly DarkButton _btnClose;

        public AboutForm() : base("关于")
        {
            StartPosition = FormStartPosition.CenterScreen;
            KeyPreview = true;

            DpiChanged += delegate
            {
                RelayoutWindow();
            };

            _btnOpenLog = new DarkButton();
            _btnOpenLog.Text = "打开日志目录";
            _btnOpenLog.Click += delegate { OpenLog(); };
            Controls.Add(_btnOpenLog);

            _btnClose = new DarkButton();
            _btnClose.Text = "关闭";
            _btnClose.Primary = true;
            _btnClose.Click += delegate { Close(); };
            Controls.Add(_btnClose);
        }

        private int S(int v)
        {
            return (int)Math.Round(v * Theme.ScaleFactor(this));
        }

        protected override void OnLoad(EventArgs e)
        {
            base.OnLoad(e);
            RelayoutWindow();
        }

        private void RelayoutWindow()
        {
            ClientSize = new Size(S(LogicalWidth), S(LogicalHeight));
            LayoutChrome();
            _btnOpenLog.Bounds = new Rectangle(S(20), ClientSize.Height - S(56), S(124), S(32));
            _btnClose.Bounds = new Rectangle(ClientSize.Width - S(20) - S(88), ClientSize.Height - S(56), S(88), S(32));
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            Graphics g = e.Graphics;
            int x = S(24);
            int w = ClientSize.Width - S(48);

            Draw.TLeft(g, "2.4G 外设电量", Theme.FontTitle, Theme.Text, new Rectangle(x, S(66), w, S(28)), true);
            string ver = AppInfo.Version;
            Draw.TLeft(g, "版本 " + ver + " · .NET Framework 4.8 · WinForms", Theme.FontSub, Theme.Sub,
                new Rectangle(x, S(96), w, S(20)), true);

            int y = S(136);
            y = Para(g, x, y, w, "关于", "常驻托盘，自动汇总罗技 HID++、标准 HID、蓝牙与 Xbox 手柄的电量。界面完全在本机渲染，不联网、不上传。");
            y = Para(g, x, y + S(14), w, "演示模式", "在“设置 → 数据源”中勾选“演示模式”后，需要重启程序才会生效；也可以用 --demo 参数启动。");
            y = Para(g, x, y + S(14), w, "本机路径", "配置：" + AppConfig.ConfigPath + "\n日志：" + Log.FilePath);
        }

        private int Para(Graphics g, int x, int y, int w, string title, string body)
        {
            Draw.TLeft(g, title, Theme.FontBodyBold, Theme.Accent, new Rectangle(x, y, w, S(20)), false);
            y += S(24);
            string[] lines = body.Split('\n');
            for (int i = 0; i < lines.Length; i++)
            {
                Draw.TLeft(g, lines[i], Theme.FontSmall, Theme.Text, new Rectangle(x, y, w, S(18)), true);
                y += S(18);
            }
            return y + S(6);
        }

        private void OpenLog()
        {
            try
            {
                Process.Start("explorer.exe", "/select,\"" + Log.FilePath + "\"");
            }
            catch (Exception ex)
            {
                Log.Warn("打开日志目录失败: " + ex.Message);
            }
        }

        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            if (keyData == Keys.Escape)
            {
                Close();
                return true;
            }
            return base.ProcessCmdKey(ref msg, keyData);
        }
    }
}
