using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;

namespace Pb.Core.Hid
{
    /// <summary>区分"设备访问异常（吞掉继续）"与"进程级致命异常（必须向上抛）"。</summary>
    internal static class ErrorPolicy
    {
        public static bool IsFatal(Exception ex)
        {
            return ex is OutOfMemoryException || ex is StackOverflowException || ex is ThreadAbortException;
        }
    }

    /// <summary>父设备（通常是 USB 接收器 / 蓝牙控制器）的附加信息，供诊断与命名使用。</summary>
    public sealed class HidParentInfo
    {
        public string InstanceId { get; set; }
        public string Name { get; set; }
    }

    /// <summary>
    /// SetupAPI 枚举 GUID_DEVINTERFACE_HID，并读取 HIDD_ATTRIBUTES / HIDP_CAPS / value caps。
    /// 单设备失败只跳过该设备，不影响整体；结果带短 TTL 缓存，避免多个 Provider 重复枚举。
    /// </summary>
    public sealed class HidBus : IHidBus
    {
        private const int EnumerateBudgetMs = 1400;
        private const int MaxInterfaces = 512;

        private static readonly object CacheSync = new object();
        private static List<HidInterface> _cache;
        private static DateTime _cacheAt;
        private static readonly Dictionary<string, HidParentInfo> ParentCache =
            new Dictionary<string, HidParentInfo>(StringComparer.OrdinalIgnoreCase);

        private readonly Action<string> _log;

        public HidBus()
        {
        }

        public HidBus(Action<string> log)
        {
            _log = log;
        }

        /// <summary>枚举结果缓存时长：两个 Provider 在同一轮扫描里共用一次枚举。</summary>
        public static int CacheTtlMs = 2500;

        /// <summary>
        /// 仅供自测：非 null 时所有 Provider 都从它取总线（假接收器端到端测试）。
        /// 生产路径永远是 null；只在自测进程里设置，进程内单线程赋值。
        /// </summary>
#pragma warning disable 0649 // 只在诊断工具（tools/Probe）里赋值；主程序编译时会看到"从未赋值"
        internal static volatile IHidBus TestOverride;
#pragma warning restore 0649

        /// <summary>Provider 统一入口：优先自测注入的总线。</summary>
        internal static IHidBus Current(Action<string> log)
        {
            IHidBus overridden = TestOverride;
            return overridden != null ? overridden : new HidBus(log);
        }

        public static void InvalidateCache()
        {
            lock (CacheSync)
            {
                _cache = null;
                _cacheAt = DateTime.MinValue;
                ParentCache.Clear();
            }
        }

        public List<HidInterface> Enumerate()
        {
            lock (CacheSync)
            {
                if (_cache != null && (DateTime.UtcNow - _cacheAt).TotalMilliseconds < CacheTtlMs)
                    return new List<HidInterface>(_cache);
            }
            List<HidInterface> fresh = EnumerateFresh();
            lock (CacheSync)
            {
                _cache = fresh;
                _cacheAt = DateTime.UtcNow;
                return new List<HidInterface>(fresh);
            }
        }

        /// <summary>跳过缓存，强制重新枚举（诊断工具使用）。</summary>
        public List<HidInterface> EnumerateFresh()
        {
            var result = new List<HidInterface>();
            var sw = Stopwatch.StartNew();
            IntPtr set = IntPtr.Zero;
            try
            {
                Guid guid = HidNative.GuidDevInterfaceHid;
                set = HidNative.SetupDiGetClassDevs(ref guid, IntPtr.Zero, IntPtr.Zero,
                    HidNative.DIGCF_PRESENT | HidNative.DIGCF_DEVICEINTERFACE);
                if (set == HidNative.InvalidHandleValue)
                {
                    Log("SetupDiGetClassDevs 失败，错误码 " + Marshal.GetLastWin32Error());
                    return result;
                }

                for (int index = 0; index < MaxInterfaces; index++)
                {
                    if (sw.ElapsedMilliseconds > EnumerateBudgetMs)
                    {
                        Log("HID 枚举超出 " + EnumerateBudgetMs + "ms 预算，已找到 " + result.Count + " 个接口，提前结束");
                        break;
                    }

                    var iface = new HidNative.SP_DEVICE_INTERFACE_DATA();
                    iface.cbSize = Marshal.SizeOf(typeof(HidNative.SP_DEVICE_INTERFACE_DATA));
                    if (!HidNative.SetupDiEnumDeviceInterfaces(set, IntPtr.Zero, ref guid, index, ref iface))
                        break;

                    try
                    {
                        HidInterface info = ReadInterface(set, ref iface);
                        if (info != null) result.Add(info);
                    }
                    catch (Exception ex)
                    {
                        // 单个设备失败不能拖垮整轮枚举
                        Log("读取 HID 接口失败（索引 " + index + "）: " + ex.Message);
                    }
                }
            }
            catch (Exception ex)
            {
                Log("HID 枚举异常: " + ex.Message);
            }
            finally
            {
                if (set != IntPtr.Zero && set != HidNative.InvalidHandleValue)
                    HidNative.SetupDiDestroyDeviceInfoList(set);
            }
            return result;
        }

