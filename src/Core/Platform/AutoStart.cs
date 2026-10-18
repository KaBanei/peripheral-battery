using System;
using System.Reflection;
using Microsoft.Win32;

namespace Pb.Core.Platform
{
    public static class AutoStart
    {
        private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
        private const string ValueName = "PeripheralBattery";
        private const string ApprovedKey = @"Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run";

        public static string ExePath
        {
            get
            {
                try
                {
                    var asm = Assembly.GetEntryAssembly();
                    if (asm != null && !string.IsNullOrEmpty(asm.Location)) return asm.Location;
                }
                catch { }
                return System.Diagnostics.Process.GetCurrentProcess().MainModule.FileName;
            }
        }

        public static string Command
        {
            get { return "\"" + ExePath + "\" --tray"; }
        }

        public static bool IsEnabled
        {
            get
            {
                try
                {
                    using (var key = Registry.CurrentUser.OpenSubKey(RunKey, false))
                    {
                        if (key == null) return false;
                        var v = key.GetValue(ValueName) as string;
                        if (string.IsNullOrEmpty(v)) return false;
                        if (IsBlockedByStartupApproved()) return false;
                        return true;
                    }
                }
                catch { return false; }
            }
        }

        /// <summary>任务管理器把启动项设为“禁用”时会写 StartupApproved；值为奇数表示禁用。</summary>
        private static bool IsBlockedByStartupApproved()
        {
            try
            {
                using (var key = Registry.CurrentUser.OpenSubKey(ApprovedKey, false))
                {
                    if (key == null) return false;
                    var b = key.GetValue(ValueName) as byte[];
                    if (b == null || b.Length == 0) return false;
                    return (b[0] & 1) == 1;
                }
            }
            catch { return false; }
        }

        private static void ClearStartupApproved()
        {
            try
            {
                using (var key = Registry.CurrentUser.CreateSubKey(ApprovedKey))
                {
                    if (key != null && key.GetValue(ValueName) != null)
                    {
                        key.DeleteValue(ValueName, false);
                    }
                }
            }
            catch { }
        }

        public static bool SetEnabled(bool enabled)
        {
            try
            {
                using (var key = Registry.CurrentUser.CreateSubKey(RunKey))
                {
                    if (key == null) return false;
                    if (enabled) key.SetValue(ValueName, Command, RegistryValueKind.String);
                    else if (key.GetValue(ValueName) != null) key.DeleteValue(ValueName, false);
                }
                if (enabled)
                {
                    ClearStartupApproved();
                    Log.Info("开机启动已启用: " + Command);
                    return IsEnabled;
                }
                ClearStartupApproved();
                Log.Info("开机启动已关闭");
                return true;
            }
            catch (Exception ex)
            {
                Log.Warn("设置开机启动失败: " + ex.Message);
                return false;
            }
        }
    }
}