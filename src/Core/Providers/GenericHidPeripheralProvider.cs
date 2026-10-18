using System;
using System.Collections.Generic;
using System.Threading;
using Pb.Core.Hid;

namespace Pb.Core.Providers
{
    /// <summary>
    /// 通用外设识别：不论品牌，把 HID 键鼠/手柄/耳机列出来（电量未知）。
    /// 只负责"看得见、分得清类别、可重命名"；电量仍由各专用 Provider 提供。
    /// </summary>
    public sealed class GenericHidPeripheralProvider : IProvider
    {
        private readonly AppConfig _config;
        private readonly IAppContext _ctx;

        public GenericHidPeripheralProvider(AppConfig config, IAppContext ctx)
        {
            _config = config;
            _ctx = ctx;
        }

        public string Key { get { return "generic"; } }
        public string DisplayName { get { return "通用外设识别"; } }
        public bool Enabled { get { return true; } }

        public List<Peripheral> Scan(CancellationToken ct)
        {
            var result = new List<Peripheral>();
            List<HidInterface> all;
            try
            {
                IHidBus bus = HidBus.Current(delegate(string m)
                {
                    if (_config.VerboseLog) _ctx.Log("[generic] " + m);
                });
                all = bus.Enumerate();
            }
            catch (Exception ex)
            {
                _ctx.Log("[generic] 枚举失败: " + ex.Message);
                return result;
            }

            var groups = new Dictionary<string, Group>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < all.Count; i++)
            {
                HidInterface it = all[i];
                try
                {
                    if (!IsCandidate(it)) continue;
                    string key = it.Vid.ToString("X4") + ":" + it.Pid.ToString("X4");
                    Group g;
                    if (!groups.TryGetValue(key, out g))
                    {
                        g = new Group();
                        groups[key] = g;
                    }
                    g.Items.Add(it);
                }
                catch { }
            }

            foreach (KeyValuePair<string, Group> kv in groups)
            {
                try
                {
                    Peripheral p = kv.Value.Build(_config);
                    if (p != null) result.Add(p);
                }
                catch (Exception ex)
                {
                    if (_config.VerboseLog) _ctx.Log("[generic] 构建设备失败: " + ex.Message);
                }
            }
            return result;
        }

        public void Dispose()
        {
        }

        private static bool IsCandidate(HidInterface it)
        {
            if (it == null) return false;
            // 已有专用 Provider 负责的厂商
            if (it.Vid == 0x046D || it.Vid == 0x045E) return false;
            // 蓝牙设备由蓝牙 Provider 负责
            string inst = it.InstanceId ?? "";
            if (inst.IndexOf("BTHLE", StringComparison.OrdinalIgnoreCase) >= 0) return false;
            if (inst.IndexOf("BTHENUM", StringComparison.OrdinalIgnoreCase) >= 0) return false;
            if (inst.IndexOf("TENCENT", StringComparison.OrdinalIgnoreCase) >= 0) return false;
            if (it.Vid == 0xFFAA) return false;

            string text = (it.Product ?? "") + "|" + (it.DeviceDesc ?? "") + "|" + inst;
            string[] bad = { "Virtual", "Tencent", "HIDI2C", "RDP_", "VMware", "vmmouse" };
            for (int i = 0; i < bad.Length; i++)
                if (text.IndexOf(bad[i], StringComparison.OrdinalIgnoreCase) >= 0) return false;

            bool isPeripheral = it.Service == "kbdhid" || it.Service == "mouhid"
                || (it.UsagePage == 0x0001 && (it.Usage == 0x02 || it.Usage == 0x06 || it.Usage == 0x04 || it.Usage == 0x05))
                || (it.UsagePage == 0x000C && it.Usage == 0x0001)
                || (it.UsagePage == 0x000D && it.Usage == 0x0005);
            if (!isPeripheral) return false;

            // 通用识别只收“看起来是无线外设”的设备，避免把有线耳机、内置触控板等全部列出来
            string hint = (it.Product ?? "") + " " + (it.DeviceDesc ?? "") + " " + (it.Manufacturer ?? "");
            return HasWirelessHint(hint);
        }

        private sealed class Group
        {
            public readonly List<HidInterface> Items = new List<HidInterface>();

            public Peripheral Build(AppConfig config)
            {
                if (Items.Count == 0) return null;
                HidInterface first = Items[0];

                string name = PickName();
                DeviceKind kind = PickKind();
                string product = first.Product ?? "";
                string brand = BrandOf(product, first.Manufacturer);
                string connection = Is24G(product) || Is24G(name) ? "2.4G" : "USB";

                var p = new Peripheral();
                p.Id = "generic:" + first.Vid.ToString("X4") + ":" + first.Pid.ToString("X4");
                p.PhysicalId = p.Id;
                p.Name = name;
                p.Brand = brand;
                p.Kind = kind;
                p.Connection = connection;
                p.Percent = null;
                p.MilliVolts = null;
                p.State = BatteryState.Unknown;
                p.Provider = "通用外设识别";
                p.Detail = "通用识别：该设备未暴露电量协议，可右键/详情里重命名";
                p.DevicePath = first.Path;
                p.Vid = first.Vid;
                p.Pid = first.Pid;
                p.Updated = DateTime.Now;
                p.IsGeneric = true;
                return p;
            }

