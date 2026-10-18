using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using Pb.Core.Platform;

namespace Pb.Core.Providers
{
    /// <summary>
    /// 蓝牙外设电量 Provider。
    ///
    /// 原理：Windows 蓝牙栈会把已连接设备的电量写入该设备 PnP 节点的设备属性
    /// （GUID 104EA319-6EE2-4701-BD47-8DDBF425BBE5，PID 2 = DEVPKEY_Device_BatteryLevel，
    /// DEVPROP_TYPE_BYTE，0-100）。因此这里用 SetupAPI 枚举全部设备节点，筛出
    /// InstanceId 以 BTHLE\ 或 BTHENUM\ 开头的蓝牙设备，再读友好名 + 电量。
    ///
    /// 该属性由蓝牙栈在设备连接后填充；未连接、驱动未上报、老式经典蓝牙外设
    /// 都可能读不到 —— 读不到就跳过，不猜测。
    /// </summary>
    public sealed class BluetoothBatteryProvider : IProvider
    {
        // DEVPKEY_Device_FriendlyName
        private static readonly Guid FriendlyNameFmtid = new Guid("a45c254e-df1c-4efd-8020-67d146a850e0");
        private const uint FriendlyNamePid = 14;
        // DEVPKEY_Device_DeviceDesc（很多蓝牙子节点没有 FriendlyName，只有 DeviceDesc）
        private static readonly Guid DeviceDescFmtid = new Guid("4340a6c5-93fa-4706-972c-7b648008a5a7");
        private const uint DeviceDescPid = 2;
        // DEVPKEY_Device_BusReportedDeviceDesc（设备自己上报的名字，最接近"型号"）
        private static readonly Guid BusReportedFmtid = new Guid("540b947e-8b40-45bc-a8a2-6a0b894cbda2");
        private const uint BusReportedPid = 4;

        // {104EA319-6EE2-4701-BD47-8DDBF425BBE5}（蓝牙/设备电量属性族）
        private static readonly Guid BatteryFmtid = new Guid("104ea319-6ee2-4701-bd47-8ddbf425bbe5");
        private const uint BatteryPidPrimary = 2;
        private const uint BatteryPidFallback = 4;

        private const int MaxDevices = 1024;

        // 名字里出现这些词的大多是适配器 / 本地服务 / 协议端点，不是外设本体
        private static readonly string[] ExcludedNameMarkers = new string[]
        {
            "enumerator", "adapter", "适配器", "radio", "无线电",
            "bluetooth device (", "generic attribute", "generic access",
            "avrcp", "a2dp", "hands-free", "handsfree", "audio sink", "audio source",
            "service", "服务", "rfcomm", "personal area network",
            "bluetooth le device", "gatt"
        };

        private readonly AppConfig _config;
        private readonly IAppContext _ctx;
        private readonly object _sync = new object();
        private volatile bool _disposed;
        private bool _warnedOnce;

        public BluetoothBatteryProvider(AppConfig cfg, IAppContext ctx)
        {
            _config = cfg;
            _ctx = ctx;
        }

        public string Key { get { return "bluetooth"; } }
        public string DisplayName { get { return "蓝牙设备"; } }
        public bool Enabled { get { return _config != null && _config.EnableBluetooth; } }

        public void Dispose() { _disposed = true; }

        public List<Peripheral> Scan(CancellationToken ct)
        {
            var result = new List<Peripheral>();
            if (_disposed || !Enabled) return result;

            lock (_sync)
            {
                try
                {
                    ScanCore(ct, result);
                }
                catch (Exception ex)
                {
                    // 约定：绝不把异常抛给宿主
                    Log.Warn("蓝牙电量扫描失败（已跳过）: " + ex.Message);
                }
            }
            return result;
        }

