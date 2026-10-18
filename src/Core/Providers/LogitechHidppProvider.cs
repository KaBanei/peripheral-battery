using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;
using System.Threading;
using Pb.Core.Hid;

namespace Pb.Core.Providers
{
    /// <summary>
    /// 罗技 HID++ 电量：枚举 VID=046D、UsagePage=0xFF00 / Usage=0x0001 的厂商集合（Unifying / Bolt /
    /// Lightspeed 接收器，也覆盖直连的罗技设备），逐个设备索引 ping，再读 0x1004 / 0x1000 / 0x1001，
    /// 老设备回退 HID++ 1.0 寄存器 0x07 / 0x0D。
    /// 无接收器时立即返回空列表；单设备失败只跳过该设备；整轮有耗时上限。
    /// </summary>
    public sealed class LogitechHidppProvider : IProvider
    {
        public const int LogitechVid = 0x046D;
        private const ushort VendorUsagePage = 0xFF00;
        private const ushort VendorUsage = 0x0001;

        private const int MinOutputReportLen = 7;   // 短报文下限；>= 20 的长报文集合优先
        private const int FullReportLen = 20;
        private const int ProbeTimeoutMs = 260;
        private const int RequestTimeoutMs = 300;
        private const int ScanBudgetMs = 8000;
        private const int ReprobleEveryRounds = 5;
        private const int MaxDeviceIndex = 8;
        private const int DirectDeviceIndex = 0xFF;
        private const int FeatureCacheMinutes = 10;

        private sealed class DeviceState
        {
            public int Index;
            public string Name;
            public DeviceKind Kind;
            public int DeviceType = -1;
            public int NameFeature;
            public int UnifiedFeature;
            public int StatusFeature;
            public int VoltageFeature;
            public bool FeaturesKnown;
            public DateTime FeatureExpiry = DateTime.MinValue;
        }

        private sealed class ReceiverState
        {
            public string Path;
            public HidInterface Interface;
            public List<int> Connected = new List<int>();
            public Dictionary<int, DeviceState> Devices = new Dictionary<int, DeviceState>();
            public int RoundsSinceProbe = int.MaxValue;
        }

        private readonly AppConfig _config;
        private readonly IAppContext _ctx;
        private readonly Action<string> _log;
        private readonly object _sync = new object();
        private readonly Dictionary<string, ReceiverState> _receivers =
            new Dictionary<string, ReceiverState>(StringComparer.OrdinalIgnoreCase);
        private readonly HashSet<string> _openFailures = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        public LogitechHidppProvider(AppConfig config, IAppContext ctx)
        {
            _config = config;
            _ctx = ctx;
            _log = (config != null && config.VerboseLog) ? new Action<string>(Verbose) : null;
        }

        public string Key
        {
            get { return "logitech"; }
        }

        public string DisplayName
        {
            get { return "罗技 HID++"; }
        }

        public bool Enabled
        {
            get { return _config != null && _config.EnableLogitech; }
        }

        public List<Peripheral> Scan(CancellationToken ct)
        {
            var found = new List<Peripheral>();
            if (!Enabled) return found;

            var clock = Stopwatch.StartNew();
            IHidBus bus = HidBus.Current(_log);
            List<HidInterface> interfaces;
            try
            {
                interfaces = bus.Enumerate();
            }
            catch (Exception ex)
            {
                if (ErrorPolicy.IsFatal(ex)) throw;
                Warn("HID 枚举失败: " + ex.Message);
                return found;
            }

            PruneStaleReceivers(interfaces);
            List<HidInterface> candidates = SelectCandidates(interfaces);
            if (candidates.Count == 0)
            {
                Verbose("未发现罗技 HID++ 厂商集合（无接收器）");
                return found;
            }

            foreach (HidInterface iface in candidates)
            {
                if (ct.IsCancellationRequested) break;
                if (clock.ElapsedMilliseconds > ScanBudgetMs)
                {
                    Warn("罗技 HID++ 扫描超出 " + ScanBudgetMs + "ms 预算，剩余接收器留待下一轮");
                    break;
                }
                try
                {
                    ScanOne(bus, iface, found, clock, ct);
                }
                catch (Exception ex)
                {
                    if (ErrorPolicy.IsFatal(ex)) throw;
                    Warn("扫描罗技接收器失败（已跳过）: " + ex.Message);
                }
            }
            return found;
        }

