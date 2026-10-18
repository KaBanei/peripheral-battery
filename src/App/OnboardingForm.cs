using System;
using System.Drawing;
using System.Windows.Forms;

namespace Pb.App
{
    /// <summary>首次运行引导。</summary>
    internal sealed class OnboardingForm : ChromeForm
    {
        private const int LogicalWidth = 480;
        private const int LogicalHeight = 560;
        private readonly DarkButton _btnStart;

        public OnboardingForm() : base("欢迎使用")
        {
            StartPosition = FormStartPosition.CenterScreen;
            KeyPreview = true;

            DpiChanged += delegate { Relayout(); };

            _btnStart = new DarkButton();
            _btnStart.Text = "开始使用";
            _btnStart.Primary = true;
            _btnStart.Click += delegate
            {
                DialogResult = DialogResult.OK;
                Close();
            };
            Controls.Add(_btnStart);
        }

        private int S(int v)
        {
            return (int)Math.Round(v * Theme.ScaleFactor(this));
        }

        protected override void OnLoad(EventArgs e)
        {
            base.OnLoad(e);
            Relayout();
        }

        private void Relayout()
        {
            ClientSize = new Size(S(LogicalWidth), S(LogicalHeight));
            LayoutChrome();
            _btnStart.Bounds = new Rectangle(ClientSize.Width - S(20) - S(124), ClientSize.Height - S(58), S(124), S(34));
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            Graphics g = e.Graphics;
            int x = S(28);
            int w = ClientSize.Width - S(56);

            Draw.TLeft(g, "欢迎使用 2.4G 外设电量", Theme.FontTitle, Theme.Text,
                new Rectangle(x, S(64), w, S(28)), true);
            Draw.TLeft(g, "常驻系统托盘，随时查看无线外设的电量", Theme.FontSub, Theme.Sub,
                new Rectangle(x, S(94), w, S(20)), true);

            int y = S(128);
            y = Block(g, x, y, w, "支持的设备", new string[]
            {
                "罗技 HID++ 设备（MX / G 系列 2.4G 接收器）",
                "标准 HID 电量（Usage Page 0x06）",
                "蓝牙低功耗设备（读取系统电量属性）",
                "Xbox 无线手柄（XInput 电量等级）"
            });
            y = Block(g, x, y + S(20), w, "已知限制", new string[]
            {
                "部分品牌使用私有协议，暂只能显示“未知”",
                "蓝牙电量来自系统中转，可能延迟一个刷新周期",
                "接收器需插好，部分设备需以管理员身份运行"
            });
            y = Block(g, x, y + S(20), w, "隐私", new string[]
            {
                "所有数据仅在本机读取与展示，不联网、不上传"
            });
            Draw.TLeft(g, "之后可随时在“设置”中调整刷新间隔、数据源与提醒方式。", Theme.FontSmall, Theme.Sub,
                new Rectangle(x, y + S(16), w, S(18)), true);
        }

        private int Block(Graphics g, int x, int y, int w, string title, string[] lines)
        {
            Draw.TLeft(g, title, Theme.FontBodyBold, Theme.Accent, new Rectangle(x, y, w, S(20)), false);
            y += S(24);
            for (int i = 0; i < lines.Length; i++)
            {
                Draw.TLeft(g, "· " + lines[i], Theme.FontBody, Theme.Text, new Rectangle(x, y, w, S(22)), true);
                y += S(22);
            }
            return y;
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