        private HidInterface ReadInterface(IntPtr set, ref HidNative.SP_DEVICE_INTERFACE_DATA ifaceData)
        {
            int required = 0;
            HidNative.SetupDiGetDeviceInterfaceDetail(set, ref ifaceData, IntPtr.Zero, 0, ref required, IntPtr.Zero);
            if (required <= 4) return null;

            IntPtr detail = IntPtr.Zero;
            IntPtr devInfoPtr = IntPtr.Zero;
            IntPtr handle = IntPtr.Zero;
            IntPtr preparsed = IntPtr.Zero;
            try
            {
                var devInfo = new HidNative.SP_DEVINFO_DATA();
                devInfo.cbSize = Marshal.SizeOf(typeof(HidNative.SP_DEVINFO_DATA));
                devInfoPtr = Marshal.AllocHGlobal(devInfo.cbSize);
                Marshal.StructureToPtr(devInfo, devInfoPtr, false);

                detail = Marshal.AllocHGlobal(required);
                // x64 下 cbSize = 8（x86 为 6），DevicePath 从偏移 4 开始
                Marshal.WriteInt32(detail, IntPtr.Size == 8 ? 8 : 6);
                if (!HidNative.SetupDiGetDeviceInterfaceDetail(set, ref ifaceData, detail, required, ref required,
                        devInfoPtr))
                    return null;

                devInfo = (HidNative.SP_DEVINFO_DATA)Marshal.PtrToStructure(devInfoPtr,
                    typeof(HidNative.SP_DEVINFO_DATA));
                string path = Marshal.PtrToStringUni(new IntPtr(detail.ToInt64() + 4));
                if (string.IsNullOrEmpty(path)) return null;

                var sbInstance = new StringBuilder(1024);
                int instanceNeeded = 0;
                string instanceId = null;
                if (HidNative.SetupDiGetDeviceInstanceId(set, ref devInfo, sbInstance, sbInstance.Capacity,
                        ref instanceNeeded))
                    instanceId = HidNative.CleanString(sbInstance.ToString());

                var info = new HidInterface();
                info.Path = path;
                info.InstanceId = instanceId;
                info.DeviceDesc = HidNative.ReadEnumValue(instanceId, "DeviceDesc");
                string friendly = HidNative.ReadEnumValue(instanceId, "FriendlyName");
                if (string.IsNullOrEmpty(info.DeviceDesc)) info.DeviceDesc = friendly;
                if (string.IsNullOrEmpty(info.DeviceDesc))
                    info.DeviceDesc = HidNative.PropString(set, ref devInfo, 0x0C);
                info.Service = HidNative.ReadEnumValue(instanceId, "Service");

                // 只查询打开：即使设备被独占也能拿到属性/字符串
                bool opened = false;
                handle = HidNative.OpenDeviceQuery(path);
                if (handle != HidNative.InvalidHandleValue && handle != IntPtr.Zero)
                {
                    opened = true;
                }
                else
                {
                    handle = IntPtr.Zero;
                    Log("HID 设备打不开（权限或独占），仅使用注册表信息: " + path);
                }

                if (opened)
                {
                    var attributes = new HidNative.HIDD_ATTRIBUTES();
                    attributes.Size = HidNative.AttributesSize;
                    if (HidNative.HidD_GetAttributes(handle, ref attributes))
                    {
                        info.Vid = attributes.VendorID;
                        info.Pid = attributes.ProductID;
                        info.Version = attributes.VersionNumber;
                    }

                    info.Product = HidNative.GetProductString(handle);
                    info.Manufacturer = HidNative.GetManufacturerString(handle);
                    info.Serial = HidNative.GetSerialNumberString(handle);

                    if (HidNative.HidD_GetPreparsedData(handle, ref preparsed))
                    {
                        var caps = new HidNative.HIDP_CAPS();
                        if (HidNative.HidP_GetCaps(preparsed, ref caps) == HidNative.HIDP_STATUS_SUCCESS)
                        {
                            info.UsagePage = caps.UsagePage;
                            info.Usage = caps.Usage;
                            info.InputReportLen = caps.InputReportByteLength;
                            info.OutputReportLen = caps.OutputReportByteLength;
                            info.FeatureReportLen = caps.FeatureReportByteLength;
                            info.FeatureUsages = HidNative.ReadValueCaps(preparsed, HidNative.HIDP_FEATURE,
                                caps.NumberFeatureValueCaps);
                            info.InputUsages = HidNative.ReadValueCaps(preparsed, HidNative.HIDP_INPUT,
                                caps.NumberInputValueCaps);
                        }
                    }
                }
                else
                {
                    int vid;
                    int pid;
                    if (TryParseVidPid(instanceId, out vid, out pid))
                    {
                        info.Vid = vid;
                        info.Pid = pid;
                    }
                    else
                    {
                        // 没有 VID/PID 的节点（虚拟总线等）不参与设备列表
                        return null;
                    }
                }

                if (string.IsNullOrEmpty(info.Product))
                    info.Product = !string.IsNullOrEmpty(friendly) ? friendly : info.DeviceDesc;

                CacheParent(instanceId);
                return info;
            }
            finally
            {
                if (preparsed != IntPtr.Zero) HidNative.HidD_FreePreparsedData(preparsed);
                if (handle != IntPtr.Zero) HidNative.CloseHandle(handle);
                if (detail != IntPtr.Zero) Marshal.FreeHGlobal(detail);
                if (devInfoPtr != IntPtr.Zero) Marshal.FreeHGlobal(devInfoPtr);
            }
        }

