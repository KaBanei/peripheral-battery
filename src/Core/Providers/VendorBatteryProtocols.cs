using System;
using Pb.Core.Hid;

namespace Pb.Core.Providers
{
    public enum VendorTransport
    {
        Control,    // HidD_SetOutputReport（控制端点）
        Interrupt   // WriteFile（中断 OUT 端点）
    }

    public sealed class VendorReading
    {
        public int Level { get; set; }
        public bool Charging { get; set; }
        public int? MilliVolts { get; set; }
    }

    /// <summary>
    /// 一家厂商/型号的 2.4G 私有电量协议描述（数据驱动）。
    /// 新增品牌只需要：抓包 → 填 Vid/Pid/usage/reportId/传输方式/命令/解析，不需要写新的 Provider。
    /// </summary>
    public sealed class VendorBatteryProtocol
    {
        public string Key { get; set; }
        public string Vendor { get; set; }
        public string Model { get; set; }
        public DeviceKind Kind { get; set; }
        public int Vid { get; set; }
        public int Pid { get; set; }
        public ushort UsagePage { get; set; }
        public ushort Usage { get; set; }
        /// <summary>该控制集合的 output report 总长度（含报告 ID）。</summary>
        public int MinReportLength { get; set; }
        public VendorTransport Transport { get; set; }
        /// <summary>正式查询前的握手命令（可能为空）。</summary>
        public Func<byte[]>[] PreCommands { get; set; }
        /// <summary>电量查询命令（完整报告，首字节为报告 ID）。</summary>
        public Func<byte[]> Command { get; set; }
        /// <summary>解析响应；返回 null 表示这不是有效电量回包。</summary>
        public Func<byte[], VendorReading> Parse { get; set; }

        public bool Matches(HidInterface it)
        {
            if (it == null) return false;
            if (it.Vid != Vid || it.Pid != Pid) return false;
            if (it.UsagePage != UsagePage) return false;
            if (Usage != 0 && it.Usage != Usage) return false;
            return it.OutputReportLen >= MinReportLength;
        }
    }

    public static class VendorProtocolRegistry
    {
        public static readonly VendorBatteryProtocol[] All = new[]
        {
            VgnMouse(),
            VgnKeyboard()
        };

        // ---------------- VGN 蜻蜓 F1 系鼠标 / 3554:F503 ----------------
        // 控制集合 FF02:0002，output report 8；先发 PCDriverStatus(2)+1 握手，
        // 再发 DeviceOnLine(3)、BatteryLevel(4)；响应 [08 04 ..]：level=r[6], charging=r[7]==1, mV=r[8..9]。
        private static VendorBatteryProtocol VgnMouse()
        {
            return new VendorBatteryProtocol
            {
                Key = "vgn-f1-mouse",
                Vendor = "VGN",
                Model = "蜻蜓 F1 系列鼠标",
                Kind = DeviceKind.Mouse,
                Vid = 0x3554,
                Pid = 0xF503,
                UsagePage = 0xFF02,
                Usage = 0x0002,
                MinReportLength = 17,
                Transport = VendorTransport.Control,
                PreCommands = new Func<byte[]>[]
                {
                    delegate { return MouseFrame(2, new byte[] { 1 }); },
                    delegate { return MouseFrame(3, null); }
                },
                Command = delegate { return MouseFrame(4, null); },
                Parse = delegate(byte[] r)
                {
                    if (r == null || r.Length <= 9 || r[1] != 4) return null;
                    int level = r[6];
                    if (level < 0 || level > 100) return null;
                    return new VendorReading
                    {
                        Level = level,
                        Charging = r[7] == 1,
                        MilliVolts = (r[8] << 8) | r[9]
                    };
                }
            };
        }

        private static byte[] MouseFrame(byte cmd, byte[] value)
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
            data[15] = (byte)((crc - 8) & 0xFF);
            byte[] report = new byte[17];
            report[0] = 8;
            Array.Copy(data, 0, report, 1, 16);
            return report;
        }

        // ---------------- VGN V98Pro V2 键盘 / 320F:5088 ----------------
        // 控制集合 FF1C:0092，report 4；必须走中断 OUT 端点（WriteFile），
        // 命令 [0,0,0x1A,6,0,0,0]；响应 [04 00 00 1A 06 00 00 00 47 ..]：level=r[8], charging=r[9]==1。
        private static VendorBatteryProtocol VgnKeyboard()
        {
            return new VendorBatteryProtocol
            {
                Key = "vgn-v98pro",
                Vendor = "VGN",
                Model = "V98Pro V2 键盘",
                Kind = DeviceKind.Keyboard,
                Vid = 0x320F,
                Pid = 0x5088,
                UsagePage = 0xFF1C,
                Usage = 0x0092,
                MinReportLength = 60,
                Transport = VendorTransport.Interrupt,
                PreCommands = new Func<byte[]>[0],
                Command = delegate
                {
                    byte[] buf = new byte[64];
                    buf[0] = 4;
                    buf[3] = 26;
                    buf[4] = 6;
                    return buf;
                },
                Parse = delegate(byte[] r)
                {
                    if (r == null || r.Length <= 8 || r[3] != 26) return null;
                    int level = r[8];
                    if (level < 0 || level > 100) return null;
                    return new VendorReading { Level = level, Charging = r[9] == 1 };
                }
            };
        }
    }
}
