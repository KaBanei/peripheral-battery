using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;
using System.Threading;
using System.Web.Script.Serialization;
using Pb.Core;
using Pb.Core.Hid;
using Pb.Core.Providers;

namespace Pb.Tools
{
    /// <summary>
    /// 设备诊断工具（build.ps1 -Probe 生成 BatteryProbe.exe）：
    ///   --dump      打印全部 HID 接口（VID:PID / usage / 报告长度 / 产品名 / feature usage / 路径）
    ///   --scan      跑 Logitech + 标准 HID + 演示模式，输出结果表
    ///   --selftest  HID++ 编解码 + 假接收器端到端自测（无需硬件）
    ///   --json      机器可读输出
    /// </summary>
    public static class ProbeProgram
    {
        private static bool _json;
        private static bool _verbose;
        private static int _pass;
        private static int _fail;

        public static int Main(string[] args)
        {
            try
            {
                // 输出被重定向（日志/管道）时用 UTF-8，避免中文变乱码；交互式控制台保持系统默认。
                if (Console.IsOutputRedirected) Console.OutputEncoding = new UTF8Encoding(false);
            }
            catch
            {
            }

            bool dump = false;
            bool scan = false;
            bool selftest = false;
            bool demo = true;

            foreach (string raw in args)
            {
                string a = (raw == null ? "" : raw.Trim().ToLowerInvariant());
                switch (a)
                {
                    case "--dump": dump = true; break;
                    case "--scan": scan = true; break;
                    case "--selftest": selftest = true; break;
                    case "--json": _json = true; break;
                    case "--verbose": _verbose = true; break;
                    case "--no-demo": demo = false; break;
                    case "--help":
                    case "-h":
                    case "/?":
                        PrintHelp();
                        return 0;
                    case "":
                        break;
                    default:
                        Console.WriteLine("未知参数: " + raw);
                        PrintHelp();
                        return 2;
                }
            }

            if (!dump && !scan && !selftest)
            {
                PrintHelp();
                dump = true;
            }

            if (selftest) return RunSelfTest();
            if (dump) RunDump();
            if (scan) RunScan(demo);
            return 0;
        }

        private static void PrintHelp()
        {
            Console.WriteLine("BatteryProbe - 2.4G 外设电量诊断工具");
            Console.WriteLine();
            Console.WriteLine("用法: BatteryProbe.exe [--dump] [--scan] [--selftest] [--json] [--verbose] [--no-demo]");
            Console.WriteLine();
            Console.WriteLine("  --dump      列出全部 HID 接口（VID:PID、usage、报告长度、产品名、feature usage、路径）");
            Console.WriteLine("  --scan      运行 罗技 HID++ / 标准 HID / 演示模式 三个 Provider，输出结果表");
            Console.WriteLine("  --selftest  HID++ 编解码 + 假接收器端到端自测（无需硬件），PASS/FAIL 汇总");
            Console.WriteLine("  --json      机器可读输出");
            Console.WriteLine("  --verbose   打印调试日志");
            Console.WriteLine("  --no-demo   --scan 时不包含演示设备");
        }

        // ============================ --dump ============================

        private static void RunDump()
        {
            var clock = Stopwatch.StartNew();
            var bus = new HidBus(delegate(string m) { if (_verbose) Console.WriteLine("  · " + m); });
            List<HidInterface> interfaces = bus.EnumerateFresh();
            clock.Stop();

            if (_json)
            {
                Console.WriteLine(JsonSerialize(DumpJson(interfaces, clock.ElapsedMilliseconds)));
                return;
            }

            Console.WriteLine("== HID 接口 ==================================================");
            Console.WriteLine("共 " + interfaces.Count + " 个接口，枚举耗时 " + clock.ElapsedMilliseconds + " ms");
            Console.WriteLine();

            int index = 0;
            foreach (HidInterface iface in interfaces)
            {
                index++;
                Console.WriteLine("[" + index.ToString().PadLeft(2) + "] "
                                  + "VID:PID " + iface.Vid.ToString("X4") + ":" + iface.Pid.ToString("X4")
                                  + "  v" + ((iface.Version >> 8) & 0xFF).ToString("X2") + "."
                                  + (iface.Version & 0xFF).ToString("X2")
                                  + "  " + (iface.DisplayName == null ? "?" : iface.DisplayName));
                Console.WriteLine("     usage   0x" + iface.UsagePage.ToString("X4") + ":0x"
                                  + iface.Usage.ToString("X4")
                                  + "   in=" + iface.InputReportLen + " out=" + iface.OutputReportLen
                                  + " feat=" + iface.FeatureReportLen
                                  + (iface.Service != null ? "   service=" + iface.Service : ""));
                Console.WriteLine("     product=" + Q(iface.Product) + "  manufacturer=" + Q(iface.Manufacturer)
                                  + "  serial=" + Q(iface.Serial));
                Console.WriteLine("     instance=" + Q(iface.InstanceId));
                Console.WriteLine("     desc=" + Q(iface.DeviceDesc));

                HidParentInfo parent;
                if (bus.TryGetParent(iface, out parent) && parent != null)
                    Console.WriteLine("     parent=" + Q(parent.InstanceId) + "  " + Q(parent.Name));

                if (iface.FeatureUsages.Count > 0)
                {
                    Console.WriteLine("     feature usages (" + iface.FeatureUsages.Count + "):");
                    foreach (HidUsageRange usage in iface.FeatureUsages)
                        Console.WriteLine("       " + FormatUsage(usage));
                }
                if (_verbose && iface.InputUsages.Count > 0)
                {
                    Console.WriteLine("     input usages (" + iface.InputUsages.Count + "):");
                    foreach (HidUsageRange usage in iface.InputUsages)
                        Console.WriteLine("       " + FormatUsage(usage));
                }
                Console.WriteLine("     path=" + iface.Path);
                Console.WriteLine();
            }
        }

