using System;
using System.Collections.Generic;
using System.Text;
using System.Windows.Forms;
using Pb.Core;
using Pb.Core.Platform;

namespace Pb.App
{
    internal static class Diag
    {
        public static string Build(AppConfig cfg, IList<Peripheral> devices, IList<ProviderStatus> statuses, DateTime at)
        {
            var sb = new StringBuilder();
            sb.AppendLine("2.4G 外设电量 · 诊断信息");
            sb.AppendLine("----------------------------------------");
            sb.AppendLine("时间      : " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
            sb.AppendLine("更新时间  : " + (at == default(DateTime) ? "—" : at.ToString("yyyy-MM-dd HH:mm:ss")));
            sb.AppendLine("程序      : " + SafeExePath());
            sb.AppendLine("版本      : " + AppInfo.Version);
            sb.AppendLine("系统      : " + Environment.OSVersion.VersionString + "  " + (Environment.Is64BitProcess ? "x64" : "x86"));
            sb.AppendLine("DPI       : " + SafeDpi() + "  屏幕: " + SafeScreen());
            sb.AppendLine("配置文件  : " + AppConfig.ConfigPath);
            sb.AppendLine("日志文件  : " + Log.FilePath);
            sb.AppendLine("刷新间隔  : " + cfg.RefreshSeconds + " 秒");
            sb.AppendLine("托盘显示  : " + cfg.TrayDisplay);
            sb.AppendLine("低电量阈值: " + cfg.LowBatteryThreshold + "%");
            sb.AppendLine("开机启动  : " + (AutoStart.IsEnabled ? "已启用" : "未启用"));
            sb.AppendLine("演示模式  : " + (cfg.EnableDemo ? "开启" : "关闭"));
            sb.AppendLine("数据源开关: 罗技=" + OnOff(cfg.EnableLogitech) + " HID=" + OnOff(cfg.EnableStandardHid)
                + " 蓝牙=" + OnOff(cfg.EnableBluetooth) + " 手柄=" + OnOff(cfg.EnableXInput));
            sb.AppendLine();
            sb.AppendLine("== 数据源 (" + (statuses == null ? 0 : statuses.Count) + ") ==");
            if (statuses == null || statuses.Count == 0)
            {
                sb.AppendLine("（尚未注册数据源，请检查 Provider 程序集）");
            }
            else
            {
                for (int i = 0; i < statuses.Count; i++)
                {
                    ProviderStatus st = statuses[i];
                    string state = !st.Enabled ? "停用" : (st.Ok ? "正常" : "失败");
                    sb.AppendLine("- " + st.DisplayName + "  [" + st.Key + "]  " + state
                        + "  " + st.DurationMs + " ms  " + st.Count + " 台");
                    if (!string.IsNullOrEmpty(st.Error)) sb.AppendLine("    错误: " + st.Error);
                }
            }
            sb.AppendLine();
            sb.AppendLine("== 设备 (" + (devices == null ? 0 : devices.Count) + ") ==");
            if (devices == null || devices.Count == 0)
            {
                sb.AppendLine("（未发现设备）");
            }
            else
            {
                for (int i = 0; i < devices.Count; i++)
                {
                    Peripheral d = devices[i];
                    sb.AppendLine("- " + d.Name + "  [" + d.KindText + "]");
                    sb.AppendLine("    品牌=" + d.Brand + "  连接=" + d.Connection
                        + "  电量=" + Fmt.Percent(d.Percent) + "  状态=" + Theme.StateText(d.State)
                        + "  电压=" + Fmt.Mv(d.MilliVolts) + "  信号=" + Fmt.Signal(d.Signal));
                    sb.AppendLine("    VID:PID=" + Fmt.IdPair(d.Vid, d.Pid) + "  数据源=" + d.Provider
                        + "  更新=" + d.Updated.ToString("HH:mm:ss") + (d.Simulated ? "  （演示）" : ""));
                    if (!string.IsNullOrEmpty(d.DevicePath)) sb.AppendLine("    路径=" + d.DevicePath);
                    if (!string.IsNullOrEmpty(d.Detail)) sb.AppendLine("    明细=" + d.Detail);
                }
            }
            return sb.ToString();
        }

        private static string OnOff(bool v)
        {
            return v ? "开" : "关";
        }

        private static string SafeExePath()
        {
            try { return Application.ExecutablePath; } catch { return "—"; }
        }

        private static string SafeDpi()
        {
            try
            {
                using (System.Drawing.Graphics g = System.Drawing.Graphics.FromHwnd(IntPtr.Zero))
                {
                    return g.DpiX + " DPI";
                }
            }
            catch { return "—"; }
        }

        private static string SafeScreen()
        {
            try
            {
                Screen s = Screen.PrimaryScreen;
                if (s == null) return "—";
                return s.Bounds.Width + "x" + s.Bounds.Height + "（工作区 " + s.WorkingArea.Width + "x" + s.WorkingArea.Height + "）";
            }
            catch { return "—"; }
        }
    }
}
