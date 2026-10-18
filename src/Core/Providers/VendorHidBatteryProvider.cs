using System;
using System.Collections.Generic;
using System.Threading;
using Pb.Core.Hid;

namespace Pb.Core.Providers
{
    /// <summary>
    /// 通用“厂商私有协议”Provider：按数据表（VendorProtocolRegistry）匹配 HID 控制集合，
    /// 依次尝试握手 + 查询命令，解析电量。新增品牌只需往表里加一条协议，不用改这里。
    /// </summary>
    public sealed class VendorHidBatteryProvider : IProvider
    {
        private readonly AppConfig _config;
        private readonly IAppContext _ctx;

        public VendorHidBatteryProvider(AppConfig config, IAppContext ctx)
        {
            _config = config;
            _ctx = ctx;
        }

        public string Key { get { return "vendor"; } }
        public string DisplayName { get { return "厂商私有协议（实验）"; } }
        public bool Enabled { get { return true; } }

        public List<Peripheral> Scan(CancellationToken ct)
        {
            var result = new List<Peripheral>();
            try
            {
                var bus = new HidBus(delegate(string m)
                {
                    if (_config.VerboseLog) _ctx.Log("[vendor] " + m);
                });
                List<HidInterface> all = bus.Enumerate();
                var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                for (int i = 0; i < all.Count; i++)
                {
                    if (ct.IsCancellationRequested) break;
                    HidInterface it = all[i];
                    for (int k = 0; k < VendorProtocolRegistry.All.Length; k++)
                    {
                        VendorBatteryProtocol proto = VendorProtocolRegistry.All[k];
                        if (!proto.Matches(it)) continue;
                        if (seen.Contains(proto.Key)) continue;
                        Peripheral p = TryRead(bus, it, proto, ct);
                        if (p != null)
                        {
                            seen.Add(proto.Key);
                            result.Add(p);
                            break;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                _ctx.Log("[vendor] 扫描失败: " + ex.Message);
            }
            return result;
        }

        private Peripheral TryRead(IHidBus bus, HidInterface iface, VendorBatteryProtocol proto, CancellationToken ct)
        {
            try
            {
                using (IHidChannel ch = bus.Open(iface))
                {
                    if (proto.PreCommands != null)
                    {
                        for (int i = 0; i < proto.PreCommands.Length; i++)
                        {
                            Send(ch, proto.Transport, proto.PreCommands[i]());
                            Thread.Sleep(25);
                        }
                    }
                    Send(ch, proto.Transport, proto.Command());

                    DateTime deadline = DateTime.UtcNow.AddMilliseconds(1200);
                    while (DateTime.UtcNow < deadline && !ct.IsCancellationRequested)
                    {
                        byte[] r = ch.Read(250);
                        if (r == null) continue;
                        VendorReading reading = proto.Parse(r);
                        if (reading == null) continue;

                        var p = new Peripheral();
                        p.Id = "vendor:" + proto.Key;
                        p.PhysicalId = p.Id;
                        p.Name = proto.Model;
                        p.Brand = proto.Vendor;
                        p.Kind = proto.Kind;
                        p.Connection = "2.4G";
                        p.Percent = reading.Level;
                        p.MilliVolts = reading.MilliVolts;
                        p.State = reading.Charging ? BatteryState.Charging : BatteryState.Discharging;
                        p.Provider = DisplayName;
                        p.Detail = proto.Vendor + " 私有协议（实验）";
                        p.DevicePath = iface.Path;
                        p.Vid = iface.Vid;
                        p.Pid = iface.Pid;
                        p.Updated = DateTime.Now;
                        return p;
                    }
                }
            }
            catch (Exception ex)
            {
                if (_config.VerboseLog) _ctx.Log("[vendor] " + proto.Key + " 读取失败: " + ex.Message);
            }
            return null;
        }

        private static void Send(IHidChannel ch, VendorTransport transport, byte[] report)
        {
            if (report == null || report.Length == 0) return;
            if (transport == VendorTransport.Interrupt) ch.WriteInterrupt(report);
            else ch.Write(report);
        }

        public void Dispose()
        {
        }
    }
}