        /// <summary>筛选罗技厂商集合；同一设备实例只保留输出报告最长的那个集合（优先 20 字节长报文）。</summary>
        private static List<HidInterface> SelectCandidates(List<HidInterface> interfaces)
        {
            var byInstance = new Dictionary<string, HidInterface>(StringComparer.OrdinalIgnoreCase);
            var order = new List<string>();
            foreach (HidInterface iface in interfaces)
            {
                if (iface.Vid != LogitechVid) continue;
                if (iface.UsagePage != VendorUsagePage || iface.Usage != VendorUsage) continue;
                if (iface.OutputReportLen < MinOutputReportLen) continue;

                string key = string.IsNullOrEmpty(iface.InstanceId) ? iface.Path : iface.InstanceId;
                HidInterface existing;
                if (!byInstance.TryGetValue(key, out existing))
                {
                    byInstance[key] = iface;
                    order.Add(key);
                }
                else if (iface.OutputReportLen > existing.OutputReportLen)
                {
                    byInstance[key] = iface;
                }
            }

            var list = new List<HidInterface>();
            foreach (string key in order) list.Add(byInstance[key]);
            list.Sort(delegate(HidInterface a, HidInterface b)
            {
                bool aLong = a.OutputReportLen >= FullReportLen;
                bool bLong = b.OutputReportLen >= FullReportLen;
                if (aLong != bLong) return aLong ? -1 : 1;
                return b.OutputReportLen.CompareTo(a.OutputReportLen);
            });
            return list;
        }

        private void PruneStaleReceivers(List<HidInterface> interfaces)
        {
            var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (HidInterface iface in interfaces) paths.Add(iface.Path);

            lock (_sync)
            {
                var stale = new List<string>();
                foreach (KeyValuePair<string, ReceiverState> pair in _receivers)
                    if (!paths.Contains(pair.Key)) stale.Add(pair.Key);
                foreach (string key in stale) _receivers.Remove(key);
            }
        }

        private void ScanOne(IHidBus bus, HidInterface iface, List<Peripheral> found, Stopwatch clock,
            CancellationToken ct)
        {
            ReceiverState state;
            lock (_sync)
            {
                if (!_receivers.TryGetValue(iface.Path, out state))
                {
                    state = new ReceiverState();
                    state.Path = iface.Path;
                    state.RoundsSinceProbe = int.MaxValue;
                    _receivers[iface.Path] = state;
                }
                state.Interface = iface;
                if (state.RoundsSinceProbe < int.MaxValue) state.RoundsSinceProbe++;
            }

            bool fullProbe;
            lock (_sync) fullProbe = state.RoundsSinceProbe >= ReprobleEveryRounds || state.Connected.Count == 0;

            if (iface.OutputReportLen < FullReportLen)
                Verbose("厂商集合输出报告仅 " + iface.OutputReportLen + " 字节，HID++ 只能用短报文");

            IHidChannel channel = null;
            try
            {
                channel = bus.Open(iface);
            }
            catch (Exception ex)
            {
                if (ErrorPolicy.IsFatal(ex)) throw;
                // 独占/权限问题每 30 秒都会复现，只在首次记录，避免刷日志
                bool first;
                lock (_sync) first = _openFailures.Add(iface.Path);
                if (first) Warn("打开罗技接收器失败（可能被独占，后续不再重复记录）: " + ex.Message);
                else Verbose("打开罗技接收器仍失败: " + iface.Path);
                return;
            }

            try
            {
                var session = new HidppSession(channel, _log);

                if (fullProbe)
                {
                    List<int> connected = ProbeDevices(session, clock, ct);
                    lock (_sync)
                    {
                        var alive = new Dictionary<int, DeviceState>();
                        foreach (int index in connected)
                        {
                            DeviceState device;
                            if (state.Devices.TryGetValue(index, out device)) alive[index] = device;
                        }
                        state.Connected = connected;
                        state.Devices = alive;
                        state.RoundsSinceProbe = 0;
                    }
                    Verbose("接收器 " + iface.DisplayName + " 在线索引: "
                            + (connected.Count == 0 ? "（无）" : Join(connected)));
                }

                List<int> indexes;
                lock (_sync) indexes = new List<int>(state.Connected);

                foreach (int index in indexes)
                {
                    if (ct.IsCancellationRequested) break;
                    if (clock.ElapsedMilliseconds > ScanBudgetMs) break;

                    DeviceState device;
                    lock (_sync)
                    {
                        if (!state.Devices.TryGetValue(index, out device))
                        {
                            device = new DeviceState();
                            device.Index = index;
                            state.Devices[index] = device;
                        }
                    }

                    Peripheral peripheral = ReadOne(session, state, device);
                    if (peripheral != null) found.Add(peripheral);
                }
            }
            finally
            {
                channel.Dispose();
            }
        }

        private static List<int> ProbeDevices(HidppSession session, Stopwatch clock, CancellationToken ct)
        {
            var connected = new List<int>();
            // 直连罗技设备用 0xFF 索引；接收器本身也可能响应，没有电量特性的会在 ReadOne 里被跳过
            if (session.Ping(DirectDeviceIndex, ProbeTimeoutMs)) connected.Add(DirectDeviceIndex);

            for (int index = 1; index <= MaxDeviceIndex; index++)
            {
                if (ct.IsCancellationRequested) break;
                if (clock.ElapsedMilliseconds > ScanBudgetMs) break;
                if (session.Ping(index, ProbeTimeoutMs)) connected.Add(index);
            }
            return connected;
        }