            private string PickName()
            {
                string best = null;
                for (int i = 0; i < Items.Count; i++)
                {
                    HidInterface it = Items[i];
                    string n = Clean(it.Product);
                    if (string.IsNullOrEmpty(n) || n.StartsWith("HID-compliant", StringComparison.OrdinalIgnoreCase)) continue;
                    if (best == null || n.Length > best.Length) best = n;
                }
                if (!string.IsNullOrEmpty(best)) return Trim(best, 60);
                for (int i = 0; i < Items.Count; i++)
                {
                    string n = Clean(Items[i].DeviceDesc);
                    if (string.IsNullOrEmpty(n)) continue;
                    if (best == null || n.Length > best.Length) best = n;
                }
                if (!string.IsNullOrEmpty(best)) return Trim(best, 60);
                return "2.4G 外设";
            }

            private DeviceKind PickKind()
            {
                string products = "";
                string descs = "";
                for (int i = 0; i < Items.Count; i++)
                {
                    products += " " + (Items[i].Product ?? "");
                    descs += " " + (Items[i].DeviceDesc ?? "");
                }

                // 产品名优先（“VGN Mouse 2.4G Receiver” → 鼠标；“VGN V98pro V2 2.4G Dongle” → 看 usage）
                if (Contains(products, "mouse") || Contains(products, "鼠标")) return DeviceKind.Mouse;
                if (Contains(products, "keyboard") || Contains(products, "键盘")) return DeviceKind.Keyboard;
                if (Contains(products, "headset") || Contains(products, "耳机")) return DeviceKind.Headset;
                if (Contains(products, "gamepad") || Contains(products, "手柄") || Contains(products, "controller")) return DeviceKind.Gamepad;

                bool mouse = false, keyboard = false, gamepad = false;
                for (int i = 0; i < Items.Count; i++)
                {
                    HidInterface it = Items[i];
                    if (it.UsagePage == 0x0001 && it.Usage == 0x02) mouse = true;
                    if (it.UsagePage == 0x0001 && it.Usage == 0x06) keyboard = true;
                    if (it.UsagePage == 0x0001 && (it.Usage == 0x05 || it.Usage == 0x04)) gamepad = true;
                    if (it.Service == "mouhid") mouse = true;
                    if (it.Service == "kbdhid") keyboard = true;
                }
                if (keyboard) return DeviceKind.Keyboard;
                if (mouse) return DeviceKind.Mouse;
                if (gamepad) return DeviceKind.Gamepad;

                if (Contains(descs, "mouse") || Contains(descs, "鼠标")) return DeviceKind.Mouse;
                if (Contains(descs, "keyboard") || Contains(descs, "键盘")) return DeviceKind.Keyboard;
                return DeviceKind.Other;
            }
        }

        private static string Clean(string s)
        {
            if (string.IsNullOrEmpty(s)) return s;
            int semi = s.LastIndexOf(';');
            if (semi >= 0 && semi + 1 < s.Length) s = s.Substring(semi + 1);
            return s.Trim();
        }

        private static string Trim(string s, int max)
        {
            if (s.Length <= max) return s;
            return s.Substring(0, max - 1) + "…";
        }

        private static bool HasWirelessHint(string text)
        {
            if (string.IsNullOrEmpty(text)) return false;
            string[] hints = { "2.4G", "2.4g", "Wireless", "无线", "Dongle", "Receiver", "接收器" };
            for (int i = 0; i < hints.Length; i++)
                if (text.IndexOf(hints[i], StringComparison.OrdinalIgnoreCase) >= 0) return true;
            return false;
        }

        private static bool Is24G(string s)
        {
            if (string.IsNullOrEmpty(s)) return false;
            return s.IndexOf("2.4G", StringComparison.OrdinalIgnoreCase) >= 0
                || s.IndexOf("2.4G", StringComparison.OrdinalIgnoreCase) >= 0
                || s.IndexOf("Dongle", StringComparison.OrdinalIgnoreCase) >= 0
                || s.IndexOf("Receiver", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static bool Contains(string text, string word)
        {
            return text.IndexOf(word, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static string BrandOf(string product, string manufacturer)
        {
            if (!string.IsNullOrEmpty(product))
            {
                int sp = product.IndexOf(' ');
                string token = sp > 0 ? product.Substring(0, sp) : product;
                token = token.Trim();
                if (token.Length >= 2 && token.Length <= 12 && IsBrandToken(token)) return token;
            }
            if (!string.IsNullOrEmpty(manufacturer))
            {
                string m = manufacturer.Trim();
                if (!IsGenericVendor(m)) return m;
            }
            return "";
        }

        private static bool IsBrandToken(string s)
        {
            for (int i = 0; i < s.Length; i++)
            {
                char c = s[i];
                if (!char.IsLetterOrDigit(c)) return false;
            }
            string[] generic = { "USB", "HID", "Wireless", "Generic", "HID-compliant", "2.4G" };
            for (int i = 0; i < generic.Length; i++)
                if (string.Equals(s, generic[i], StringComparison.OrdinalIgnoreCase)) return false;
            return true;
        }

        private static bool IsGenericVendor(string m)
        {
            string[] generic = { "Microsoft", "Compx", "RDR", "Realtek", "Standard", "Generic" };
            for (int i = 0; i < generic.Length; i++)
                if (string.Equals(m, generic[i], StringComparison.OrdinalIgnoreCase)) return true;
            return false;
        }
    }
}