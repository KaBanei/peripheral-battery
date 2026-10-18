using System;
using System.Collections.Generic;
using System.Threading;
using Pb.Core;

namespace Pb.App
{
    /// <summary>
    /// 仅在演示模式开启、且 Core 中尚不存在 SimulationProvider 时注册的兜底演示数据源，
    /// 保证界面（卡片/电量条/颜色）可独立验证。真实 SimulationProvider 就绪后自动不再注册。
    /// </summary>
    internal sealed class DemoFallbackProvider : IProvider
    {
        public string Key { get { return "demo-ui"; } }
        public string DisplayName { get { return "演示数据"; } }
        public bool Enabled { get { return true; } }

        public List<Peripheral> Scan(CancellationToken ct)
        {
            var list = new List<Peripheral>();
            DateTime now = DateTime.Now;
            list.Add(Make("demo-mouse", "MX Master 3S", "罗技", DeviceKind.Mouse, "2.4G", 78, 3710,
                BatteryState.Discharging, 88, 0x046D, 0xB034, "Logi Bolt 接收器",
                @"\\?\hid#vid_046d&pid_b034&mi_02#8&2f0b1c9a&0&0000#{4d1e55b2-f16f-11cf-88cb-001111000030}", now));
            list.Add(Make("demo-keyboard", "MX Keys", "罗技", DeviceKind.Keyboard, "蓝牙", 41, 3620,
                BatteryState.Discharging, 72, 0x046D, 0xB35B, "蓝牙低功耗",
                @"\\?\hid#{00001124-0000-1000-8000-00805f9b34fb}_vid&0002046d_pid&b35b#9&1a2b3c4d&0&0000#{4d1e55b2-f16f-11cf-88cb-001111000030}", now));
            list.Add(Make("demo-headset", "G733 无线耳机", "罗技", DeviceKind.Headset, "2.4G", 18, 3550,
                BatteryState.Low, 64, 0x046D, 0x0AB5, "Lightspeed 接收器",
                @"\\?\hid#vid_046d&pid_0ab5&mi_03#7&1c2d3e4f&0&0000#{4d1e55b2-f16f-11cf-88cb-001111000030}", now));
            list.Add(Make("demo-gamepad", "Xbox 无线控制器", "微软", DeviceKind.Gamepad, "2.4G", 55, 0,
                BatteryState.Charging, 91, 0x045E, 0x0B13, "XInput 无线适配器", "", now));
            if (ct.IsCancellationRequested) return new List<Peripheral>();
            return list;
        }

        private static Peripheral Make(string id, string name, string brand, DeviceKind kind, string conn,
            int percent, int mv, BatteryState state, int signal, int vid, int pid, string detail, string path, DateTime now)
        {
            return new Peripheral
            {
                Id = id,
                Name = name,
                Brand = brand,
                Kind = kind,
                Connection = conn,
                Percent = percent,
                MilliVolts = mv,
                State = state,
                Signal = signal,
                Provider = "演示数据",
                Detail = detail,
                DevicePath = path,
                Vid = vid,
                Pid = pid,
                Updated = now,
                Simulated = true
            };
        }

        public void Dispose() { }
    }
}
