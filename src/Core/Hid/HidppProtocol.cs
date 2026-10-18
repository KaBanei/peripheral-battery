using System;

namespace Pb.Core.Hid
{
    /// <summary>
    /// HID++ 2.0 报文编解码（纯函数，无 I/O，便于无硬件自测）。
    ///
    /// 报文布局（Solaar base.py request_id 组装方式，已与官方草案一致）：
    ///   短报文 0x10（7 字节） : [0]=0x10 [1]=设备索引 [2]=功能索引 [3]=(函数&lt;&lt;4)|swId [4..6]=参数(3)
    ///   长报文 0x11（20 字节）: [0]=0x11 [1]=设备索引 [2]=功能索引 [3]=(函数&lt;&lt;4)|swId [4..19]=参数(16)
    ///   错误响应: [2]=0xFF [3]=功能索引 [4]=函数|swId [5]=错误码
    /// </summary>
    public static class Hidpp
    {
        public const byte ReportIdShort = 0x10;
        public const byte ReportIdLong = 0x11;
        public const byte SubIdError = 0xFF;
        public const byte SubIdRegisterReadReply = 0x81;
        public const byte SubIdRegisterError = 0x8F;

        public const int ShortLength = 7;
        public const int LongLength = 20;

        // 特性 ID 与函数号以 Solaar hidpp20_constants.py + 官方 2012 草案为准：
        //   0x0000 ROOT            fn0 getFeature(featureId) / fn1 ping
        //   0x0005 DEVICE_NAME     fn0 getDeviceNameCount / fn1 getDeviceName(offset) / fn2 getDeviceType
        //   0x1000 BATTERY_STATUS  fn0 → [0]=百分比 [1]=nextLevel [2]=状态(0..7)
        //   0x1001 BATTERY_VOLTAGE fn0 → [0..1]=电压 mV 大端 [2]=标志位
        //   0x1004 UNIFIED_BATTERY fn1 getBatteryInfo → [0]=百分比 [1]=level 枚举 [2]=状态(0..4) [3]=外接电源
        // 注意：0x1000 是"电量百分比"，0x1001 才是"电压"，不要对调。
        public const ushort FeatureRoot = 0x0000;
        public const ushort FeatureDeviceName = 0x0005;
        public const ushort FeatureBatteryStatus = 0x1000;   // 百分比 + 充电状态（旧式）
        public const ushort FeatureBatteryVoltage = 0x1001;  // 电压 mV + 充电标志
        public const ushort FeatureUnifiedBattery = 0x1004;  // 统一电量（百分比 + 状态）

        public const int DefaultTimeoutMs = 300;
        public const int EmptyMillivolts = 3000;
        public const int FullMillivolts = 4200;

        public static byte[] BuildRequest(int deviceIndex, int featureIndex, int function, int swId,
            params byte[] parameters)
        {
            return BuildReport(ShortLength, ReportIdShort, deviceIndex, featureIndex, function, swId, parameters);
        }

        public static byte[] BuildLongRequest(int deviceIndex, int featureIndex, int function, int swId,
            params byte[] parameters)
        {
            return BuildReport(LongLength, ReportIdLong, deviceIndex, featureIndex, function, swId, parameters);
        }

        private static byte[] BuildReport(int length, byte reportId, int deviceIndex, int featureIndex,
            int function, int swId, byte[] parameters)
        {
            var report = new byte[length];
            report[0] = reportId;
            report[1] = (byte)(deviceIndex & 0xFF);
            report[2] = (byte)(featureIndex & 0xFF);
            report[3] = (byte)(((function & 0x0F) << 4) | (swId & 0x0F));
            if (parameters != null)
            {
                int count = Math.Min(parameters.Length, length - 4);
                Array.Copy(parameters, 0, report, 4, count);
            }
            return report;
        }

        /// <summary>ROOT(feature 0) / function 1 = ping，设备会原样回显参数。</summary>
        public static byte[] BuildPing(int deviceIndex, int swId, byte data)
        {
            return BuildRequest(deviceIndex, FeatureRoot, 1, swId, 0x00, 0x00, data);
        }

        /// <summary>HID++ 1.0 的 "device info" 短请示（兼容不支持 2.0 的老设备）。</summary>
        public static byte[] BuildHidpp10Ping(int deviceIndex)
        {
            var report = new byte[ShortLength];
            report[0] = ReportIdShort;
            report[1] = (byte)(deviceIndex & 0xFF);
            return report; // [2..6] 全 0
        }