        private static string FormatUsage(HidUsageRange usage)
        {
            string range = usage.IsRange
                ? "0x" + usage.UsageMin.ToString("X4") + "-0x" + usage.UsageMax.ToString("X4")
                : "0x" + usage.UsageMin.ToString("X4");
            return "0x" + usage.UsagePage.ToString("X4") + ":" + range
                   + "  reportId=" + usage.ReportId
                   + "  bits=" + usage.BitSize
                   + "  logical=" + usage.LogicalMin + ".." + usage.LogicalMax;
        }

        private static List<object> DumpJson(List<HidInterface> interfaces, long elapsedMs)
        {
            var list = new List<object>();
            foreach (HidInterface iface in interfaces)
            {
                var usages = new List<object>();
                foreach (HidUsageRange usage in iface.FeatureUsages)
                {
                    usages.Add(new Dictionary<string, object>
                    {
                        { "usagePage", usage.UsagePage },
                        { "usageMin", usage.UsageMin },
                        { "usageMax", usage.UsageMax },
                        { "isRange", usage.IsRange },
                        { "reportId", usage.ReportId },
                        { "bits", usage.BitSize },
                        { "logicalMin", usage.LogicalMin },
                        { "logicalMax", usage.LogicalMax }
                    });
                }
                list.Add(new Dictionary<string, object>
                {
                    { "vid", iface.Vid },
                    { "pid", iface.Pid },
                    { "version", iface.Version },
                    { "usagePage", iface.UsagePage },
                    { "usage", iface.Usage },
                    { "inputReportLen", iface.InputReportLen },
                    { "outputReportLen", iface.OutputReportLen },
                    { "featureReportLen", iface.FeatureReportLen },
                    { "product", iface.Product },
                    { "manufacturer", iface.Manufacturer },
                    { "serial", iface.Serial },
                    { "instanceId", iface.InstanceId },
                    { "deviceDesc", iface.DeviceDesc },
                    { "service", iface.Service },
                    { "path", iface.Path },
                    { "featureUsages", usages }
                });
            }
            var root = new Dictionary<string, object>
            {
                { "count", list.Count },
                { "elapsedMs", elapsedMs },
                { "interfaces", list }
            };
            return new List<object> { root };
        }

        // ============================ --scan ============================

