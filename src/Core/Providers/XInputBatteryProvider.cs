using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Threading;
using Pb.Core.Platform;

namespace Pb.Core.Providers
{
    /// <summary>
    /// Xbox 手柄电量 Provider（XInput）。
    ///
    /// 限制：XInput 只提供 4 档电量（空/低/中/满），没有百分比，也没有电压。
    /// 所以 Percent 是档位估算值（5/25/50/100），Detail 里会明确写出"档位估算"，
    /// 避免用户拿它和 HID 的精确百分比比较。
    ///
    /// 架构相关：xinput1_4.dll（Win8+）/ xinput1_3.dll（Win7）都用 LoadLibrary 动态加载，
    /// 找不到就永久标记失败，扫描直接返回空列表。
    /// </summary>
    public sealed class XInputBatteryProvider : IProvider
    {
        private static readonly string[] DllCandidates = new string[]
        {
            "xinput1_4.dll",
            "xinput1_3.dll"
        };

        private readonly AppConfig _config;
        private readonly IAppContext _ctx;
        private readonly object _sync = new object();

        private IntPtr _module = IntPtr.Zero;
        private XInputInterop.XInputGetStateDelegate _getState;
        private XInputInterop.XInputGetBatteryInformationDelegate _getBattery;
        private bool _loadAttempted;
        private bool _disposed;

        public XInputBatteryProvider(AppConfig cfg, IAppContext ctx)
        {
            _config = cfg;
            _ctx = ctx;
        }

        public string Key { get { return "xinput"; } }
        public string DisplayName { get { return "Xbox 手柄"; } }
        public bool Enabled { get { return _config != null && _config.EnableXInput; } }

        public void Dispose()
        {
            lock (_sync)
            {
                _disposed = true;
                _getState = null;
                _getBattery = null;
                if (_module != IntPtr.Zero)
                {
                    try { XInputInterop.FreeLibrary(_module); } catch { }
                    _module = IntPtr.Zero;
                }
            }
        }

        public List<Peripheral> Scan(CancellationToken ct)
        {
            var result = new List<Peripheral>();
            if (!Enabled) return result;

            lock (_sync)
            {
                if (_disposed) return result;
                try
                {
                    if (!_loadAttempted) LoadLibrary();
                    if (_getState == null || _getBattery == null) return result;
                    ScanCore(ct, result);
                }
                catch (Exception ex)
                {
                    Log.Warn("XInput 扫描失败（已跳过）: " + ex.Message);
                }
            }
            return result;
        }

        private void LoadLibrary()
        {
            _loadAttempted = true;
            for (int i = 0; i < DllCandidates.Length; i++)
            {
                string dll = DllCandidates[i];
                IntPtr module = IntPtr.Zero;
                try { module = XInputInterop.LoadLibrary(dll); }
                catch { module = IntPtr.Zero; }
                if (module == IntPtr.Zero) continue;

                try
                {
                    IntPtr pState = XInputInterop.GetProcAddress(module, "XInputGetState");
                    IntPtr pBattery = XInputInterop.GetProcAddress(module, "XInputGetBatteryInformation");
                    if (pState == IntPtr.Zero || pBattery == IntPtr.Zero)
                    {
                        XInputInterop.FreeLibrary(module);
                        continue;
                    }
                    _getState = (XInputInterop.XInputGetStateDelegate)
                        Marshal.GetDelegateForFunctionPointer(pState, typeof(XInputInterop.XInputGetStateDelegate));
                    _getBattery = (XInputInterop.XInputGetBatteryInformationDelegate)
                        Marshal.GetDelegateForFunctionPointer(pBattery, typeof(XInputInterop.XInputGetBatteryInformationDelegate));
                    _module = module;
                    if (_ctx != null) _ctx.Log("XInput 已加载: " + dll);
                    return;
                }
                catch (Exception ex)
                {
                    try { XInputInterop.FreeLibrary(module); } catch { }
                    Log.Warn("加载 " + dll + " 失败: " + ex.Message);
                }
            }
            if (_ctx != null) _ctx.Log("未找到可用的 xinput DLL，手柄支持关闭");
        }

