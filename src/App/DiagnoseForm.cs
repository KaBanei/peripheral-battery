using System;
using System.Drawing;
using System.Windows.Forms;
using Pb.Core;

namespace Pb.App
{
    /// <summary>诊断信息窗口（--diagnose）。</summary>
    internal sealed class DiagnoseForm : ChromeForm
    {
        private const int LogicalWidth = 660;
        private const int LogicalHeight = 600;
        private readonly TextBox _text;
        private readonly DarkButton _btnCopy;
        private readonly DarkButton _btnClose;

        public DiagnoseForm(string content) : base("诊断信息")
        {
            StartPosition = FormStartPosition.CenterScreen;
            KeyPreview = true;

            DpiChanged += delegate { RelayoutWindow(); };

            _text = new TextBox();
            _text.Multiline = true;
            _text.ReadOnly = true;
            _text.WordWrap = true;
            _text.ScrollBars = ScrollBars.Vertical;
            _text.BorderStyle = BorderStyle.None;
            _text.BackColor = Theme.Field;
            _text.ForeColor = Theme.Text;
            _text.Font = Theme.FontMono;
            _text.Text = content == null ? "" : content;
            Controls.Add(_text);

            _btnCopy = new DarkButton();
            _btnCopy.Text = "复制诊断信息";
            _btnCopy.Click += delegate { Copy(); };
            Controls.Add(_btnCopy);

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
            _text.Select(0, 0);
        }

        private void RelayoutWindow()
        {
            ClientSize = new Size(S(LogicalWidth), S(LogicalHeight));
            LayoutChrome();
            int pad = S(16);
            _text.Bounds = new Rectangle(pad, S(64), ClientSize.Width - pad * 2, ClientSize.Height - S(64) - S(72));
            _btnCopy.Bounds = new Rectangle(pad, ClientSize.Height - S(54), S(124), S(32));
            _btnClose.Bounds = new Rectangle(ClientSize.Width - pad - S(88), ClientSize.Height - S(54), S(88), S(32));
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            Graphics g = e.Graphics;
            Rectangle r = _text.Bounds;
            Draw.StrokeRound(g, Theme.Border, new RectangleF(r.X - 0.5f, r.Y - 0.5f, r.Width + 1f, r.Height + 1f), Theme.SF(this, 6f), 1f);
            Draw.TLeft(g, "可复制以下内容发给开发者，日志文件同目录下。", Theme.FontSmall, Theme.Sub,
                new Rectangle(S(150), ClientSize.Height - S(54), ClientSize.Width - S(150) - S(110), S(32)), true);
        }

        private void Copy()
        {
            try
            {
                Clipboard.SetText(_text.Text);
            }
            catch (Exception ex)
            {
                Log.Warn("复制诊断信息失败: " + ex.Message);
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