        private static void RunScan(bool demo)
        {
            var config = new AppConfig();
            config.EnableLogitech = true;
            config.EnableStandardHid = true;
            config.EnableDemo = demo;
            config.EnableBluetooth = false;
            config.EnableXInput = false;
            config.VerboseLog = _verbose;

            var ctx = new ProbeContext(config, _verbose);
            var providers = new List<IProvider>();
            providers.Add(new LogitechHidppProvider(config, ctx));
            providers.Add(new StandardHidBatteryProvider(config, ctx));
            providers.Add(new SimulationProvider(config, ctx));

            var all = new List<Peripheral>();
            var statuses = new List<Dictionary<string, object>>();
            var total = Stopwatch.StartNew();

            foreach (IProvider provider in providers)
            {
                var clock = Stopwatch.StartNew();
                List<Peripheral> found;
                string error = null;
                try
                {
                    found = provider.Scan(CancellationToken.None) ?? new List<Peripheral>();
                }
                catch (Exception ex)
                {
                    if (ex is OutOfMemoryException || ex is StackOverflowException) throw;
                    found = new List<Peripheral>();
                    error = ex.Message;
                }
                clock.Stop();
                foreach (Peripheral p in found)
                {
                    if (string.IsNullOrEmpty(p.Provider)) p.Provider = provider.DisplayName;
                    if (p.Updated == default(DateTime)) p.Updated = DateTime.Now;
                }
                all.AddRange(found);
                statuses.Add(new Dictionary<string, object>
                {
                    { "key", provider.Key },
                    { "displayName", provider.DisplayName },
                    { "count", found.Count },
                    { "durationMs", clock.ElapsedMilliseconds },
                    { "ok", error == null },
                    { "error", error }
                });
                if (!_json)
                {
                    Console.WriteLine("  [" + provider.Key + "] " + Pad(provider.DisplayName, 14)
                                      + found.Count + " 台   " + clock.ElapsedMilliseconds.ToString().PadLeft(5)
                                      + " ms   " + (error == null ? "OK" : "失败: " + error));
                }
            }
            total.Stop();

            if (_json)
            {
                Console.WriteLine(JsonSerialize(ScanJson(all, statuses, total.ElapsedMilliseconds)));
                return;
            }

            Console.WriteLine();
            Console.WriteLine("== 扫描结果 ==================================================");
            Console.WriteLine("共 " + all.Count + " 台设备，总耗时 " + total.ElapsedMilliseconds + " ms");
            Console.WriteLine();
            Console.WriteLine(Pad("#", 3) + Pad("名称", 26) + Pad("类型", 7) + Pad("连接", 6)
                              + Pad("电量", 6) + Pad("电压", 9) + Pad("状态", 8) + "来源");
            Console.WriteLine(new string('-', 92));
            int i = 0;
            foreach (Peripheral p in all)
            {
                i++;
                Console.WriteLine(Pad(i.ToString(), 3)
                                  + Pad(p.Name, 26)
                                  + Pad(p.KindText, 7)
                                  + Pad(p.Connection, 6)
                                  + Pad(p.Percent.HasValue ? p.Percent.Value + "%" : "-", 6)
                                  + Pad(p.MilliVolts.HasValue ? p.MilliVolts.Value + "mV" : "-", 9)
                                  + Pad(StateText(p.State), 8)
                                  + (p.Provider == null ? "-" : p.Provider));
            }
            if (all.Count == 0)
                Console.WriteLine("  （没有可用数据：本机可能没有罗技接收器，也没有 0x06:0x20 标准 HID 电量 usage）");
            Console.WriteLine();
            Console.WriteLine("提示: 加 --verbose 看每台设备的详细发现过程。");
        }

        private static List<object> ScanJson(List<Peripheral> devices,
            List<Dictionary<string, object>> statuses, long elapsedMs)
        {
            var list = new List<object>();
            foreach (Peripheral p in devices)
            {
                list.Add(new Dictionary<string, object>
                {
                    { "id", p.Id },
                    { "name", p.Name },
                    { "brand", p.Brand },
                    { "kind", p.Kind.ToString() },
                    { "kindText", p.KindText },
                    { "connection", p.Connection },
                    { "percent", p.Percent.HasValue ? (object)p.Percent.Value : null },
                    { "milliVolts", p.MilliVolts.HasValue ? (object)p.MilliVolts.Value : null },
                    { "state", p.State.ToString() },
                    { "signal", p.Signal.HasValue ? (object)p.Signal.Value : null },
                    { "provider", p.Provider },
                    { "detail", p.Detail },
                    { "devicePath", p.DevicePath },
                    { "vid", p.Vid },
                    { "pid", p.Pid },
                    { "simulated", p.Simulated }
                });
            }
            var root = new Dictionary<string, object>
            {
                { "count", devices.Count },
                { "elapsedMs", elapsedMs },
                { "providers", statuses },
                { "devices", list }
            };
            return new List<object> { root };
        }

        private static string StateText(BatteryState state)
        {
            switch (state)
            {
                case BatteryState.Discharging: return "放电";
                case BatteryState.Charging: return "充电中";
                case BatteryState.Full: return "已充满";
                case BatteryState.Low: return "电量低";
                case BatteryState.Critical: return "严重不足";
                case BatteryState.Wired: return "有线供电";
                default: return "未知";
            }
        }

        // ============================ --selftest ============================

        private static int RunSelfTest()
        {
            _pass = 0;
            _fail = 0;

            Console.WriteLine("== HID++ 编解码 / 假接收器端到端自测 ==");
            Console.WriteLine();

            TestStructLayout();
            TestRequestEncoding();
            TestParsing();
            TestBatteryFeatures();
            TestHidpp10Fallback();
            TestPing();
            TestKindMapping();
            TestStandardHidScaling();
            TestStandardHidEndToEnd();
            TestLogitechEndToEnd();
            TestNoReceiverIsFast();

            Console.WriteLine();
            Console.WriteLine("结果: PASS " + _pass + " / FAIL " + _fail);
            if (_json)
            {
                Console.WriteLine("{\"pass\":" + _pass + ",\"fail\":" + _fail + "}");
            }
            return _fail == 0 ? 0 : 1;
        }

