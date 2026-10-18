using System;
using System.Collections.Generic;
using System.Threading;
using Pb.Core.Hid;

namespace Pb.Core.Providers
{
    /// <summary>
    /// 标准 HID 电量：HID Usage Page 0x06 (Generic Device Controls) / Usage 0x20 (Battery Strength)。
    /// 读取 Feature report 的数值，按该 usage 的 LogicalMin/LogicalMax 换算成 0-100。
    /// 只返回真正读到电量的设备；读不到就跳过（绝不伪造数值）。
    /// </summary>
    public sealed class StandardHidBatteryProvider : IProvider
    {
        private const ushort BatteryUsagePage = 0x0006;
        private const ushort BatteryStrengthUsage = 0x0020;

        private readonly AppConfig _config;
        private readonly IAppContext _ctx;
        private readonly Action<string> _log;

        public StandardHidBatteryProvider(AppConfig config, IAppContext ctx)
        {
            _config = config;
            _ctx = ctx;
            _log = (config != null && config.VerboseLog) ? new Action<string>(Verbose) : null;
        }

        public string Key
        {
            get { return "hid"; }
        }

        public string DisplayName
        {
            get { return "标准 HID 电量"; }
        }

        public bool Enabled
        {
            get { return _config != null && _config.EnableStandardHid; }
        }

        public List<Peripheral> Scan(CancellationToken ct)
        {
            var found = new List<Peripheral>();
            if (!Enabled) return found;

            IHidBus bus = HidBus.Current(_log);
            List<HidInterface> interfaces = bus.Enumerate();
            if (interfaces.Count == 0) return found;

            Dictionary<string, DeviceKind> kindHints = BuildSiblingKindHints(interfaces);

            foreach (HidInterface iface in interfaces)
            {
                if (ct.IsCancellationRequested) break;
                if (!iface.HasFeatureUsage(BatteryUsagePage, BatteryStrengthUsage)) continue;

                IHidChannel channel = null;
                try
                {
                    channel = bus.Open(iface);
                    var reader = channel as IHidUsageReader;
                    if (reader == null)
                    {
                        Verbose("通道不支持 usage 读取，跳过: " + iface.Path);
                        continue;
                    }

                    int rawValue;
                    HidUsageRange caps;
                    if (!reader.TryReadUsageValue(BatteryUsagePage, BatteryStrengthUsage, out rawValue, out caps))
                    {
                        Verbose("未读到 0x06:0x20 电量（feature report 读取失败）: " + iface.DisplayName);
                        continue;
                    }

                    int? percent = ToPercent(rawValue, caps);
                    if (!percent.HasValue)
                    {
                        Verbose("电量原始值无法换算: raw=" + rawValue + " " + iface.DisplayName);
                        continue;
                    }

                    found.Add(Build(iface, percent.Value, rawValue, caps, kindHints));
                    Verbose("标准 HID 电量: " + iface.DisplayName + " = " + percent.Value + "% (raw " + rawValue + ")");
                }
                catch (Exception ex)
                {
                    if (ErrorPolicy.IsFatal(ex)) throw;
                    // 单个设备失败不影响整体扫描
                    Verbose("读取标准 HID 电量失败（已跳过）: " + ex.Message);
                }
                finally
                {
                    if (channel != null) channel.Dispose();
                }
            }
            return found;
        }

        private Peripheral Build(HidInterface iface, int percent, int rawValue, HidUsageRange caps,
            Dictionary<string, DeviceKind> kindHints)
        {
            var p = new Peripheral();
            string baseId = string.IsNullOrEmpty(iface.InstanceId) ? iface.Path : iface.InstanceId;
            p.Id = "hid:" + baseId + ":" + iface.UsagePage.ToString("X4") + ":" + iface.Usage.ToString("X4");
            p.Name = iface.DisplayName;
            p.Brand = iface.Manufacturer;
            p.Kind = InferKind(iface, kindHints);
            p.Connection = InferConnection(iface);
            p.Percent = percent;
            p.State = BatteryState.Unknown; // 标准 usage 只给强度，不给充放电状态
            p.Provider = DisplayName;
            p.DevicePath = iface.Path;
            p.Vid = iface.Vid;
            p.Pid = iface.Pid;
            p.Updated = DateTime.Now;

            string range = caps == null ? "?" : (caps.LogicalMin + ".." + caps.LogicalMax);
            string report = caps == null ? "" : (" reportId=" + caps.ReportId + " bits=" + caps.BitSize);
            p.Detail = "HID 0x06:0x20 raw=" + rawValue + " logical=" + range + report;
            if (!string.IsNullOrEmpty(iface.Serial)) p.Detail += " sn=" + iface.Serial;
            return p;
        }

