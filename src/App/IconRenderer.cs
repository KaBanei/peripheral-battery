using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;
using Pb.Core;

namespace Pb.App
{
    /// <summary>托盘动态电池图标：圆角电池 + 填充 + 数字/闪电，切换后销毁 GDI 句柄。</summary>
    internal sealed class IconRenderer : IDisposable
    {
        private Icon _current;
        private static Icon _appIcon;
        private static int _iconSize;

        /// <summary>图标像素尺寸：32 @96dpi，按 DPI 调整。</summary>
        public static int PixelSize
        {
            get
            {
                if (_iconSize > 0) return _iconSize;
                int size = 32;
                try
                {
                    int dpi = Native.SystemDpi();
                    size = (int)Math.Round(32.0 * dpi / 96.0);
                }
                catch { }
                if (size < 16) size = 16;
                if (size > 64) size = 64;
                _iconSize = size;
                return _iconSize;
            }
        }

        public static Icon AppIcon
        {
            get
            {
                if (_appIcon != null) return _appIcon;
                int size = 32;
                using (Bitmap bmp = new Bitmap(size, size, PixelFormat.Format32bppArgb))
                {
                    using (Graphics g = Graphics.FromImage(bmp))
                    {
                        g.SmoothingMode = SmoothingMode.AntiAlias;
                        g.Clear(Color.Transparent);
                        DrawIcon(g, size, (int?)72, BatteryState.Discharging, true);
                    }
                    _appIcon = ToIcon(bmp);
                }
                return _appIcon;
            }
        }

        /// <summary>选择要显示的电量（worst/average/highest）。</summary>
        public static void Pick(IList<Peripheral> devices, string mode, out int? percent, out BatteryState state)
        {
            percent = null;
            state = BatteryState.Unknown;
            if (devices == null || devices.Count == 0) return;

            Peripheral pick = null;
            int sum = 0;
            int n = 0;
            bool anyCharging = false;
            for (int i = 0; i < devices.Count; i++)
            {
                Peripheral d = devices[i];
                if (d == null || !d.Percent.HasValue) continue;
                n++;
                sum += d.Percent.Value;
                if (d.State == BatteryState.Charging) anyCharging = true;
                if (pick == null)
                {
                    pick = d;
                }
                else if (mode == "highest")
                {
                    if (d.Percent.Value > pick.Percent.Value) pick = d;
                }
                else if (mode == "average")
                {
                    if (d.Percent.Value > pick.Percent.Value) pick = d;
                }
                else
                {
                    if (d.Percent.Value < pick.Percent.Value) pick = d;
                }
            }
            if (n == 0) return;
            if (mode == "average")
            {
                percent = (int)Math.Round((double)sum / n);
                state = anyCharging ? BatteryState.Charging : (pick == null ? BatteryState.Unknown : pick.State);
                return;
            }
            percent = pick.Percent;
            state = pick.State;
            if (anyCharging && mode == "worst" && pick.State != BatteryState.Charging)
            {
                // 最低电量设备正在充电时仍按充电显示
                state = pick.State;
            }
        }

        /// <summary>生成新图标（不销毁旧图标，由 Replace 负责）。</summary>
        public Icon Create(IList<Peripheral> devices, string mode)
        {
            using (Bitmap bmp = RenderBitmap(PixelSize, devices, mode))
            {
                return ToIcon(bmp);
            }
        }

        /// <summary>渲染托盘图标位图（供 --icon-test 自检，不创建 HICON）。</summary>
        public static Bitmap RenderBitmap(int size, IList<Peripheral> devices, string mode)
        {
            int? percent;
            BatteryState state;
            Pick(devices, mode, out percent, out state);
            Bitmap bmp = new Bitmap(size, size, PixelFormat.Format32bppArgb);
            using (Graphics g = Graphics.FromImage(bmp))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
                g.Clear(Color.Transparent);
                DrawIcon(g, size, percent, state, percent.HasValue && state != BatteryState.Charging);
            }
            return bmp;
        }

        /// <summary>替换当前图标并释放上一个。</summary>
        public void Replace(Icon next)
        {
            Icon old = _current;
            _current = next;
            if (old != null && !object.ReferenceEquals(old, next))
            {
                try { old.Dispose(); } catch { }
            }
        }

        public void Dispose()
        {
            Icon old = _current;
            _current = null;
            if (old != null) { try { old.Dispose(); } catch { } }
        }

        private static Icon ToIcon(Bitmap bmp)
        {
            IntPtr h = bmp.GetHicon();
            try
            {
                using (Icon tmp = Icon.FromHandle(h))
                {
                    return (Icon)tmp.Clone();
                }
            }
            finally
            {
                Native.DestroyIcon(h);
            }
        }