        private static void TestStructLayout()
        {
            Section("结构体布局");
            Check("HIDP_VALUE_CAPS = 72 字节", HidNative.ValueCapsSize == 72, "实际 " + HidNative.ValueCapsSize);
            Check("HIDP_CAPS = 64 字节", HidNative.CapsSize == 64, "实际 " + HidNative.CapsSize);
            Check("HIDD_ATTRIBUTES = 12 字节", HidNative.AttributesSize == 12, "实际 " + HidNative.AttributesSize);
        }

        private static void TestRequestEncoding()
        {
            Section("请求编码");

            byte[] getFeature = Hidpp.BuildRequest(3, 0x00, 0, 5, 0x10, 0x04);
            Check("getFeature 请求编码", getFeature.Length == 7 && getFeature[0] == 0x10
                  && getFeature[1] == 3 && getFeature[2] == 0x00 && getFeature[3] == 0x05
                  && getFeature[4] == 0x10 && getFeature[5] == 0x04 && getFeature[6] == 0x00, Hex(getFeature));

            byte[] shifted = Hidpp.BuildRequest(1, 0x08, 1, 0x0A);
            Check("byte3 = (函数<<4)|swId", shifted[3] == 0x1A, Hex(shifted));

            byte[] longReport = Hidpp.BuildLongRequest(2, 0x07, 3, 0x0F);
            Check("长报文 20 字节 / 0x11", longReport.Length == 20 && longReport[0] == 0x11
                  && longReport[3] == 0x3F, Hex(longReport));

            byte[] ping = Hidpp.BuildPing(1, 7, 0x42);
            Check("ROOT ping 编码", ping[2] == 0x00 && ping[3] == 0x17 && ping[6] == 0x42, Hex(ping));

            byte[] register = Hidpp.BuildRegisterRead(2, 0x07);
            Check("HID++1 读寄存器 0x07", register[2] == 0x81 && register[3] == 0x07, Hex(register));
        }

        private static void TestParsing()
        {
            Section("响应解析");

            HidppReply ok = Hidpp.Parse(new byte[] { 0x11, 3, 0x00, 0x15, 0x09, 0x01, 0x00 });
            Check("正常响应", ok != null && !ok.IsError && ok.DeviceIndex == 3 && ok.FeatureIndex == 0
                  && ok.Function == 1 && ok.SwId == 5 && ok.Payload.Length == 3 && ok.Payload[0] == 9,
                ok == null ? "null" : ok.ToString());

            HidppReply error = Hidpp.Parse(new byte[] { 0x10, 3, 0xFF, 0x05, 0x15, 0x09, 0x00 });
            Check("错误响应 [2]=0xFF", error != null && error.IsError && error.FeatureIndex == 5
                  && error.Function == 1 && error.SwId == 5 && error.ErrorCode == 0x09,
                error == null ? "null" : error.ToString());

            Check("非 HID++ 报文返回 null", Hidpp.Parse(new byte[] { 0x01, 0x02, 0x03, 0x04 }) == null);
            Check("过短报文返回 null", Hidpp.Parse(new byte[] { 0x10, 0x01 }) == null);
            Check("空报文返回 null", Hidpp.Parse(null) == null);

            var reply = new HidppReply();
            reply.Payload = new byte[] { 0x0F, 0x1C };
            Check("大端 u16", reply.UInt16BE(0) == 3868, "3868 mV");
        }

        private static void TestBatteryFeatures()
        {
            Section("电量特性解析");

            var unified = new FakeChannel();
            unified.Handler = delegate(byte[] request)
            {
                return LongReply(request[1], request[2], request[3], new byte[] { 64, 0x04, 0x00, 0x00 });
            };
            BatteryReading reading = new HidppSession(unified).ReadBattery(1, 8, 0, 0, 200);
            Check("0x1004 fn1 → 64% / Discharging",
                reading != null && reading.Percent == 64 && reading.State == BatteryState.Discharging
                && reading.Source == "0x1004", Describe(reading));

            var unifiedCharging = new FakeChannel();
            unifiedCharging.Handler = delegate(byte[] request)
            {
                return LongReply(request[1], request[2], request[3], new byte[] { 88, 0x04, 0x01, 0x01 });
            };
            reading = new HidppSession(unifiedCharging).ReadBattery(2, 8, 0, 0, 200);
            Check("0x1004 充电状态 → Charging", reading != null && reading.Percent == 88
                  && reading.State == BatteryState.Charging, Describe(reading));

            var unifiedLevel = new FakeChannel();
            unifiedLevel.Handler = delegate(byte[] request)
            {
                return LongReply(request[1], request[2], request[3], new byte[] { 0x00, 0x02, 0x00, 0x00 });
            };
            reading = new HidppSession(unifiedLevel).ReadBattery(3, 8, 0, 0, 200);
            Check("0x1004 百分比为 0 时用 level 枚举兜底", reading != null && reading.Percent == 20,
                Describe(reading));

            var legacy = new FakeChannel();
            legacy.Handler = delegate(byte[] request)
            {
                return LongReply(request[1], request[2], request[3], new byte[] { 45, 0x05, 0x01 });
            };
            reading = new HidppSession(legacy).ReadBattery(4, 0, 5, 0, 200);
            Check("0x1000 fn0 → 45% / Charging", reading != null && reading.Percent == 45
                  && reading.State == BatteryState.Charging && reading.Source == "0x1000", Describe(reading));

            var voltage = new FakeChannel();
            voltage.Handler = delegate(byte[] request)
            {
                return LongReply(request[1], request[2], request[3], new byte[] { 0x0F, 0x1C, 0x80 });
            };
            reading = new HidppSession(voltage).ReadBattery(5, 0, 0, 6, 200);
            Check("0x1001 fn0 → 3868mV / 72% / Charging", reading != null && reading.MilliVolts == 3868
                  && reading.Percent == 72 && reading.State == BatteryState.Charging
                  && reading.Source == "0x1001", Describe(reading));

            var noAnswer = new FakeChannel();
            reading = new HidppSession(noAnswer).ReadBattery(6, 8, 0, 0, 120);
            Check("无响应 → null（不抛异常）", reading == null);
        }

