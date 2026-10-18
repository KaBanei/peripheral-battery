using System;
using System.Collections.Generic;
using System.Threading;
using Pb.Core.Hid;

namespace Pb.Core.Providers
{
    /// <summary>
    /// VGN 2.4G 鼠标电量（实验）：使用 VGN VHUB 的 WebHID 私有命令。
    /// 控制接口 usage FF02:0002 / output report 8；先发 PCDriverStatus 握手，再查 DeviceOnLine / BatteryLevel。
    /// 实测 VGN F1 系接收器（3554:F503）可返回百分比、充电状态与电压。
    /// </summary>
    public sealed class VgnBatteryProvider : IProvider
    {
        private readonly AppConfig _config;
        private readonly IAppContext _ctx;
        private const byte ReportId = 8;

        public VgnBatteryProvider(AppConfig config, IAppContext ctx)
        {
            _config = config;
            _ctx = ctx;
        }

        public string Key { get { return "vgn"; } }
        public string DisplayName { get { return "VGN 2.4G（实验）"; } }
        public bool Enabled { get { return true; } }

        public List<Peripheral> Scan(CancellationToken ct)
        {
            var result = new List<Peripheral>();
            try
            {
                var bus = new HidBus(delegate(string m)
                {
                    if (_config.VerboseLog) _ctx.Log("[vgn] " + m);
                });
                List<HidInterface> all = bus.Enumerate();
                for (int i = 0; i < all.Count; i++)
                {
                    if (ct.IsCancellationRequested) break;
                    HidInterface it = all[i];
                    if (it.Vid != 0x3554 || it.Pid != 0xF503) continue;
                    if (it.UsagePage != 0xFF02 || it.OutputReportLen < 16) continue;
                    Peripheral p = TryReadMouse(bus, it, ct);
                    if (p != null) result.Add(p);
                }
            }
            catch (Exception ex)
            {
                _ctx.Log("[vgn] 扫描失败: " + ex.Message);
            }
            return result;
        }

        private Peripheral TryReadMouse(IHidBus bus, HidInterface iface, CancellationToken ct)
        {
            try
            {
                using (IHidChannel ch = bus.Open(iface))
                {
                    // 1) 驱动在线握手（VHUB 在连接后先发 PCDriverStatus=2 + 1）
                    SendMouse(ch, 2, new byte[] { 1 });
                    Thread.Sleep(30);
                    // 2) 查询设备在线
                    SendMouse(ch, 3, null);
                    Thread.Sleep(30);
                    // 3) 查询电量
                    SendMouse(ch, 4, null);

                    int? level = null;
                    int? millivolts = null;
                    bool charging = false;
                    DateTime deadline = DateTime.UtcNow.AddMilliseconds(1500);
                    while (DateTime.UtcNow < deadline && !ct.IsCancellationRequested)
                    {
                        byte[] r = ch.Read(250);
                        if (r == null) continue;
                        if (r.Length > 9 && r[1] == 4)
                        {
                            level = r[6];
                            charging = r[7] == 1;
                            millivolts = (r[8] << 8) | r[9];
                            break;
                        }
                    }
                    if (!level.HasValue || level.Value < 0 || level.Value > 100) return null;

                    var p = new Peripheral();
                    p.Id = "vgn:3554:f503";
                    p.PhysicalId = p.Id;
                    p.Name = "VGN 鼠标";
                    p.Brand = "VGN";
                    p.Kind = DeviceKind.Mouse;
                    p.Connection = "2.4G";
                    p.Percent = level;
                    p.MilliVolts = millivolts;
                    p.State = charging ? BatteryState.Charging : BatteryState.Discharging;
                    p.Provider = DisplayName;
                    p.Detail = "VGN 私有协议（实验）" + (string.IsNullOrEmpty(iface.Product) ? "" : " · " + iface.Product);
                    p.DevicePath = iface.Path;
                    p.Vid = iface.Vid;
                    p.Pid = iface.Pid;
                    p.Updated = DateTime.Now;
                    return p;
                }
            }
            catch (Exception ex)
            {
                if (_config.VerboseLog) _ctx.Log("[vgn] 读取失败: " + ex.Message);
                return null;
            }
        }

        private static void SendMouse(IHidChannel ch, byte cmd, byte[] value)
        {
            byte[] data = new byte[16];
            data[0] = cmd;
            if (value != null && value.Length > 0)
            {
                data[4] = (byte)value.Length;
                for (int i = 0; i < value.Length && 5 + i < 15; i++) data[5 + i] = value[i];
            }
            int sum = 0;
            for (int i = 0; i < 15; i++) sum += data[i];
            int crc = (85 - (sum & 0xFF)) & 0xFF;
            data[15] = (byte)((crc - ReportId) & 0xFF);
            byte[] report = new byte[17];
            report[0] = ReportId;
            Array.Copy(data, 0, report, 1, 16);
            ch.Write(report);
        }

        public void Dispose()
        {
        }
    }
}
