using System;
using System.Drawing;
using System.Windows.Forms;
using Pb.Core;

namespace Pb.App
{
    /// <summary>深色主题：颜色、字体、DPI 缩放与电量配色。</summary>
    internal static class Theme
    {
        public static readonly Color Bg = Hex("#0F1115");
        public static readonly Color Card = Hex("#171B22");
        public static readonly Color CardHover = Hex("#1D2431");
        public static readonly Color Field = Hex("#12171F");
        public static readonly Color Tile = Hex("#1E2530");
        public static readonly Color Accent = Hex("#4DA3FF");
        public static readonly Color AccentSoft = Hex("#22344A");
        public static readonly Color Text = Hex("#E5E7EB");
        public static readonly Color Sub = Hex("#94A3B8");
        public static readonly Color Divider = Hex("#232A36");
        public static readonly Color Border = Hex("#2A3240");
        public static readonly Color Green = Hex("#34D399");
        public static readonly Color Yellow = Hex("#FBBF24");
        public static readonly Color Red = Hex("#F87171");
        public static readonly Color Blue = Hex("#38BDF8");
        public static readonly Color Gray = Hex("#94A3B8");
        public static readonly Color OnAccent = Hex("#0B1220");

        public static readonly FontFamily Family = ResolveFamily();

        public static readonly Font FontTitle = new Font(Family, 12f, FontStyle.Bold, GraphicsUnit.Point);
        public static readonly Font FontH1 = new Font(Family, 10.5f, FontStyle.Bold, GraphicsUnit.Point);
        public static readonly Font FontBody = new Font(Family, 9f, FontStyle.Regular, GraphicsUnit.Point);
        public static readonly Font FontBodyBold = new Font(Family, 9f, FontStyle.Bold, GraphicsUnit.Point);
        public static readonly Font FontSub = new Font(Family, 8.25f, FontStyle.Regular, GraphicsUnit.Point);
        public static readonly Font FontSmall = new Font(Family, 7.75f, FontStyle.Regular, GraphicsUnit.Point);
        public static readonly Font FontNum = new Font(Family, 15f, FontStyle.Bold, GraphicsUnit.Point);
        public static readonly Font FontNumBig = new Font(Family, 18f, FontStyle.Bold, GraphicsUnit.Point);
        public static readonly Font FontMono = new Font("Consolas", 9f, FontStyle.Regular, GraphicsUnit.Point);

        private static Color Hex(string s)
        {
            try { return ColorTranslator.FromHtml(s); } catch { return Color.Black; }
        }

        private static FontFamily ResolveFamily()
        {
            string[] names = new string[] { "Microsoft YaHei UI", "Microsoft YaHei", "Segoe UI", "Tahoma" };
            for (int i = 0; i < names.Length; i++)
            {
                try
                {
                    FontFamily f = new FontFamily(names[i]);
                    if (f != null) return f;
                }
                catch { }
            }
            return FontFamily.GenericSansSerif;
        }

        /// <summary>
        /// 控件当前 DPI。优先 GetDpiForWindow，回落 DeviceDpi，最后用真实监视器 DPI，
        /// 避免部分环境下 DeviceDpi 误报 96 造成 125%/150% 缩放下布局偏小。
        /// </summary>
        public static int DpiOf(Control c)
        {
            if (c == null) return 96;
            int dpi = 0;
            try
            {
                if (c.IsHandleCreated) dpi = Native.WindowDpi(c.Handle);
            }
            catch { }
            if (dpi <= 0)
            {
                try
                {
                    int d = c.DeviceDpi;
                    if (d >= 72 && d <= 480) dpi = d;
                }
                catch { }
            }
            try
            {
                int mon = Native.MonitorDpi(c.IsHandleCreated ? c.Handle : IntPtr.Zero);
                if (mon > dpi) dpi = mon;
            }
            catch { }
            if (dpi <= 0) dpi = 96;
            return dpi;
        }

        /// <summary>DPI 缩放系数（相对 96 DPI）。</summary>
        public static float ScaleFactor(Control c)
        {
            try { return DpiOf(c) / 96f; }
            catch { return 1f; }
        }

        public static string DpiDebug(Control c)
        {
            try
            {
                string design = "?";
                string current = "?";
                ContainerControl cc = c as ContainerControl;
                if (cc != null)
                {
                    design = cc.AutoScaleDimensions.ToString();
                    current = cc.CurrentAutoScaleDimensions.ToString();
                }
                return "dpi=" + DpiOf(c) + " deviceDpi=" + c.DeviceDpi
                    + " windowDpi=" + (c.IsHandleCreated ? Native.WindowDpi(c.Handle) : 0)
                    + " monitorDpi=" + Native.MonitorDpi(c.IsHandleCreated ? c.Handle : IntPtr.Zero)
                    + " scale=" + ScaleFactor(c).ToString("0.00")
                    + " autoDesign=" + design + " autoCurrent=" + current;
            }
            catch { return "dpi=? "; }
        }

        /// <summary>把 96 DPI 逻辑像素换算为当前控件的物理像素。</summary>
        public static int S(Control c, int v)
        {
            return (int)Math.Round(v * ScaleFactor(c));
        }

        public static float SF(Control c, float v)
        {
            return v * ScaleFactor(c);
        }

        public static void InitForm(Form f, string title)
        {
            f.Text = title;
            f.Font = FontBody;
            f.BackColor = Bg;
            f.ForeColor = Text;
            f.AutoScaleMode = AutoScaleMode.Dpi;
            f.AutoScaleDimensions = new SizeF(96f, 96f);
            f.FormBorderStyle = FormBorderStyle.None;
            f.StartPosition = FormStartPosition.Manual;
            f.ShowInTaskbar = false;
            f.MaximizeBox = false;
            f.MinimizeBox = false;
            f.ShowIcon = false;
        }

        public static Color BatteryColor(int? percent, BatteryState state)
        {
            if (state == BatteryState.Charging) return Blue;
            if (!percent.HasValue) return Gray;
            if (percent.Value >= 50) return Green;
            if (percent.Value >= 20) return Yellow;
            return Red;
        }

        public static string StateText(BatteryState state)
        {
            switch (state)
            {
                case BatteryState.Discharging: return "放电中";
                case BatteryState.Charging: return "充电中";
                case BatteryState.Full: return "已充满";
                case BatteryState.Low: return "电量低";
                case BatteryState.Critical: return "电量极低";
                case BatteryState.Wired: return "有线供电";
                default: return "未知";
            }
        }
    }
}
