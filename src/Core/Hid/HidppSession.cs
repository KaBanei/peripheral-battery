using System;
using System.Diagnostics;
using System.Text;

namespace Pb.Core.Hid
{
    /// <summary>一次电量读取结果（可为百分比 + 电压 + 状态）。</summary>
    public sealed class BatteryReading
    {
        public int? Percent { get; set; }
        public int? MilliVolts { get; set; }
        public BatteryState State { get; set; }
        public string Source { get; set; }
        public string Note { get; set; }
        public HidppReply RawReply { get; set; }

        public bool HasData
        {
            get { return Percent.HasValue || MilliVolts.HasValue; }
        }

        public override string ToString()
        {
            return (Percent.HasValue ? Percent.Value + "%" : "-")
                   + (MilliVolts.HasValue ? " " + MilliVolts.Value + "mV" : "")
                   + " " + State + (Source != null ? " [" + Source + "]" : "");
        }
    }

    /// <summary>
    /// 在一个已打开的 HID 通道上执行 HID++ 会话：请求/响应配对、ping、特性索引、设备名、电量。
    /// 只依赖 IHidChannel，因此可以用伪通道做无硬件自测。
    /// </summary>
    public sealed class HidppSession
    {
        private readonly IHidChannel _channel;
        private readonly Action<string> _log;
        private readonly object _io = new object();
        private int _swId;

        public HidppSession(IHidChannel channel)
            : this(channel, null)
        {
        }

        public HidppSession(IHidChannel channel, Action<string> log)
        {
            if (channel == null) throw new ArgumentNullException("channel");
            _channel = channel;
            _log = log;
            _swId = 1 + (new Random(Environment.TickCount).Next(13));
        }

        public IHidChannel Channel
        {
            get { return _channel; }
        }

        /// <summary>发起一次 HID++ 2.0 请求；超时返回 null。错误响应会原样返回（IsError=true）。</summary>
        public HidppReply Request(int deviceIndex, int featureIndex, int function, int timeoutMs,
            params byte[] parameters)
        {
            if (timeoutMs <= 0) timeoutMs = Hidpp.DefaultTimeoutMs;
            int sw;
            byte[] request;
            lock (_io)
            {
                sw = _swId;
                _swId = (_swId % 14) + 1;
                request = Hidpp.BuildRequest(deviceIndex, featureIndex, function, sw, parameters);
                try
                {
                    if (!_channel.Write(request)) return null;
                }
                catch (Exception ex)
                {
                    Warn("HID++ 写失败: " + ex.Message);
                    return null;
                }

                var clock = Stopwatch.StartNew();
                while (true)
                {
                    int remaining = timeoutMs - (int)clock.ElapsedMilliseconds;
                    if (remaining <= 0) return null;

                    byte[] report;
                    try
                    {
                        report = _channel.Read(remaining);
                    }
                    catch (Exception ex)
                    {
                        Warn("HID++ 读失败: " + ex.Message);
                        return null;
                    }
                    if (report == null) return null;

                    HidppReply reply = Hidpp.Parse(report);
                    if (reply == null) continue;
                    if (reply.DeviceIndex != (deviceIndex & 0xFF)) continue;
                    if (reply.SwId != sw)
                    {
                        // 其它会话的响应或设备主动通知，丢弃后继续等
                        continue;
                    }
                    return reply;
                }
            }
        }