        private static void TestHidpp10Fallback()
        {
            Section("HID++ 1.0 寄存器回退");

            var channel = new FakeChannel();
            channel.Handler = delegate(byte[] request)
            {
                if (request[2] == Hidpp.SubIdRegisterReadReply && request[3] == 0x07)
                    return new byte[] { 0x10, request[1], 0x81, 0x07, 55, 50, 0x00 };
                return null;
            };
            BatteryReading reading = new HidppSession(channel).ReadBatteryHidpp10(3, 200);
            Check("寄存器 0x07 → 55% / Discharging", reading != null && reading.Percent == 55
                  && reading.State == BatteryState.Discharging, Describe(reading));
        }

        private static void TestPing()
        {
            Section("在线探测");

            var online = new FakeChannel();
            online.Handler = delegate(byte[] request)
            {
                return new byte[] { 0x10, request[1], 0x00, 0x04, 0x01, 0x03, 0x00 };
            };
            Check("HID++1 device info → 在线", new HidppSession(online).Ping(1, 200));

            var empty = new FakeChannel();
            empty.Handler = delegate(byte[] request)
            {
                return new byte[] { 0x10, request[1], 0x8F, 0x00, 0x00, 0x00, 0x00 };
            };
            Check("0x8F 寄存器错误 → 槽位为空", !new HidppSession(empty).Ping(3, 200));

            var silent = new FakeChannel();
            var clock = Stopwatch.StartNew();
            bool alive = new HidppSession(silent).Ping(5, 150);
            clock.Stop();
            Check("超时 → 不在线且不卡死", !alive && clock.ElapsedMilliseconds < 900,
                clock.ElapsedMilliseconds + " ms");
        }

        private static void TestKindMapping()
        {
            Section("设备类型映射");

            Check("3 → 鼠标", Hidpp.MapDeviceType(3) == DeviceKind.Mouse);
            Check("0 → 键盘", Hidpp.MapDeviceType(0) == DeviceKind.Keyboard);
            Check("2 → 数字键盘", Hidpp.MapDeviceType(2) == DeviceKind.Numpad);
            Check("6 → 演示器", Hidpp.MapDeviceType(6) == DeviceKind.Presenter);
            Check("8 → 耳机", Hidpp.MapDeviceType(8) == DeviceKind.Headset);
            Check("12 → 手柄", Hidpp.MapDeviceType(12) == DeviceKind.Gamepad);
            Check("4（触摸板）→ 其它", Hidpp.MapDeviceType(4) == DeviceKind.Other);
            Check("99（未知）→ 其它", Hidpp.MapDeviceType(99) == DeviceKind.Other);
            Check("名称兜底: 'MX Master Mouse' → 鼠标",
                Hidpp.GuessKindFromName("MX Master Mouse") == DeviceKind.Mouse);
        }

        private static void TestStandardHidScaling()
        {
            Section("标准 HID 电量换算");

            Check("0..100 直接使用", StandardHidBatteryProvider.ToPercent(55, Range(0, 100)) == 55);
            Check("0..255 除以 2.55 (255→100)", StandardHidBatteryProvider.ToPercent(255, Range(0, 255)) == 100);
            Check("0..255 除以 2.55 (128→50)", StandardHidBatteryProvider.ToPercent(128, Range(0, 255)) == 50);
            Check("1..5 线性换算 (5→100)", StandardHidBatteryProvider.ToPercent(5, Range(1, 5)) == 100);
            Check("1..5 线性换算 (3→50)", StandardHidBatteryProvider.ToPercent(3, Range(1, 5)) == 50);
            Check("负值 → null", StandardHidBatteryProvider.ToPercent(-1, Range(0, 100)) == null);
            Check("无 caps 且 >100 → null", StandardHidBatteryProvider.ToPercent(200, null) == null);
        }