        private void ScanCore(CancellationToken ct, List<Peripheral> result)
        {
            IntPtr set = SetupApiInterop.SetupDiGetClassDevs(
                IntPtr.Zero, null, IntPtr.Zero,
                SetupApiInterop.DIGCF_PRESENT | SetupApiInterop.DIGCF_ALLCLASSES);
            if (set == IntPtr.Zero || set == new IntPtr(-1))
            {
                WarnOnce("SetupDiGetClassDevs 失败，无法枚举蓝牙设备");
                return;
            }

            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            try
            {
                int dataSize = Marshal.SizeOf(typeof(SetupApiInterop.SP_DEVINFO_DATA));
                for (uint i = 0; i < (uint)MaxDevices; i++)
                {
                    if (ct.IsCancellationRequested) break;

                    var data = new SetupApiInterop.SP_DEVINFO_DATA();
                    data.cbSize = dataSize;
                    if (!SetupApiInterop.SetupDiEnumDeviceInfo(set, i, ref data)) break;

                    string instanceId = SetupApiInterop.GetInstanceId(set, ref data);
                    if (string.IsNullOrEmpty(instanceId)) continue;
                    if (!IsBluetoothDeviceNode(instanceId)) continue;

                    string name = ReadName(set, ref data);
                    if (string.IsNullOrEmpty(name)) continue;
                    name = name.Trim();
                    if (name.Length == 0) continue;
                    if (IsIgnoredName(name)) continue;

                    int? percent = null;
                    int usedPid = 0;
                    percent = ReadBatteryPercent(set, ref data, BatteryPidPrimary);
                    if (percent.HasValue) usedPid = (int)BatteryPidPrimary;
                    if (!percent.HasValue)
                    {
                        percent = ReadBatteryPercent(set, ref data, BatteryPidFallback);
                        if (percent.HasValue) usedPid = (int)BatteryPidFallback;
                    }
                    if (!percent.HasValue) continue; // 读不到电量就跳过，不编造

                    DeviceKind kind = InferKind(name);
                    string brand = InferBrand(name);
                    string dedupeKey = name.ToLowerInvariant() + "|" + ((int)kind).ToString();
                    if (!seen.Add(dedupeKey)) continue;

                    int vid, pid;
                    ParseVidPid(instanceId, out vid, out pid);

                    int pct = percent.Value;
                    if (pct < 0) pct = 0;
                    if (pct > 100) pct = 100;

                    var dev = new Peripheral();
                    dev.Id = "bt:" + instanceId;
                    dev.Name = name;
                    dev.Brand = brand;
                    dev.Kind = kind;
                    dev.Connection = "蓝牙";
                    dev.Percent = pct;
                    dev.State = pct >= 100 ? BatteryState.Full : BatteryState.Discharging;
                    dev.Provider = DisplayName;
                    dev.Detail = "系统蓝牙电量属性（GUID 104EA319…, PID " + usedPid + "）";
                    dev.DevicePath = instanceId;
                    dev.Vid = vid;
                    dev.Pid = pid;
                    dev.Updated = DateTime.Now;
                    result.Add(dev);
                }
            }
            finally
            {
                try { SetupApiDestroy(set); } catch { }
            }
        }

        private static void SetupApiDestroy(IntPtr set)
        {
            SetupApiInterop.SetupDiDestroyDeviceInfoList(set);
        }

        /// <summary>设备名：FriendlyName → DeviceDesc → BusReportedDeviceDesc，取第一个非空。</summary>
        private static string ReadName(IntPtr set, ref SetupApiInterop.SP_DEVINFO_DATA data)
        {
            string name = SetupApiInterop.GetStringProperty(set, ref data, FriendlyNameFmtid, FriendlyNamePid);
            if (!string.IsNullOrEmpty(name)) return name;
            name = SetupApiInterop.GetStringProperty(set, ref data, DeviceDescFmtid, DeviceDescPid);
            if (!string.IsNullOrEmpty(name)) return name;
            return SetupApiInterop.GetStringProperty(set, ref data, BusReportedFmtid, BusReportedPid);
        }

        private static bool IsBluetoothDeviceNode(string instanceId)
        {
            if (instanceId.StartsWith("BTHLE\\", StringComparison.OrdinalIgnoreCase)) return true;
            if (instanceId.StartsWith("BTHENUM\\", StringComparison.OrdinalIgnoreCase))
            {
                // BTHENUM\DEV_xxx 是本机适配器枚举出来的本地服务对象，不是远端外设
                if (instanceId.StartsWith("BTHENUM\\DEV_", StringComparison.OrdinalIgnoreCase)) return false;
                return true;
            }
            return false;
        }

        private static bool IsIgnoredName(string name)
        {
            string lower = name.ToLowerInvariant();
            for (int i = 0; i < ExcludedNameMarkers.Length; i++)
            {
                if (lower.IndexOf(ExcludedNameMarkers[i], StringComparison.Ordinal) >= 0) return true;
            }
            // 只有一个 "Bluetooth" / "蓝牙" 字样的节点是适配器本体
            if (lower == "bluetooth" || lower == "蓝牙" || lower == "bluetooth le") return true;
            return false;
        }

        private static int? ReadBatteryPercent(IntPtr set, ref SetupApiInterop.SP_DEVINFO_DATA data, uint pid)
        {
            int? raw = SetupApiInterop.GetIntProperty(set, ref data, BatteryFmtid, pid);
            if (!raw.HasValue) return null;
            int v = raw.Value;
            // 只接受 0-100 的百分比；255/-1 之类是"未知"占位符
            if (v < 0 || v > 100) return null;
            return v;
        }

