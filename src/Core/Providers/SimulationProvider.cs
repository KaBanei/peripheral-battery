using System;
using System.Collections.Generic;
using System.Threading;
using Pb.Core.Hid;

namespace Pb.Core.Providers
{
    /// <summary>
    /// 演示模式：返回 3 个模拟设备，数值随每分钟缓慢变化。
    /// 仅在 config.EnableDemo = true 时工作，Simulated = true 便于 UI 标注。
    /// </summary>
    public sealed class SimulationProvider : IProvider
    {
        private const int LogitechVid = 0x046D;

        private readonly AppConfig _config;
        private readonly IAppContext _ctx;

        public SimulationProvider(AppConfig config, IAppContext ctx)
        {
            _config = config;
            _ctx = ctx;
        }

        public string Key
        {
            get { return "demo"; }
        }

        public string DisplayName
        {
            get { return "演示模式"; }
        }

        public bool Enabled
        {
            get { return _config != null && _config.EnableDemo; }
        }

        public List<Peripheral> Scan(CancellationToken ct)
        {
            var list = new List<Peripheral>();
            if (!Enabled) return list;

            int minute = DateTime.Now.Minute;
            int mouse = 78 + (int)Math.Round(Math.Sin(minute * 0.30) * 3.0);
            int keyboard = 45 + (minute % 6);   // 充电中缓慢上升
            int headset = 12 + (minute % 3);

            list.Add(Make("demo:logi-mouse", "罗技 MX Master 3", DeviceKind.Mouse, mouse, 3870,
                BatteryState.Discharging, 92, 0xB023));
            list.Add(Make("demo:logi-keyboard", "罗技 MX Keys", DeviceKind.Keyboard, keyboard, 4050,
                BatteryState.Charging, 88, 0xB35B));
            list.Add(Make("demo:logi-headset", "罗技 G733 耳机", DeviceKind.Headset, headset, 3620,
                BatteryState.Discharging, 74, 0x0A66));

            if (_ctx != null && _config != null && _config.VerboseLog)
                _ctx.Log("演示模式返回 " + list.Count + " 个模拟设备");
            return list;
        }

        private Peripheral Make(string id, string name, DeviceKind kind, int percent, int millivolts,
            BatteryState state, int signal, int pid)
        {
            var p = new Peripheral();
            p.Id = id;
            p.Name = name;
            p.Brand = "罗技";
            p.Kind = kind;
            p.Connection = "虚拟";
            p.Percent = Hidpp.ClampPercent(percent);
            p.MilliVolts = millivolts;
            p.State = state;
            p.Signal = signal;
            p.Provider = DisplayName;
            p.DevicePath = "demo://" + id;
            p.Vid = LogitechVid;
            p.Pid = pid;
            p.Updated = DateTime.Now;
            p.Simulated = true;
            p.Detail = "演示数据（每分钟缓慢变化）";
            return p;
        }

        public void Dispose()
        {
        }
    }
}