        private static void TestStandardHidEndToEnd()
        {
            Section("标准 HID Provider 端到端（假总线）");

            const string instance = @"HID\VID_0000&PID_0000\fake-hid";

            var mouseCollection = new HidInterface();
            mouseCollection.Path = "fake://hid-mouse";
            mouseCollection.InstanceId = instance;
            mouseCollection.Vid = 0x1234;
            mouseCollection.Pid = 0x5678;
            mouseCollection.UsagePage = 0x0001;
            mouseCollection.Usage = 0x0002;
            mouseCollection.Product = "Fake HID Battery Device";

            var batteryCollection = new HidInterface();
            batteryCollection.Path = "fake://hid-battery";
            batteryCollection.InstanceId = instance;
            batteryCollection.Vid = 0x1234;
            batteryCollection.Pid = 0x5678;
            batteryCollection.UsagePage = 0x0006;
            batteryCollection.Usage = 0x0020;
            batteryCollection.Product = "Fake HID Battery Device";
            batteryCollection.FeatureReportLen = 5;
            batteryCollection.FeatureUsages.Add(new HidUsageRange
            {
                UsagePage = 0x0006,
                UsageMin = 0x0020,
                UsageMax = 0x0020,
                ReportId = 3,
                IsRange = false,
                BitSize = 8,
                LogicalMin = 0,
                LogicalMax = 255
            });

            var channel = new FakeChannel();
            channel.UsageAvailable = true;
            channel.UsageRaw = 128;
            channel.UsageCaps = batteryCollection.FeatureUsages[0];

            var bus = new FakeBus();
            bus.Interfaces.Add(mouseCollection);
            bus.Interfaces.Add(batteryCollection);
            bus.Opener = delegate(HidInterface iface) { return channel; };

            var config = new AppConfig();
            config.EnableStandardHid = true;
            var provider = new StandardHidBatteryProvider(config, new ProbeContext(config, false));

            HidBus.TestOverride = bus;
            List<Peripheral> found;
            try
            {
                found = provider.Scan(CancellationToken.None);
            }
            finally
            {
                HidBus.TestOverride = null;
            }

            Check("1 台设备（只返回读到电量的）", found.Count == 1, "实际 " + found.Count);
            if (found.Count == 1)
            {
                Peripheral p = found[0];
                Check("128/255 → 50%", p.Percent == 50, p.Percent + "%");
                Check("同实例的鼠标集合 → Kind=Mouse", p.Kind == DeviceKind.Mouse, p.Kind.ToString());
                Check("连接类型 → 2.4G", p.Connection == "2.4G", p.Connection);
                Check("厂商名保留", p.Name == "Fake HID Battery Device", p.Name);
            }
        }

        private static void TestLogitechEndToEnd()
        {
            Section("罗技 HID++ Provider 端到端（假接收器）");

            var receiver = new HidInterface();
            receiver.Path = "fake://receiver";
            receiver.InstanceId = @"HID\VID_046D&PID_0000&MI_02\fake";
            receiver.Vid = 0x046D;
            receiver.Pid = 0x0000;
            receiver.Product = "Fake Unifying Receiver";
            receiver.UsagePage = 0xFF00;
            receiver.Usage = 0x0001;
            receiver.InputReportLen = 20;
            receiver.OutputReportLen = 20;
            receiver.FeatureReportLen = 20;

            var bus = new FakeBus();
            bus.Interfaces.Add(receiver);
            bus.Opener = delegate(HidInterface iface)
            {
                var channel = new FakeChannel();
                channel.Handler = new Func<byte[], byte[]>(FakeReceiverHandler);
                return channel;
            };

            var config = new AppConfig();
            config.EnableLogitech = true;
            var provider = new LogitechHidppProvider(config, new ProbeContext(config, false));

            HidBus.TestOverride = bus;
            List<Peripheral> found;
            var clock = Stopwatch.StartNew();
            try
            {
                found = provider.Scan(CancellationToken.None);
            }
            finally
            {
                HidBus.TestOverride = null;
                clock.Stop();
            }

            Check("设备数 = 2（索引 3..8 槽位为空）", found.Count == 2, "实际 " + found.Count);

            Peripheral mouse = Find(found, "Fake Mouse");
            Peripheral keyboard = Find(found, "Fake Keyboard");
            Check("鼠标 64% / Discharging / Mouse", mouse != null && mouse.Percent == 64
                  && mouse.State == BatteryState.Discharging && mouse.Kind == DeviceKind.Mouse, Describe(mouse));
            Check("键盘 88% / Charging / Keyboard", keyboard != null && keyboard.Percent == 88
                  && keyboard.State == BatteryState.Charging && keyboard.Kind == DeviceKind.Keyboard,
                Describe(keyboard));

            if (mouse != null)
            {
                Check("Id 形如 logi:<接收器路径>:<索引>",
                    mouse.Id != null && mouse.Id.StartsWith("logi:fake://receiver:"), mouse.Id);
                Check("Brand=罗技 / Connection=2.4G", mouse.Brand == "罗技" && mouse.Connection == "2.4G",
                    mouse.Brand + " / " + mouse.Connection);
                Check("DevicePath 保留", mouse.DevicePath == "fake://receiver", mouse.DevicePath);
            }
            Check("整轮扫描 < 4000 ms", clock.ElapsedMilliseconds < 4000, clock.ElapsedMilliseconds + " ms");
        }