        /// <summary>HID++ 1.0 读寄存器：sub-id 0x81。</summary>
        public static byte[] BuildRegisterRead(int deviceIndex, byte register)
        {
            var report = new byte[ShortLength];
            report[0] = ReportIdShort;
            report[1] = (byte)(deviceIndex & 0xFF);
            report[2] = SubIdRegisterReadReply;
            report[3] = register;
            return report;
        }

        public static HidppReply Parse(byte[] report)
        {
            if (report == null || report.Length < 4) return null;
            if (report[0] != ReportIdShort && report[0] != ReportIdLong) return null;

            var reply = new HidppReply();
            reply.RawReport = report;
            reply.DeviceIndex = report[1];
            reply.IsError = report[2] == SubIdError;

            if (reply.IsError)
            {
                reply.FeatureIndex = report[3];
                if (report.Length > 4)
                {
                    reply.Function = (report[4] >> 4) & 0x0F;
                    reply.SwId = report[4] & 0x0F;
                }
                reply.ErrorCode = report.Length > 5 ? report[5] : (byte)0;
                reply.Payload = new byte[0];
                return reply;
            }

            reply.FeatureIndex = report[2];
            reply.Function = (report[3] >> 4) & 0x0F;
            reply.SwId = report[3] & 0x0F;
            int payloadLength = report.Length - 4;
            reply.Payload = new byte[payloadLength];
            Array.Copy(report, 4, reply.Payload, 0, payloadLength);
            return reply;
        }

        /// <summary>解析 HID++ 1.0 读寄存器响应（[0]=0x10 [1]=idx [2]=0x81 [3]=reg [4..6]=数据）。</summary>
        public static bool TryParseRegisterReply(byte[] report, int deviceIndex, byte register, out int data0,
            out int data1, out int data2)
        {
            data0 = data1 = data2 = 0;
            if (report == null || report.Length < 7) return false;
            if (report[0] != ReportIdShort && report[0] != ReportIdLong) return false;
            if (report[1] != (byte)(deviceIndex & 0xFF)) return false;
            if (report[2] != SubIdRegisterReadReply) return false;
            if (report[3] != register) return false;
            data0 = report[4];
            data1 = report[5];
            data2 = report[6];
            return true;
        }

        /// <summary>是否"槽位为空 / 未配对"的 HID++ 1.0 寄存器错误响应。</summary>
        public static bool IsRegisterError(byte[] report, int deviceIndex)
        {
            return report != null && report.Length >= 3
                   && (report[0] == ReportIdShort || report[0] == ReportIdLong)
                   && report[1] == (byte)(deviceIndex & 0xFF)
                   && report[2] == SubIdRegisterError;
        }

        public static int ClampPercent(int value)
        {
            if (value < 0) return 0;
            if (value > 100) return 100;
            return value;
        }

        /// <summary>电压换算电量：3.0V → 0%，4.2V → 100%（仅为没有百分比特性时的兜底估算）。</summary>
        public static int PercentFromMillivolts(int millivolts)
        {
            double ratio = (millivolts - EmptyMillivolts) * 100.0 / (FullMillivolts - EmptyMillivolts);
            return ClampPercent((int)Math.Round(ratio));
        }

        public static BatteryState MapUnifiedStatus(int status)
        {
            switch (status)
            {
                case 0: return BatteryState.Discharging;
                case 1: return BatteryState.Charging;
                case 2: return BatteryState.Charging; // 慢充
                case 3: return BatteryState.Full;
                default: return BatteryState.Unknown;
            }
        }

        public static BatteryState MapLegacyStatus(int status)
        {
            switch (status)
            {
                case 0: return BatteryState.Discharging;
                case 1: return BatteryState.Charging;
                case 2: return BatteryState.Full;     // almost full
                case 3: return BatteryState.Full;
                case 4: return BatteryState.Charging; // slow recharge
                default: return BatteryState.Unknown;
            }
        }

        /// <summary>0x1001 的标志位：bit7 外接电源、bit0-1 充电状态、bit3 快充、bit4 慢充、bit5 电量告急。</summary>
        public static BatteryState MapVoltageFlags(int flags)
        {
            bool external = (flags & 0x80) != 0;
            if (!external) return BatteryState.Discharging;
            int status = flags & 0x03;
            if (status == 0x01 || status == 0x03) return BatteryState.Full;
            if (status == 0x02) return BatteryState.Wired; // 接了外部电源但不充电
            return BatteryState.Charging;
        }