        private Peripheral ReadOne(HidppSession session, ReceiverState receiver, DeviceState device)
        {
            bool needFeatures = !device.FeaturesKnown || DateTime.UtcNow > device.FeatureExpiry;
            if (needFeatures) QueryFeatures(session, device);

            BatteryReading battery = session.ReadBattery(device.Index, device.UnifiedFeature,
                device.StatusFeature, device.VoltageFeature, RequestTimeoutMs);

            if (battery == null || !battery.HasData)
            {
                Verbose("罗技设备 #" + device.Index + " 未取到电量，跳过"
                        + (device.Name != null ? "（" + device.Name + "）" : ""));
                return null;
            }

            if (device.Kind == DeviceKind.Unknown)
                device.Kind = Hidpp.GuessKindFromName(device.Name);

            var p = new Peripheral();
            p.Id = "logi:" + receiver.Path + ":" + device.Index;
            p.Name = string.IsNullOrEmpty(device.Name) ? ("罗技外设 #" + device.Index) : device.Name;
            p.Brand = "罗技";
            p.Kind = device.Kind;
            p.Connection = device.Index == DirectDeviceIndex ? "USB" : "2.4G";
            p.Percent = battery.Percent;
            p.MilliVolts = battery.MilliVolts;
            p.State = battery.State;
            p.Provider = DisplayName;
            p.DevicePath = receiver.Path;
            p.Vid = LogitechVid;
            p.Pid = receiver.Interface != null ? receiver.Interface.Pid : 0;
            p.Updated = DateTime.Now;

            var detail = new StringBuilder();
            detail.Append("HID++ ").Append(battery.Source);
            if (device.DeviceType >= 0) detail.Append(" · 类型字节 ").Append(device.DeviceType);
            detail.Append(" · 设备索引 ").Append(device.Index);
            if (battery.Note != null) detail.Append(" · ").Append(battery.Note);
            if (receiver.Interface != null && !string.IsNullOrEmpty(receiver.Interface.Product))
                detail.Append(" · 接收器 ").Append(receiver.Interface.Product);
            p.Detail = detail.ToString();
            return p;
        }

        private void QueryFeatures(HidppSession session, DeviceState device)
        {
            device.NameFeature = session.GetFeatureIndex(device.Index, Hidpp.FeatureDeviceName, RequestTimeoutMs);
            device.UnifiedFeature = session.GetFeatureIndex(device.Index, Hidpp.FeatureUnifiedBattery,
                RequestTimeoutMs);
            device.StatusFeature = session.GetFeatureIndex(device.Index, Hidpp.FeatureBatteryStatus,
                RequestTimeoutMs);
            device.VoltageFeature = session.GetFeatureIndex(device.Index, Hidpp.FeatureBatteryVoltage,
                RequestTimeoutMs);

            if (device.NameFeature > 0)
            {
                string name = session.GetDeviceName(device.Index, device.NameFeature, RequestTimeoutMs);
                if (!string.IsNullOrEmpty(name)) device.Name = name;

                int type = session.GetDeviceType(device.Index, device.NameFeature, RequestTimeoutMs);
                if (type >= 0)
                {
                    device.DeviceType = type;
                    device.Kind = Hidpp.MapDeviceType(type);
                }
            }

            device.FeaturesKnown = true;

            bool complete = device.NameFeature >= 0 && device.UnifiedFeature >= 0
                            && device.StatusFeature >= 0 && device.VoltageFeature >= 0;
            bool hasBattery = device.UnifiedFeature > 0 || device.StatusFeature > 0 || device.VoltageFeature > 0;
            device.FeatureExpiry = DateTime.UtcNow.AddMinutes(complete && hasBattery ? FeatureCacheMinutes : 1);

            Verbose("罗技设备 #" + device.Index + " 特性 name=" + device.NameFeature
                    + " 0x1004=" + device.UnifiedFeature + " 0x1000=" + device.StatusFeature
                    + " 0x1001=" + device.VoltageFeature
                    + (device.Name != null ? " 名称=" + device.Name : ""));
        }

        private static string Join(List<int> values)
        {
            var sb = new StringBuilder();
            for (int i = 0; i < values.Count; i++)
            {
                if (i > 0) sb.Append(',');
                sb.Append(values[i] == DirectDeviceIndex ? "0xFF(直连)" : values[i].ToString());
            }
            return sb.ToString();
        }

        private void Verbose(string message)
        {
            if (_ctx == null) return;
            try { _ctx.Log(message); }
            catch { }
        }

        private void Warn(string message)
        {
            if (_ctx == null) return;
            try { _ctx.Log("罗技 HID++: " + message); }
            catch { }
        }

        public void Dispose()
        {
            lock (_sync)
            {
                _receivers.Clear();
                _openFailures.Clear();
            }
        }
    }
}