        private static void TestNoReceiverIsFast()
        {
            Section("无接收器快速返回");

            var bus = new FakeBus();
            var config = new AppConfig();
            config.EnableLogitech = true;
            var provider = new LogitechHidppProvider(config, new ProbeContext(config, false));

            HidBus.TestOverride = bus;
            List<Peripheral> found;
            var clock = Stopwatch.StartNew();
            try
            {
                found = provider.Scan(CancellationToken.None);
            }
            finally
            {
                HidBus.TestOverride = null;
                clock.Stop();
            }
            Check("无罗技集合 → 空列表且 < 300 ms", found.Count == 0 && clock.ElapsedMilliseconds < 300,
                found.Count + " 台 / " + clock.ElapsedMilliseconds + " ms");
        }

        private static Peripheral Find(List<Peripheral> list, string name)
        {
            foreach (Peripheral p in list)
                if (p.Name == name) return p;
            return null;
        }

        private static HidUsageRange Range(int min, int max)
        {
            var caps = new HidUsageRange();
            caps.LogicalMin = min;
            caps.LogicalMax = max;
            caps.BitSize = 8;
            caps.UsagePage = 0x0006;
            caps.UsageMin = 0x0020;
            return caps;
        }

        private static void Section(string title)
        {
            if (_json) return;
            Console.WriteLine();
            Console.WriteLine("-- " + title + " --");
        }

        private static void Check(string name, bool ok)
        {
            Check(name, ok, null);
        }

        private static void Check(string name, bool ok, string detail)
        {
            if (ok) _pass++; else _fail++;
            if (_json) return;
            Console.WriteLine("  " + (ok ? "PASS" : "FAIL") + "  " + name
                              + (string.IsNullOrEmpty(detail) ? "" : "   [" + detail + "]"));
        }

        private static string Hex(byte[] data)
        {
            if (data == null) return "null";
            var sb = new StringBuilder();
            for (int i = 0; i < data.Length; i++)
            {
                if (i > 0) sb.Append(' ');
                sb.Append(data[i].ToString("X2"));
            }
            return sb.ToString();
        }

        private static string Describe(BatteryReading reading)
        {
            return reading == null ? "null" : reading.ToString();
        }

        private static string Describe(Peripheral p)
        {
            if (p == null) return "null";
            return p.Name + " / " + (p.Percent.HasValue ? p.Percent.Value + "%" : "-")
                   + " / " + p.State + " / " + p.Kind;
        }

        private static string Q(string value)
        {
            return value == null ? "(null)" : "'" + value + "'";
        }

        private static string JsonSerialize(object value)
        {
            var serializer = new JavaScriptSerializer();
            serializer.MaxJsonLength = 4 * 1024 * 1024;
            return serializer.Serialize(value);
        }

        private static int DisplayWidth(string text)
        {
            if (string.IsNullOrEmpty(text)) return 0;
            int width = 0;
            foreach (char c in text)
                width += c >= 0x2E80 ? 2 : 1;
            return width;
        }

        private static string Pad(string text, int width)
        {
            if (text == null) text = "";
            int padding = width - DisplayWidth(text);
            return padding <= 0 ? text : text + new string(' ', padding);
        }

        // ============================ 假总线 / 假通道 ============================

        private sealed class FakeBus : IHidBus
        {
            public readonly List<HidInterface> Interfaces = new List<HidInterface>();
            public Func<HidInterface, IHidChannel> Opener;

            public List<HidInterface> Enumerate()
            {
                return new List<HidInterface>(Interfaces);
            }

            public IHidChannel Open(HidInterface iface)
            {
                if (Opener == null) throw new InvalidOperationException("FakeBus.Opener 未设置");
                return Opener(iface);
            }
        }

        private sealed class FakeChannel : IHidChannel, IHidUsageReader
        {
            public Func<byte[], byte[]> Handler;
            public bool UsageAvailable;
            public int UsageRaw;
            public HidUsageRange UsageCaps;
            public readonly List<byte[]> Requests = new List<byte[]>();

            private readonly Queue<byte[]> _pending = new Queue<byte[]>();

            public HidInterface Info { get; set; }

            public bool Write(byte[] report)
            {
                Requests.Add(report);
                if (Handler != null)
                {
                    byte[] response = Handler(report);
                    if (response != null) _pending.Enqueue(response);
                }
                return true;
            }

