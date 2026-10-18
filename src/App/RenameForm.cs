using System;
using System.Drawing;
using System.Windows.Forms;
using Pb.Core;

namespace Pb.App
{
    /// <summary>重命名设备的小模态框。</summary>
    internal sealed class RenameForm : ChromeForm
    {
        private const int LogicalWidth = 440;
        private const int LogicalHeight = 252;

        private readonly AppConfig _config;
        private readonly Peripheral _device;
        private readonly string _originName;
        private readonly bool _originKnown;
        private readonly TextBox _input;
        private readonly DarkButton _btnRestore;
        private readonly DarkButton _btnCancel;
        private readonly DarkButton _btnSave;

        private string _error = "";
        private bool _supported = true;

        /// <summary>保存后的显示名；空字符串表示恢复默认。</summary>
        public string AppliedName { get; private set; }

        public RenameForm(AppConfig config, Peripheral device, string originName, bool originKnown) : base("重命名设备")
        {
            _config = config;
            _device = device;
            _originKnown = originKnown;
            _originName = string.IsNullOrEmpty(originName)
                ? (device == null ? "" : (device.Name ?? ""))
                : originName;
            AppliedName = "";
            StartPosition = FormStartPosition.CenterParent;
            KeyPreview = true;

            _supported = device != null && !string.IsNullOrEmpty(device.Id);

            _input = new TextBox();
            _input.BorderStyle = BorderStyle.None;
            _input.BackColor = Theme.Field;
            _input.ForeColor = Theme.Text;
            _input.Font = Theme.FontBody;
            _input.MaxLength = 20;
            string prefill = "";
            if (_supported)
            {
                string custom = _config.GetCustomName(_device.Id);
                prefill = string.IsNullOrEmpty(custom) ? (_device.Name ?? "") : custom;
            }
            if (_input.MaxLength > 0 && prefill.Length > _input.MaxLength)
            {
                // 原名称可能超过 20 字：先放开限制填入，让用户看到真实名称并自行精简
                _input.MaxLength = 0;
                _input.Text = prefill;
                _input.MaxLength = 20;
            }
            else
            {
                _input.Text = prefill;
            }
            _input.TextChanged += delegate { Invalidate(); };
            _input.KeyDown += OnInputKeyDown;
            Controls.Add(_input);

            _btnRestore = new DarkButton();
            _btnRestore.Text = "恢复默认";
            _btnRestore.Click += delegate { Restore(); };
            Controls.Add(_btnRestore);

            _btnCancel = new DarkButton();
            _btnCancel.Text = "取消";
            _btnCancel.Click += delegate
            {
                DialogResult = DialogResult.Cancel;
                Close();
            };
            Controls.Add(_btnCancel);

            _btnSave = new DarkButton();
            _btnSave.Text = "保存";
            _btnSave.Primary = true;
            _btnSave.Click += delegate { Save(); };
            Controls.Add(_btnSave);

            if (!_supported)
            {
                _error = "该设备没有稳定标识，暂不支持重命名";
                _btnSave.Enabled = false;
                _btnRestore.Enabled = false;
            }
        }

        private int S(int v)
        {
            return (int)Math.Round(v * Theme.ScaleFactor(this));
        }

        protected override void OnLoad(EventArgs e)
        {
            base.OnLoad(e);
            RelayoutWindow();
            if (_supported)
            {
                _input.Focus();
                _input.SelectAll();
            }
        }

        private void RelayoutWindow()
        {
            ClientSize = new Size(S(LogicalWidth), S(LogicalHeight));
            LayoutChrome();
            int pad = S(20);
            int w = ClientSize.Width;
            _input.Bounds = new Rectangle(pad + S(10), S(92) + S(8), w - pad * 2 - S(20), S(20));
            _btnRestore.Bounds = new Rectangle(pad, ClientSize.Height - S(52), S(96), S(32));
            _btnSave.Bounds = new Rectangle(w - pad - S(88), ClientSize.Height - S(52), S(88), S(32));
            _btnCancel.Bounds = new Rectangle(w - pad - S(88) - S(8) - S(76), ClientSize.Height - S(52), S(76), S(32));
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            Graphics g = e.Graphics;
            g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            int pad = S(20);
            int w = ClientSize.Width;

            string origin = (_originKnown ? "原名称：" : "当前名称：") + _originName;
            if (_device != null)
            {
                origin += "   ·   " + _device.KindText;
                if (!string.IsNullOrEmpty(_device.Brand)) origin += " · " + _device.Brand;
                if (!string.IsNullOrEmpty(_device.Connection)) origin += " · " + _device.Connection;
            }
            Draw.TLeft(g, origin, Theme.FontSub, Theme.Sub, new Rectangle(pad, S(62), w - pad * 2, S(20)), true);

            Draw.FillRound(g, Theme.Field, new RectangleF(pad, S(92), w - pad * 2, S(36)), Theme.SF(this, 8f));
            Draw.StrokeRound(g, _error.Length > 0 ? Theme.Red : Theme.Border,
                new RectangleF(pad + 0.5f, S(92) + 0.5f, w - pad * 2 - 1f, S(36) - 1f), Theme.SF(this, 8f), 1f);

            string counter = _input.TextLength + "/20";
            bool over = _input.TextLength > 20;
            Draw.TRight(g, counter, Theme.FontSmall, over ? Theme.Red : Theme.Sub,
                new Rectangle(w - pad - S(80), S(134), S(80), S(18)));
            if (_error.Length > 0)
            {
                Draw.TLeft(g, _error, Theme.FontSmall, Theme.Red,
                    new Rectangle(pad, S(134), w - pad * 2 - S(86), S(18)), true);
            }
            else
            {
                Draw.TLeft(g, "留空保存 = 恢复为设备原始名称", Theme.FontSmall, Theme.Sub,
                    new Rectangle(pad, S(156), w - pad * 2, S(16)), true);
            }
        }

        private void OnInputKeyDown(object sender, KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Enter)
            {
                e.Handled = true;
                e.SuppressKeyPress = true;
                Save();
            }
            else if (e.KeyCode == Keys.Escape)
            {
                e.Handled = true;
                e.SuppressKeyPress = true;
                DialogResult = DialogResult.Cancel;
                Close();
            }
        }

        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            if (keyData == Keys.Escape)
            {
                DialogResult = DialogResult.Cancel;
                Close();
                return true;
            }
            if (keyData == Keys.Enter && !_input.Focused)
            {
                Save();
                return true;
            }
            return base.ProcessCmdKey(ref msg, keyData);
        }

        private void Save()
        {
            if (!_supported) return;
            string name = (_input.Text ?? "").Trim();
            if (name.Length > 20)
            {
                _error = "最多 20 个字符";
                Invalidate();
                return;
            }
            if (!_config.SetCustomName(_device.Id, name))
            {
                _error = "最多 20 个字符";
                Invalidate();
                return;
            }
            try { _config.Save(); } catch { }
            AppliedName = name;
            DialogResult = DialogResult.OK;
            Close();
        }

        private void Restore()
        {
            if (!_supported) return;
            if (!_config.SetCustomName(_device.Id, ""))
            {
                _error = "最多 20 个字符";
                Invalidate();
                return;
            }
            try { _config.Save(); } catch { }
            AppliedName = "";
            DialogResult = DialogResult.OK;
            Close();
        }
    }
}