        private static bool TryParseVidPid(string instanceId, out int vid, out int pid)
        {
            vid = 0;
            pid = 0;
            if (string.IsNullOrEmpty(instanceId)) return false;
            int vi = instanceId.IndexOf("VID_", StringComparison.OrdinalIgnoreCase);
            int pi = instanceId.IndexOf("PID_", StringComparison.OrdinalIgnoreCase);
            if (vi < 0 || pi < 0) return false;
            if (vi + 8 > instanceId.Length || pi + 8 > instanceId.Length) return false;
            if (!int.TryParse(instanceId.Substring(vi + 4, 4), System.Globalization.NumberStyles.HexNumber, null, out vid)) return false;
            if (!int.TryParse(instanceId.Substring(pi + 4, 4), System.Globalization.NumberStyles.HexNumber, null, out pid)) return false;
            return vid != 0 || pid != 0;
        }

        private static void CacheParent(string instanceId)
        {
            if (string.IsNullOrEmpty(instanceId)) return;
            lock (CacheSync)
            {
                if (ParentCache.ContainsKey(instanceId)) return;
            }

            string parentId;
            string parentName;
            HidNative.TryGetParentDevice(instanceId, out parentId, out parentName);
            var parent = new HidParentInfo();
            parent.InstanceId = parentId;
            parent.Name = parentName;
            lock (CacheSync)
            {
                ParentCache[instanceId] = parent;
            }
        }

        /// <summary>取父设备（接收器）实例与名称：优先 CfgMgr32，退化为注册表 ParentIdPrefix。</summary>
        public bool TryGetParent(HidInterface iface, out HidParentInfo info)
        {
            info = null;
            if (iface == null || string.IsNullOrEmpty(iface.InstanceId)) return false;
            lock (CacheSync)
            {
                if (ParentCache.TryGetValue(iface.InstanceId, out info) && info != null) return true;
            }
            CacheParent(iface.InstanceId);
            lock (CacheSync)
            {
                if (ParentCache.TryGetValue(iface.InstanceId, out info) && info != null) return true;
            }

            string prefix = HidNative.ReadEnumValue(iface.InstanceId, "ParentIdPrefix");
            if (!string.IsNullOrEmpty(prefix))
            {
                var fallback = new HidParentInfo();
                fallback.InstanceId = prefix;
                fallback.Name = null;
                info = fallback;
                return true;
            }
            return false;
        }

        public IHidChannel Open(HidInterface iface)
        {
            if (iface == null) throw new ArgumentNullException("iface");
            return new HidChannel(iface);
        }

        private void Log(string message)
        {
            if (_log != null)
            {
                try { _log(message); }
                catch { }
            }
        }
    }
}