        private static DeviceKind InferKind(string name)
        {
            string n = name.ToLowerInvariant();
            if (ContainsAny(n, "gamepad", "controller", "手柄", "joystick", "xbox", "8bitdo", "八位堂"))
                return DeviceKind.Gamepad;
            if (ContainsAny(n, "headset", "headphone", "earbud", "耳机", "耳麦", "buds", "airpods",
                               "wh-1000", "speaker", "soundbar", "音箱"))
                return DeviceKind.Headset;
            if (ContainsAny(n, "keyboard", "键盘", "keychron", "kbd", "keys", "keypad"))
                return DeviceKind.Keyboard;
            if (ContainsAny(n, "mouse", "鼠标", "trackball", "trackpad", "pointer", "轨迹球"))
                return DeviceKind.Mouse;
            if (ContainsAny(n, "presenter", "clicker", "演示", "翻页"))
                return DeviceKind.Presenter;
            if (ContainsAny(n, "numpad", "numeric keypad", "数字键盘", "数字小键盘"))
                return DeviceKind.Numpad;
            return DeviceKind.Other;
        }

        private static bool ContainsAny(string haystack, params string[] needles)
        {
            for (int i = 0; i < needles.Length; i++)
            {
                if (haystack.IndexOf(needles[i], StringComparison.Ordinal) >= 0) return true;
            }
            return false;
        }

        private static string InferBrand(string name)
        {
            string n = name.ToLowerInvariant();
            if (ContainsAny(n, "logitech", "罗技", "logi ")) return "Logitech";
            if (ContainsAny(n, "microsoft", "微软", "surface")) return "Microsoft";
            if (ContainsAny(n, "razer", "雷蛇")) return "Razer";
            if (ContainsAny(n, "steelseries", "赛睿")) return "SteelSeries";
            if (ContainsAny(n, "corsair", "海盗船")) return "Corsair";
            if (ContainsAny(n, "keychron")) return "Keychron";
            if (ContainsAny(n, "vgn")) return "VGN";
            if (ContainsAny(n, "xiaomi", "小米", "redmi")) return "Xiaomi";
            if (ContainsAny(n, "huawei", "华为", "honor", "荣耀")) return "Huawei";
            if (ContainsAny(n, "apple", "airpods", "magic mouse", "magic keyboard")) return "Apple";
            if (ContainsAny(n, "sony", "wh-1000", "wf-1000")) return "Sony";
            if (ContainsAny(n, "jabra")) return "Jabra";
            if (ContainsAny(n, "bose")) return "Bose";
            if (ContainsAny(n, "jbl")) return "JBL";
            if (ContainsAny(n, "samsung", "三星", "galaxy")) return "Samsung";
            if (ContainsAny(n, "rapoo", "雷柏")) return "Rapoo";
            if (ContainsAny(n, "dell", "戴尔")) return "Dell";
            if (ContainsAny(n, "lenovo", "联想", "thinkpad")) return "Lenovo";
            if (ContainsAny(n, "8bitdo", "八位堂")) return "8BitDo";
            if (ContainsAny(n, "anker", "soundcore")) return "Anker";
            if (ContainsAny(n, "edifier", "漫步者")) return "Edifier";
            return null; // Brand 允许为空
        }

        /// <summary>
        /// 从蓝牙实例 ID 里尽力解析 VID/PID：
        /// 远端经典蓝牙服务形如 BTHENUM\{00001124-...}_VID&amp;0002045E_PID&amp;B023&amp;Col01\...
        /// 蓝牙 SIG 的 VID 带 4 位"厂商编号来源"前缀（0001=SIG, 0002=USB-IF），取低 16 位。
        /// 解析失败则返回 0（合法未知值）。
        /// </summary>
        private static void ParseVidPid(string instanceId, out int vid, out int pid)
        {
            vid = 0;
            pid = 0;
            try
            {
                int vi = instanceId.IndexOf("VID&", StringComparison.OrdinalIgnoreCase);
                if (vi >= 0)
                {
                    vi += 4;
                    int end = vi;
                    while (end < instanceId.Length && IsHex(instanceId[end])) end++;
                    if (end > vi)
                    {
                        string hex = instanceId.Substring(vi, end - vi);
                        if (hex.Length > 4) hex = hex.Substring(hex.Length - 4);
                        vid = Convert.ToInt32(hex, 16);
                    }
                }
                int pi = instanceId.IndexOf("PID&", StringComparison.OrdinalIgnoreCase);
                if (pi >= 0)
                {
                    pi += 4;
                    int end = pi;
                    while (end < instanceId.Length && IsHex(instanceId[end])) end++;
                    if (end > pi)
                    {
                        string hex = instanceId.Substring(pi, end - pi);
                        if (hex.Length > 4) hex = hex.Substring(hex.Length - 4);
                        pid = Convert.ToInt32(hex, 16);
                    }
                }
            }
            catch
            {
                vid = 0;
                pid = 0;
            }
        }

        private static bool IsHex(char c)
        {
            return (c >= '0' && c <= '9') || (c >= 'a' && c <= 'f') || (c >= 'A' && c <= 'F');
        }

        private void WarnOnce(string message)
        {
            if (_warnedOnce) return;
            _warnedOnce = true;
            try
            {
                if (_ctx != null) _ctx.Log(message);
                else Log.Warn(message);
            }
            catch { }
        }
    }
}
