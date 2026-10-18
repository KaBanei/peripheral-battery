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
                    if (it.Vid == 0x3554 && it.Pid == 0xF503 && it.UsagePage == 0xFF02 && it.OutputReportLen >= 16)
                    {
                        Peripheral p = TryReadMouse(bus, it, ct);
                        if (p != null) result.Add(p);
                    }
                    else if (it.Vid == 0x320F && it.Pid == 0x5088 && it.UsagePage == 0xFF1C && it.OutputReportLen >= 60)
                    {
                        Peripheral p = TryReadKeyboard(bus, it, ct);
                        if (p != null) result.Add(p);
                    }
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

        /// <summary>
        /// 键盘（V98Pro V2）：COL04（FF1C:0092）/ report 4，命令 0x1A，数据 [0,0,0x1A,6,0,0,0]。
        /// 必须走中断 OUT 端点（WriteFile），用 HidD_SetOutputReport（控制端点）设备不会回应。
        /// 响应：[04 00 00 1A 06 00 00 00 47 ...]，第 8 字节为电量（0x47=71）。
        /// </summary>
        private Peripheral TryReadKeyboard(IHidBus bus, HidInterface iface, CancellationToken ct)
        {
            try
            {
                using (IHidChannel ch = bus.Open(iface))
                {
                    int len = iface.OutputReportLen >= 60 ? iface.OutputReportLen : 64;
                    byte[] buf = new byte[len];
                    buf[0] = 4;
                    buf[3] = 26;
                    buf[4] = 6;
                    if (!ch.WriteInterrupt(buf)) return null;

                    int? level = null;
                    bool charging = false;
                    DateTime deadline = DateTime.UtcNow.AddMilliseconds(1200);
                    while (DateTime.UtcNow < deadline && !ct.IsCancellationRequested)
                    {
                        byte[] r = ch.Read(250);
                        if (r == null) continue;
                        if (r.Length > 8 && r[3] == 26)
                        {
                            level = r[8];
                            charging = r[9] == 1;
                            break;
                        }
                    }
                    if (!level.HasValue || level.Value < 0 || level.Value > 100) return null;

                    var p = new Peripheral();
                    p.Id = "vgn:320f:5088";
                    p.PhysicalId = p.Id;
                    p.Name = "VGN V98Pro V2";
                    p.Brand = "VGN";
                    p.Kind = DeviceKind.Keyboard;
                    p.Connection = "2.4G";
                    p.Percent = level;
                    p.State = charging ? BatteryState.Charging : BatteryState.Discharging;
                    p.Provider = DisplayName;
                    p.Detail = "VGN 私有协议（实验）· " + (iface.Product ?? "V98Pro V2");
                    p.DevicePath = iface.Path;
                    p.Vid = iface.Vid;
                    p.Pid = iface.Pid;
                    p.Updated = DateTime.Now;
                    return p;
                }
            }
            catch (Exception ex)
            {
                if (_config.VerboseLog) _ctx.Log("[vgn] 键盘读取失败: " + ex.Message);
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