        private void ScanCore(CancellationToken ct, List<Peripheral> result)
        {
            for (uint i = 0; i < 4; i++)
            {
                if (ct.IsCancellationRequested) break;

                XInputInterop.XINPUT_STATE state;
                uint rc;
                try { rc = _getState(i, out state); }
                catch { continue; } // 个别驱动会抛，跳过该槽位
                if (rc != XInputInterop.ERROR_SUCCESS) continue; // 1167 = 未连接

                XInputInterop.XINPUT_BATTERY_INFORMATION battery;
                try { rc = _getBattery(i, XInputInterop.BATTERY_DEVTYPE_GAMEPAD, out battery); }
                catch { continue; }
                if (rc != XInputInterop.ERROR_SUCCESS) continue;

                if (battery.BatteryType == XInputInterop.BATTERY_TYPE_DISCONNECTED) continue;

                bool wired = battery.BatteryType == XInputInterop.BATTERY_TYPE_WIRED;
                string typeText = BatteryTypeText(battery.BatteryType);
                string levelText = BatteryLevelText(battery.BatteryLevel);
                int? percent = wired ? (int?)null : LevelToPercent(battery.BatteryLevel);

                var dev = new Peripheral();
                dev.Id = "xinput:" + i.ToString();
                dev.Name = "Xbox 手柄 #" + (i + 1).ToString();
                dev.Brand = "Microsoft";
                dev.Kind = DeviceKind.Gamepad;
                dev.Connection = wired ? "USB" : "Xbox 无线";
                dev.Percent = percent;
                dev.Provider = DisplayName;
                dev.DevicePath = "xinput://" + i.ToString();
                dev.Vid = 0x045E; // Microsoft
                dev.Pid = 0x0B00 + (int)i;
                dev.Signal = null;
                dev.Updated = DateTime.Now;
                if (wired)
                {
                    dev.State = BatteryState.Wired;
                    dev.Detail = "USB 有线连接（" + typeText + "），XInput 不提供电量";
                }
                else
                {
                    dev.State = percent.HasValue && percent.Value >= 100
                        ? BatteryState.Full
                        : (battery.BatteryLevel == XInputInterop.BATTERY_LEVEL_EMPTY
                            ? BatteryState.Critical
                            : (battery.BatteryLevel == XInputInterop.BATTERY_LEVEL_LOW
                                ? BatteryState.Low
                                : BatteryState.Discharging));
                    dev.Detail = "档位估算（XInput 仅提供 4 档，非真实百分比）：" + typeText + " / " + levelText;
                }
                result.Add(dev);
            }
        }

        /// <summary>BatteryLevel(0-3) → 估算百分比 5/25/50/100。固定映射，写入文档以免误读。</summary>
        private static int LevelToPercent(byte level)
        {
            switch (level)
            {
                case XInputInterop.BATTERY_LEVEL_EMPTY: return 5;
                case XInputInterop.BATTERY_LEVEL_LOW: return 25;
                case XInputInterop.BATTERY_LEVEL_MEDIUM: return 50;
                case XInputInterop.BATTERY_LEVEL_FULL: return 100;
                default: return 50;
            }
        }

        private static string BatteryTypeText(byte type)
        {
            switch (type)
            {
                case XInputInterop.BATTERY_TYPE_WIRED: return "有线";
                case XInputInterop.BATTERY_TYPE_ALKALINE: return "碱性电池";
                case XInputInterop.BATTERY_TYPE_NIMH: return "镍氢电池";
                case XInputInterop.BATTERY_TYPE_LITHIUM: return "锂电池（部分第三方手柄）";
                case XInputInterop.BATTERY_TYPE_UNKNOWN: return "未知电池";
                default: return "未知电池";
            }
        }

        private static string BatteryLevelText(byte level)
        {
            switch (level)
            {
                case XInputInterop.BATTERY_LEVEL_EMPTY: return "空";
                case XInputInterop.BATTERY_LEVEL_LOW: return "低";
                case XInputInterop.BATTERY_LEVEL_MEDIUM: return "中";
                case XInputInterop.BATTERY_LEVEL_FULL: return "满";
                default: return "未知";
            }
        }
    }
}