        public static bool IsVoltageCritical(int flags)
        {
            return (flags & 0x20) != 0;
        }

        /// <summary>
        /// 0x0005 function 2 getDeviceType 的类型枚举 → 展示类型。
        /// 0 键盘 / 1 遥控 / 2 小键盘 / 3 鼠标 / 4 触摸板 / 5 轨迹球 /
        /// 6 演示器 / 7 接收器 / 8 耳机 / 12 手柄；其余（含拿不准的 4、5）一律 Other。
        /// </summary>
        public static DeviceKind MapDeviceType(int type)
        {
            switch (type)
            {
                case 0: return DeviceKind.Keyboard;
                case 2: return DeviceKind.Numpad;
                case 3: return DeviceKind.Mouse;
                case 6: return DeviceKind.Presenter;
                case 8: return DeviceKind.Headset;
                case 12: return DeviceKind.Gamepad;
                default: return DeviceKind.Other;
            }
        }

        /// <summary>设备名关键字兜底推断（类型字节缺失/不认识时使用）。</summary>
        public static DeviceKind GuessKindFromName(string name)
        {
            if (string.IsNullOrEmpty(name)) return DeviceKind.Unknown;
            string s = name.ToLowerInvariant();
            if (s.Contains("mouse") || s.Contains("鼠标") || s.Contains("trackball")) return DeviceKind.Mouse;
            if (s.Contains("keyboard") || s.Contains("键盘") || s.Contains("keypad")) return DeviceKind.Keyboard;
            if (s.Contains("headset") || s.Contains("headphone") || s.Contains("耳机") || s.Contains("audio"))
                return DeviceKind.Headset;
            if (s.Contains("gamepad") || s.Contains("joystick") || s.Contains("手柄")) return DeviceKind.Gamepad;
            if (s.Contains("presenter") || s.Contains("演示器")) return DeviceKind.Presenter;
            if (s.Contains("numpad") || s.Contains("数字键盘")) return DeviceKind.Numpad;
            return DeviceKind.Unknown;
        }

        /// <summary>0x1004 返回的粗粒度 level 枚举（bit 位），百分比为 0 时用于兜底。</summary>
        public static int PercentFromLevelFlag(int level)
        {
            switch (level)
            {
                case 1: return 5;   // critical
                case 2: return 20;  // low
                case 4: return 50;  // good
                case 8: return 100; // full
                default: return -1;
            }
        }

        public static string DescribeError(int code)
        {
            switch (code)
            {
                case 0x01: return "UNKNOWN";
                case 0x02: return "INVALID_ARGUMENT";
                case 0x03: return "OUT_OF_RANGE";
                case 0x04: return "HARDWARE_ERROR";
                case 0x05: return "LOGITECH_ERROR";
                case 0x06: return "INVALID_FEATURE_INDEX";
                case 0x07: return "INVALID_FUNCTION";
                case 0x08: return "BUSY";
                case 0x09: return "UNSUPPORTED";
                default: return "0x" + code.ToString("X2");
            }
        }
    }

    /// <summary>一条 HID++ 响应（短/长报文统一表示）。</summary>
    public sealed class HidppReply
    {
        public byte[] RawReport { get; set; }
        public int DeviceIndex { get; set; }
        public int FeatureIndex { get; set; }
        public int Function { get; set; }
        public int SwId { get; set; }
        public bool IsError { get; set; }
        public byte ErrorCode { get; set; }
        /// <summary>参数区（原始报文 [4..]）。</summary>
        public byte[] Payload { get; set; }

        public int Byte(int index)
        {
            if (Payload == null || index < 0 || index >= Payload.Length) return -1;
            return Payload[index];
        }

        public int UInt16BE(int index)
        {
            int hi = Byte(index);
            int lo = Byte(index + 1);
            if (hi < 0 || lo < 0) return -1;
            return (hi << 8) | lo;
        }

        public override string ToString()
        {
            if (IsError)
                return "ERR dev=" + DeviceIndex + " feat=" + FeatureIndex + " fn=" + Function
                       + " code=0x" + ErrorCode.ToString("X2") + "(" + Hidpp.DescribeError(ErrorCode) + ")";
            return "OK dev=" + DeviceIndex + " feat=" + FeatureIndex + " fn=" + Function
                   + " sw=" + SwId + " payload=" + Payload.Length + "B";
        }
    }
}