            public bool WriteInterrupt(byte[] report)
            {
                return Write(report);
            }

            public byte[] Read(int timeoutMs)
            {
                return _pending.Count > 0 ? _pending.Dequeue() : null;
            }

            public byte[] GetFeature(byte reportId, int length)
            {
                return null;
            }

            public bool SetFeature(byte[] report)
            {
                return false;
            }

            public bool TryReadUsageValue(ushort usagePage, ushort usage, out int rawValue, out HidUsageRange caps)
            {
                rawValue = UsageRaw;
                caps = UsageCaps;
                return UsageAvailable;
            }

            public void Dispose()
            {
            }
        }

        private static byte[] ShortReply(int device, int feature, int functionByte, params byte[] payload)
        {
            var report = new byte[7];
            report[0] = 0x10;
            report[1] = (byte)device;
            report[2] = (byte)feature;
            report[3] = (byte)functionByte;
            for (int i = 0; i < payload.Length && i < 3; i++) report[4 + i] = payload[i];
            return report;
        }

        private static byte[] LongReply(int device, int feature, int functionByte, byte[] payload)
        {
            var report = new byte[20];
            report[0] = 0x11;
            report[1] = (byte)device;
            report[2] = (byte)feature;
            report[3] = (byte)functionByte;
            if (payload != null)
                for (int i = 0; i < payload.Length && i < 16; i++) report[4 + i] = payload[i];
            return report;
        }

        private static string FakeName(int device)
        {
            return device == 1 ? "Fake Mouse" : "Fake Keyboard";
        }

        /// <summary>模拟一个 Unifying 接收器：索引 1=鼠标 64%/放电，索引 2=键盘 88%/充电，3..8 槽位为空。</summary>
        private static byte[] FakeReceiverHandler(byte[] request)
        {
            if (request == null || request.Length < 7) return null;
            int device = request[1];
            int feature = request[2];
            int functionByte = request[3];
            int function = (functionByte >> 4) & 0x0F;

            // HID++ 1.0 device info（全零短请求）：在线的设备回协议版本，空槽回 0x8F
            if (feature == 0x00 && functionByte == 0x00
                && request[4] == 0 && request[5] == 0 && request[6] == 0)
            {
                if (device == 1 || device == 2)
                    return new byte[] { 0x10, (byte)device, 0x00, 0x04, 0x01, 0x03, 0x00 };
                return new byte[] { 0x10, (byte)device, 0x8F, 0x00, 0x00, 0x00, 0x00 };
            }

            if (device != 1 && device != 2)
                return new byte[] { 0x10, (byte)device, 0x8F, 0x00, 0x00, 0x00, 0x00 };

            // ROOT(0) getFeature
            if (feature == 0x00 && function == 0)
            {
                int featureId = (request[4] << 8) | request[5];
                int index = 0;
                if (featureId == 0x0005) index = 1;
                else if (featureId == 0x1004) index = 8;
                return LongReply(device, 0x00, functionByte, new byte[] { (byte)index, 0x00, 0x00 });
            }

            // ROOT(0) ping，原样回显
            if (feature == 0x00 && function == 1)
                return LongReply(device, 0x00, functionByte,
                    new byte[] { request[4], request[5], request[6] });

            // 0x0005 设备名/类型
            if (feature == 0x01)
            {
                string name = FakeName(device);
                if (function == 0)
                    return ShortReply(device, 0x01, functionByte, (byte)name.Length, 0x00, 0x00);
                if (function == 1)
                {
                    int offset = request[4];
                    var payload = new byte[3];
                    for (int i = 0; i < 3; i++)
                    {
                        int position = offset + i;
                        payload[i] = position < name.Length ? (byte)name[position] : (byte)0;
                    }
                    return ShortReply(device, 0x01, functionByte, payload[0], payload[1], payload[2]);
                }
                if (function == 2)
                    return ShortReply(device, 0x01, functionByte, (byte)(device == 1 ? 3 : 0), 0x00, 0x00);
            }

            // 0x1004 getBatteryInfo
            if (feature == 0x08 && function == 1)
            {
                int percent = device == 1 ? 64 : 88;
                int status = device == 1 ? 0 : 1;
                int external = device == 1 ? 0 : 1;
                return LongReply(device, 0x08, functionByte,
                    new byte[] { (byte)percent, 0x04, (byte)status, (byte)external });
            }

            return LongReply(device, feature, functionByte, new byte[] { 0x00 });
        }

        private sealed class ProbeContext : IAppContext
        {
            private readonly AppConfig _config;
            private readonly bool _verbose;

            public ProbeContext(AppConfig config, bool verbose)
            {
                _config = config;
                _verbose = verbose;
            }

            public AppConfig Config
            {
                get { return _config; }
            }

            public void Log(string message)
            {
                if (_verbose) Console.WriteLine("    · " + message);
            }
        }
    }
}