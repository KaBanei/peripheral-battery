using System;
using System.Collections.Generic;

namespace Pb.Core
{
    public enum BatteryState
    {
        Unknown = 0,
        Discharging,
        Charging,
        Full,
        Low,
        Critical,
        Wired
    }

    public enum DeviceKind
    {
        Unknown = 0,
        Mouse,
        Keyboard,
        Headset,
        Gamepad,
        Presenter,
        Numpad,
        Other
    }

    /// <summary>一个可展示的外设（鼠标/键盘/耳机/手柄等）。</summary>
    public sealed class Peripheral
    {
        public string Id { get; set; }
        public string Name { get; set; }
        public string Brand { get; set; }
        public DeviceKind Kind { get; set; }
        /// <summary>2.4G / 蓝牙 / USB / 虚拟</summary>
        public string Connection { get; set; }
        public int? Percent { get; set; }
        public int? MilliVolts { get; set; }
        public BatteryState State { get; set; }
        public int? Signal { get; set; }
        public string Provider { get; set; }
        public string Detail { get; set; }
        public string DevicePath { get; set; }
        public int Vid { get; set; }
        public int Pid { get; set; }
        public DateTime Updated { get; set; }
        public bool Simulated { get; set; }
        /// <summary>true = 仅识别到设备但读不到电量（通用外设兜底）。</summary>
        public bool IsGeneric { get; set; }
        /// <summary>物理设备标识（接收器/实例），用于跨数据源去重。</summary>
        public string PhysicalId { get; set; }

        public string MergeKey
        {
            get
            {
                string b = string.IsNullOrEmpty(Brand) ? "" : Brand;
                string n = string.IsNullOrEmpty(Name) ? ("0x" + Vid.ToString("X4") + ":0x" + Pid.ToString("X4")) : Name;
                return (b + "|" + n + "|" + Kind).ToLowerInvariant();
            }
        }

        public string KindText
        {
            get
            {
                switch (Kind)
                {
                    case DeviceKind.Mouse: return "鼠标";
                    case DeviceKind.Keyboard: return "键盘";
                    case DeviceKind.Headset: return "耳机";
                    case DeviceKind.Gamepad: return "手柄";
                    case DeviceKind.Presenter: return "演示器";
                    case DeviceKind.Numpad: return "数字键盘";
                    case DeviceKind.Other: return "外设";
                    default: return "设备";
                }
            }
        }

        public Peripheral Clone()
        {
            return (Peripheral)MemberwiseClone();
        }
    }

    public sealed class ProviderStatus
    {
        public string Key { get; set; }
        public string DisplayName { get; set; }
        public bool Enabled { get; set; }
        public bool Ok { get; set; }
        public string Error { get; set; }
        public int Count { get; set; }
        public long DurationMs { get; set; }
        public DateTime At { get; set; }
    }

    public sealed class HostUpdate : EventArgs
    {
        public List<Peripheral> Devices { get; set; }
        public List<ProviderStatus> Providers { get; set; }
        public DateTime At { get; set; }
    }

    /// <summary>电量来源插件。实现方必须线程安全地被后台线程调用。</summary>
    public interface IProvider : IDisposable
    {
        string Key { get; }
        string DisplayName { get; }
        bool Enabled { get; }
        /// <summary>执行一次扫描，返回发现的设备。允许抛异常，宿主会记录并继续。</summary>
        List<Peripheral> Scan(System.Threading.CancellationToken ct);
    }

    // ---------- HID 抽象（由 Core.Hid 实现） ----------

    public sealed class HidUsageRange
    {
        public ushort UsagePage { get; set; }
        public ushort UsageMin { get; set; }
        public ushort UsageMax { get; set; }
        public byte ReportId { get; set; }
        public bool IsRange { get; set; }
        public ushort BitSize { get; set; }
        public int LogicalMin { get; set; }
        public int LogicalMax { get; set; }
    }

    /// <summary>一个 HID 顶层集合（一个接口路径）。</summary>
    public sealed class HidInterface
    {
        public string Path { get; set; }
        public int Vid { get; set; }
        public int Pid { get; set; }
        public int Version { get; set; }
        public string Product { get; set; }
        public string Manufacturer { get; set; }
        public string Serial { get; set; }
        public string InstanceId { get; set; }
        public string DeviceDesc { get; set; }
        public string Service { get; set; }
        public ushort UsagePage { get; set; }
        public ushort Usage { get; set; }
        public int InputReportLen { get; set; }
        public int OutputReportLen { get; set; }
        public int FeatureReportLen { get; set; }
        public List<HidUsageRange> FeatureUsages { get; set; }
        public List<HidUsageRange> InputUsages { get; set; }

        public HidInterface()
        {
            FeatureUsages = new List<HidUsageRange>();
            InputUsages = new List<HidUsageRange>();
        }

        public bool IsVendorDefined { get { return UsagePage == 0xFF00; } }

        public bool HasFeatureUsage(ushort page, ushort usage)
        {
            foreach (var u in FeatureUsages)
                if (u.UsagePage == page && (u.UsageMin == usage || (u.IsRange && usage >= u.UsageMin && usage <= u.UsageMax)))
                    return true;
            return false;
        }

        public string DisplayName
        {
            get
            {
                if (!string.IsNullOrEmpty(Product)) return Product;
                if (!string.IsNullOrEmpty(DeviceDesc)) return DeviceDesc;
                return "HID 设备";
            }
        }
    }

    /// <summary>已打开的 HID 通道。实现负责 overlapped I/O。</summary>
    public interface IHidChannel : IDisposable
    {
        HidInterface Info { get; }
        /// <summary>写入一个输出报告（首字节为报告 ID）。返回是否成功。</summary>
        bool Write(byte[] report);
        /// <summary>读取一个输入报告，超时返回 null。</summary>
        byte[] Read(int timeoutMs);
        byte[] GetFeature(byte reportId, int length);
        bool SetFeature(byte[] report);
    }

    public interface IHidBus
    {
        List<HidInterface> Enumerate();
        IHidChannel Open(HidInterface iface);
    }

    public interface IAppContext
    {
        AppConfig Config { get; }
        void Log(string message);
    }
}