        /// <summary>
        /// 在线探测：先 HID++ 2.0 ROOT ping（收到任何本槽位响应即在线，包括错误响应），
        /// 再退化到 HID++ 1.0 device info（0x8F 寄存器错误 = 槽位为空）。
        /// </summary>
        public bool Ping(int deviceIndex, int timeoutMs)
        {
            if (timeoutMs <= 0) timeoutMs = Hidpp.DefaultTimeoutMs;
            byte device = (byte)(deviceIndex & 0xFF);

            // 1) HID++ 1.0 device-info 短请示：一次收发即可识别"槽位为空"（0x8F 寄存器错误），
            //    对老设备也直接算在线；开销最小，所以放最前面。
            byte[] request = Hidpp.BuildHidpp10Ping(deviceIndex);
            lock (_io)
            {
                try
                {
                    if (!_channel.Write(request)) return false;
                }
                catch
                {
                    return false;
                }

                var clock = Stopwatch.StartNew();
                int firstPhase = Math.Max(60, timeoutMs / 2);
                while (clock.ElapsedMilliseconds < firstPhase)
                {
                    int remaining = firstPhase - (int)clock.ElapsedMilliseconds;
                    if (remaining <= 0) break;
                    byte[] report;
                    try
                    {
                        report = _channel.Read(remaining);
                    }
                    catch
                    {
                        break;
                    }
                    if (report == null) break;
                    if (Hidpp.IsRegisterError(report, deviceIndex)) return false;
                    if (report.Length >= 2 && report[1] == device) return true;
                }
            }

            // 2) HID++ 2.0 ROOT(feature 0) / function 1 = ping：收到本槽位任何响应（含错误响应）即在线
            HidppReply reply = Request(deviceIndex, Hidpp.FeatureRoot, 1, timeoutMs, 0x00, 0x00, 0x00);
            return reply != null;
        }

        /// <summary>解析 featureId → 功能索引；0 表示设备不支持，-1 表示无响应/错误。</summary>
        public int GetFeatureIndex(int deviceIndex, ushort featureId, int timeoutMs)
        {
            HidppReply reply = Request(deviceIndex, Hidpp.FeatureRoot, 0, timeoutMs,
                (byte)(featureId >> 8), (byte)(featureId & 0xFF));
            if (reply == null) return -1;
            if (reply.IsError) return -1;
            int index = reply.Byte(0);
            if (index <= 0) return 0;
            return index;
        }

        public int GetDeviceType(int deviceIndex, int nameFeatureIndex, int timeoutMs)
        {
            if (nameFeatureIndex <= 0) return -1;
            HidppReply reply = Request(deviceIndex, nameFeatureIndex, 2, timeoutMs);
            if (reply == null || reply.IsError) return -1;
            int type = reply.Byte(0);
            return type < 0 ? -1 : type;
        }

        public string GetDeviceName(int deviceIndex, int nameFeatureIndex, int timeoutMs)
        {
            if (nameFeatureIndex <= 0) return null;
            HidppReply countReply = Request(deviceIndex, nameFeatureIndex, 0, timeoutMs);
            if (countReply == null || countReply.IsError) return null;
            int count = countReply.Byte(0);
            if (count <= 0 || count > 128) return null;

            var sb = new StringBuilder();
            int offset = 0;
            for (int guard = 0; guard < 64 && offset < count; guard++)
            {
                HidppReply chunk = Request(deviceIndex, nameFeatureIndex, 1, timeoutMs, (byte)offset);
                if (chunk == null || chunk.IsError || chunk.Payload.Length == 0) break;
                int take = Math.Min(chunk.Payload.Length, count - offset);
                bool terminated = false;
                for (int i = 0; i < take; i++)
                {
                    byte b = chunk.Payload[i];
                    if (b == 0)
                    {
                        terminated = true;
                        break;
                    }
                    sb.Append((char)b);
                }
                offset += take;
                if (terminated) break;
            }
            string name = sb.ToString().Trim();
            return name.Length == 0 ? null : name;
        }