        /// <summary>0..100 直接使用，0..255 除以 2.55，其它按 LogicalMin/LogicalMax 线性换算。</summary>
        public static int? ToPercent(int rawValue, HidUsageRange caps)
        {
            if (rawValue < 0) return null;
            if (caps == null) return rawValue <= 100 ? (int?)rawValue : null;

            int min = caps.LogicalMin;
            int max = caps.LogicalMax;
            if (max <= min) return rawValue <= 100 ? (int?)rawValue : null;

            if (min == 0 && max == 100) return Hidpp.ClampPercent(rawValue);
            if (min == 0 && max == 255) return Hidpp.ClampPercent((int)Math.Round(rawValue / 2.55));

            double percent = (rawValue - min) * 100.0 / (max - min);
            if (percent <= 0) return 0;
            if (percent >= 100) return 100;
            return (int)Math.Round(percent);
        }

        private static DeviceKind InferKind(HidInterface iface, Dictionary<string, DeviceKind> kindHints)
        {
            string name = (iface.Product == null ? "" : iface.Product) + " "
                          + (iface.DeviceDesc == null ? "" : iface.DeviceDesc) + " "
                          + (iface.Manufacturer == null ? "" : iface.Manufacturer);
            DeviceKind byName = Hidpp.GuessKindFromName(name);
            if (byName != DeviceKind.Unknown) return byName;

            DeviceKind hint;
            if (!string.IsNullOrEmpty(iface.InstanceId) && kindHints.TryGetValue(iface.InstanceId, out hint))
                return hint;

            return KindFromUsage(iface.UsagePage, iface.Usage);
        }

        /// <summary>同一 InstanceId 下其它 HID 顶层集合的 usage 用于推断类型（电量集合本身是 0x06:0x20）。</summary>
        private static Dictionary<string, DeviceKind> BuildSiblingKindHints(List<HidInterface> interfaces)
        {
            var map = new Dictionary<string, DeviceKind>(StringComparer.OrdinalIgnoreCase);
            foreach (HidInterface iface in interfaces)
            {
                if (string.IsNullOrEmpty(iface.InstanceId)) continue;
                DeviceKind kind = KindFromUsage(iface.UsagePage, iface.Usage);
                if (kind == DeviceKind.Unknown) continue;

                DeviceKind existing;
                if (!map.TryGetValue(iface.InstanceId, out existing) || Rank(kind) < Rank(existing))
                    map[iface.InstanceId] = kind;
            }
            return map;
        }

        private static int Rank(DeviceKind kind)
        {
            switch (kind)
            {
                case DeviceKind.Mouse: return 0;
                case DeviceKind.Keyboard: return 1;
                case DeviceKind.Gamepad: return 2;
                case DeviceKind.Numpad: return 3;
                case DeviceKind.Headset: return 4;
                default: return 9;
            }
        }

        private static DeviceKind KindFromUsage(ushort usagePage, ushort usage)
        {
            if (usagePage == 0x0001)
            {
                switch (usage)
                {
                    case 0x01: return DeviceKind.Other;      // Pointer
                    case 0x02: return DeviceKind.Mouse;
                    case 0x04: return DeviceKind.Gamepad;    // Joystick
                    case 0x05: return DeviceKind.Gamepad;
                    case 0x06: return DeviceKind.Keyboard;
                    case 0x07: return DeviceKind.Numpad;     // Keypad
                    default: return DeviceKind.Unknown;
                }
            }
            if (usagePage == 0x000C && usage == 0x0001) return DeviceKind.Headset; // Consumer Control
            return DeviceKind.Unknown;
        }

        private static string InferConnection(HidInterface iface)
        {
            string text = ((iface.InstanceId == null ? "" : iface.InstanceId) + " "
                           + (iface.Path == null ? "" : iface.Path)).ToLowerInvariant();
            if (text.Contains("00001124") || text.Contains("bthenum") || text.Contains("bthle")
                || text.Contains("bluetooth"))
                return "蓝牙";
            return "2.4G";
        }

        private void Verbose(string message)
        {
            if (_ctx == null) return;
            try { _ctx.Log(message); }
            catch { }
        }

        public void Dispose()
        {
        }
    }
}