        private static void DrawIcon(Graphics g, int size, int? percent, BatteryState state, bool showText)
        {
            bool charging = state == BatteryState.Charging;
            bool unknown = !percent.HasValue;
            Color color = unknown ? Theme.Gray : Theme.BatteryColor(percent, state);

            float s = size / 32f;
            float stroke = Math.Max(1.2f, 2f * s);
            float pad = 1.6f * s;
            float nubW = 2.6f * s;
            float nubH = 7f * s;
            RectangleF body = new RectangleF(pad, (size - 16f * s) / 2f, size - pad * 2f - nubW - 0.8f * s, 16f * s);
            RectangleF nub = new RectangleF(body.Right + 1f * s, (size - nubH) / 2f, nubW, nubH);

            SmoothingMode old = g.SmoothingMode;
            g.SmoothingMode = SmoothingMode.AntiAlias;

            // 外框
            using (GraphicsPath p = Draw.Round(body, 3f * s))
            using (SolidBrush b = new SolidBrush(Color.FromArgb(230, color)))
            {
                g.FillPath(b, p);
            }
            using (GraphicsPath p = Draw.Round(nub, 1f * s))
            using (SolidBrush b = new SolidBrush(Color.FromArgb(230, color)))
            {
                g.FillPath(b, p);
            }
            // 内部挖空
            float inset = stroke + 0.9f * s;
            RectangleF inner = new RectangleF(body.X + inset, body.Y + inset, body.Width - inset * 2f, body.Height - inset * 2f);
            using (GraphicsPath p = Draw.Round(inner, 1.6f * s))
            using (SolidBrush b = new SolidBrush(Color.FromArgb(245, 12, 16, 22)))
            {
                g.FillPath(b, p);
            }
            // 电量填充
            if (charging)
            {
                using (GraphicsPath p = Draw.Round(inner, 1.6f * s))
                using (SolidBrush b = new SolidBrush(Color.FromArgb(90, Theme.Blue)))
                {
                    g.FillPath(b, p);
                }
            }
            if (percent.HasValue)
            {
                float ratio = Math.Max(0f, Math.Min(100f, percent.Value)) / 100f;
                float fw = inner.Width * ratio;
                if (fw > 0.5f)
                {
                    if (fw < 1.6f * s) fw = 1.6f * s;
                    RectangleF fill = new RectangleF(inner.X, inner.Y, fw, inner.Height);
                    using (GraphicsPath p = Draw.Round(fill, 1.6f * s))
                    using (SolidBrush b = new SolidBrush(color))
                    {
                        g.FillPath(b, p);
                    }
                }
            }

            if (charging)
            {
                RectangleF bolt = new RectangleF(body.X + body.Width * 0.27f, body.Y + body.Height * 0.10f,
                    body.Width * 0.46f, body.Height * 0.80f);
                BoltWithHalo(g, bolt, size);
            }
            else if (showText && percent.HasValue && size >= 20)
            {
                float fontPx = size * 0.40f;
                if (fontPx < 6f) fontPx = 6f;
                using (Font f = new Font(Theme.Family, fontPx, FontStyle.Bold, GraphicsUnit.Pixel))
                using (StringFormat fmt = new StringFormat())
                {
                    fmt.Alignment = StringAlignment.Center;
                    fmt.LineAlignment = StringAlignment.Center;
                    string text = percent.Value.ToString();
                    float o = Math.Max(1f, size * 0.035f);
                    using (SolidBrush halo = new SolidBrush(Color.FromArgb(235, 10, 14, 20)))
                    using (SolidBrush fg = new SolidBrush(Color.FromArgb(250, 245, 248, 252)))
                    {
                        g.DrawString(text, f, halo, new RectangleF(body.X - o, body.Y, body.Width, body.Height), fmt);
                        g.DrawString(text, f, halo, new RectangleF(body.X + o, body.Y, body.Width, body.Height), fmt);
                        g.DrawString(text, f, halo, new RectangleF(body.X, body.Y - o, body.Width, body.Height), fmt);
                        g.DrawString(text, f, halo, new RectangleF(body.X, body.Y + o, body.Width, body.Height), fmt);
                        g.DrawString(text, f, fg, body, fmt);
                    }
                }
            }
            else if (unknown)
            {
                using (Font f = new Font(Theme.Family, size * 0.46f, FontStyle.Bold, GraphicsUnit.Pixel))
                using (StringFormat fmt = new StringFormat())
                {
                    fmt.Alignment = StringAlignment.Center;
                    fmt.LineAlignment = StringAlignment.Center;
                    using (SolidBrush fg = new SolidBrush(Theme.Gray))
                    {
                        g.DrawString("?", f, fg, new RectangleF(body.X, body.Y, body.Width, body.Height), fmt);
                    }
                }
            }
            g.SmoothingMode = old;
        }

        private static void BoltWithHalo(Graphics g, RectangleF r, int size)
        {
            float o = Math.Max(1f, size * 0.035f);
            Color halo = Color.FromArgb(210, 10, 14, 20);
            Draw.Bolt(g, new RectangleF(r.X - o, r.Y, r.Width, r.Height), halo);
            Draw.Bolt(g, new RectangleF(r.X + o, r.Y, r.Width, r.Height), halo);
            Draw.Bolt(g, new RectangleF(r.X, r.Y - o, r.Width, r.Height), halo);
            Draw.Bolt(g, new RectangleF(r.X, r.Y + o, r.Width, r.Height), halo);
            Draw.Bolt(g, r, Color.White);
        }
    }
}