        /// <summary>
        /// 依次尝试 0x1004（统一电量，百分比+状态）→ 0x1000（旧式百分比+状态）
        /// → 0x1001（电压+标志，百分比由电压估算）→ HID++ 1.0 寄存器 0x07/0x0D。
        /// </summary>
        public BatteryReading ReadBattery(int deviceIndex, int unifiedFeatureIndex, int statusFeatureIndex,
            int voltageFeatureIndex, int timeoutMs)
        {
            if (unifiedFeatureIndex > 0)
            {
                HidppReply reply = Request(deviceIndex, unifiedFeatureIndex, 1, timeoutMs);
                if (reply != null && !reply.IsError && reply.Payload.Length >= 3)
                {
                    var reading = new BatteryReading();
                    reading.Source = "0x1004";
                    reading.RawReply = reply;
                    int percent = reply.Byte(0);
                    int level = reply.Byte(1);
                    int status = reply.Byte(2);
                    if (percent >= 0 && percent <= 100) reading.Percent = percent;
                    if (reading.Percent.GetValueOrDefault() == 0)
                    {
                        int approx = Hidpp.PercentFromLevelFlag(level);
                        if (approx >= 0) reading.Percent = approx;
                    }
                    reading.State = Hidpp.MapUnifiedStatus(status);
                    if (reading.HasData) return reading;
                }
            }

            if (statusFeatureIndex > 0)
            {
                HidppReply reply = Request(deviceIndex, statusFeatureIndex, 0, timeoutMs);
                if (reply != null && !reply.IsError && reply.Payload.Length >= 1)
                {
                    var reading = new BatteryReading();
                    reading.Source = "0x1000";
                    reading.RawReply = reply;
                    int percent = reply.Byte(0);
                    if (percent >= 0 && percent <= 100) reading.Percent = percent;
                    int status = reply.Byte(2);
                    reading.State = Hidpp.MapLegacyStatus(status);
                    if (reading.HasData) return reading;
                }
            }

            if (voltageFeatureIndex > 0)
            {
                HidppReply reply = Request(deviceIndex, voltageFeatureIndex, 0, timeoutMs);
                if (reply != null && !reply.IsError && reply.Payload.Length >= 3)
                {
                    var reading = new BatteryReading();
                    reading.Source = "0x1001";
                    reading.RawReply = reply;
                    int millivolts = reply.UInt16BE(0);
                    int flags = reply.Byte(2);
                    if (millivolts > 0)
                    {
                        reading.MilliVolts = millivolts;
                        reading.Percent = Hidpp.PercentFromMillivolts(millivolts);
                        reading.Note = "电压估算";
                    }
                    reading.State = Hidpp.MapVoltageFlags(flags);
                    if (Hidpp.IsVoltageCritical(flags)) reading.State = BatteryState.Critical;
                    if (reading.HasData) return reading;
                }
            }

            return ReadBatteryHidpp10(deviceIndex, timeoutMs);
        }

        /// <summary>老设备回退：HID++ 1.0 读寄存器 0x07（电量状态），失败再试 0x0D（充电状态）。</summary>
        public BatteryReading ReadBatteryHidpp10(int deviceIndex, int timeoutMs)
        {
            BatteryReading reading = ReadRegisterBattery(deviceIndex, 0x07, timeoutMs);
            if (reading != null) return reading;
            return ReadRegisterBattery(deviceIndex, 0x0D, timeoutMs);
        }

        private BatteryReading ReadRegisterBattery(int deviceIndex, byte register, int timeoutMs)
        {
            if (timeoutMs <= 0) timeoutMs = Hidpp.DefaultTimeoutMs;
            byte[] request = Hidpp.BuildRegisterRead(deviceIndex, register);
            lock (_io)
            {
                try
                {
                    if (!_channel.Write(request)) return null;
                }
                catch
                {
                    return null;
                }

                var clock = Stopwatch.StartNew();
                while (clock.ElapsedMilliseconds < timeoutMs)
                {
                    int remaining = timeoutMs - (int)clock.ElapsedMilliseconds;
                    if (remaining <= 0) break;
                    byte[] report;
                    try
                    {
                        report = _channel.Read(remaining);
                    }
                    catch
                    {
                        return null;
                    }
                    if (report == null) return null;
                    if (Hidpp.IsRegisterError(report, deviceIndex)) return null;

                    int data0, data1, data2;
                    if (Hidpp.TryParseRegisterReply(report, deviceIndex, register, out data0, out data1, out data2))
                    {
                        var reading = new BatteryReading();
                        reading.Source = "HID++1 寄存器 0x" + register.ToString("X2");
                        if (data0 >= 0 && data0 <= 100) reading.Percent = data0;
                        reading.State = Hidpp.MapLegacyStatus(data2);
                        return reading.HasData ? reading : null;
                    }
                }
            }
            return null;
        }

        private void Warn(string message)
        {
            if (_log == null) return;
            try { _log(message); }
            catch { }
        }
    }
}
