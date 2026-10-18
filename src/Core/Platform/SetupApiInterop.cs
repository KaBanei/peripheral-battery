using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;

namespace Pb.Core.Platform
{
    /// <summary>
    /// SetupAPI / CfgMgr32 的最小互操作封装：只做两件事 —— 枚举系统中已存在的 PnP 设备节点、
    /// 读取设备属性（DEVPROPKEY）。
    /// 所有调用都自带错误码判断，调用方无需（也不应）假设设备一定存在。
    /// </summary>
    internal static class SetupApiInterop
    {
        public const int DIGCF_PRESENT = 0x02;
        public const int DIGCF_ALLCLASSES = 0x04;

        public const int ERROR_NO_MORE_ITEMS = 259;
        public const int ERROR_INSUFFICIENT_BUFFER = 122;

        // DEVPROPTYPE 常量（devpropdef.h）
        public const uint DEVPROP_TYPE_BYTE = 0x03;
        public const uint DEVPROP_TYPE_INT16 = 0x04;
        public const uint DEVPROP_TYPE_UINT16 = 0x05;
        public const uint DEVPROP_TYPE_INT32 = 0x06;
        public const uint DEVPROP_TYPE_UINT32 = 0x07;
        public const uint DEVPROP_TYPE_INT64 = 0x08;
        public const uint DEVPROP_TYPE_UINT64 = 0x09;
        public const uint DEVPROP_TYPE_BOOLEAN = 0x11;
        public const uint DEVPROP_TYPE_STRING = 0x12;

        [StructLayout(LayoutKind.Sequential)]
        public struct SP_DEVINFO_DATA
        {
            public int cbSize;
            public Guid ClassGuid;
            public int DevInst;
            public IntPtr Reserved;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct DEVPROPKEY
        {
            public Guid fmtid;
            public uint pid;

            public DEVPROPKEY(Guid fmtid, uint pid)
            {
                this.fmtid = fmtid;
                this.pid = pid;
            }
        }

        [DllImport("setupapi.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        public static extern IntPtr SetupDiGetClassDevs(IntPtr classGuid, string enumerator, IntPtr hwndParent, int flags);

        [DllImport("setupapi.dll", SetLastError = true)]
        public static extern bool SetupDiEnumDeviceInfo(IntPtr deviceInfoSet, uint memberIndex, ref SP_DEVINFO_DATA deviceInfoData);

        [DllImport("setupapi.dll", SetLastError = true)]
        public static extern bool SetupDiDestroyDeviceInfoList(IntPtr deviceInfoSet);

        [DllImport("setupapi.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        public static extern bool SetupDiGetDeviceInstanceId(
            IntPtr deviceInfoSet,
            ref SP_DEVINFO_DATA deviceInfoData,
            StringBuilder deviceInstanceId,
            int deviceInstanceIdSize,
            out int requiredSize);

        [DllImport("setupapi.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        public static extern bool SetupDiGetDeviceProperty(
            IntPtr deviceInfoSet,
            ref SP_DEVINFO_DATA deviceInfoData,
            ref DEVPROPKEY propertyKey,
            out uint propertyType,
            [In, Out] byte[] propertyBuffer,
            int propertyBufferSize,
            out int requiredSize,
            int flags);

        /// <summary>枚举所有已存在的设备节点，返回实例 ID 列表（最多 maxCount 个）。失败时返回空列表。</summary>
        public static List<string> EnumerateInstanceIds(int maxCount)
        {
            var ids = new List<string>();
            IntPtr set = SetupDiGetClassDevs(IntPtr.Zero, null, IntPtr.Zero, DIGCF_PRESENT | DIGCF_ALLCLASSES);
            if (set == IntPtr.Zero || set == new IntPtr(-1)) return ids;
            try
            {
                var data = new SP_DEVINFO_DATA();
                int size = Marshal.SizeOf(typeof(SP_DEVINFO_DATA));
                for (uint i = 0; i < (uint)maxCount; i++)
                {
                    data = new SP_DEVINFO_DATA();
                    data.cbSize = size;
                    if (!SetupDiEnumDeviceInfo(set, i, ref data)) break;

                    string id = GetInstanceId(set, ref data);
                    if (id != null) ids.Add(id);
                }
            }
            catch { }
            finally
            {
                try { SetupDiDestroyDeviceInfoList(set); } catch { }
            }
            return ids;
        }

        public static string GetInstanceId(IntPtr set, ref SP_DEVINFO_DATA data)
        {
            try
            {
                var sb = new StringBuilder(512);
                int required;
                if (SetupDiGetDeviceInstanceId(set, ref data, sb, sb.Capacity, out required))
                    return sb.ToString();
                if (required > sb.Capacity)
                {
                    sb = new StringBuilder(required + 2);
                    if (SetupDiGetDeviceInstanceId(set, ref data, sb, sb.Capacity, out required))
                        return sb.ToString();
                }
            }
            catch { }
            return null;
        }

        /// <summary>读取字符串型设备属性；类型不符或不存在时返回 null。</summary>
        public static string GetStringProperty(IntPtr set, ref SP_DEVINFO_DATA data, Guid fmtid, uint pid)
        {
            try
            {
                var key = new DEVPROPKEY(fmtid, pid);
                uint type = 0;
                int required = 0;
                // 注意：探测所需缓冲区大小时 Windows 返回 FALSE + ERROR_INSUFFICIENT_BUFFER，
                // 并把字节数写入 requiredSize。不能把 FALSE 直接当成"属性不存在"。
                bool ok = SetupDiGetDeviceProperty(set, ref data, ref key, out type, null, 0, out required, 0);
                int err = Marshal.GetLastWin32Error();
                if (!ok && err != ERROR_INSUFFICIENT_BUFFER) return null;
                if (required <= 0 || required > 64 * 1024) return null;
                if (type != DEVPROP_TYPE_STRING) return null;

                var buf = new byte[required + 2];
                int got = 0;
                if (!SetupDiGetDeviceProperty(set, ref data, ref key, out type, buf, buf.Length, out got, 0))
                    return null;
                int len = Math.Min(got > 0 ? got : required, buf.Length);
                string s = Encoding.Unicode.GetString(buf, 0, len);
                return s.TrimEnd('\0');
            }
            catch { return null; }
        }

        /// <summary>
        /// 读取整数型设备属性（BYTE / INT16 / UINT16 / INT32 / UINT32 / BOOLEAN / 数字字符串）。
        /// 属性不存在或类型无法解释时返回 null。用于蓝牙电量这类设备厂商属性。
        /// </summary>
        public static int? GetIntProperty(IntPtr set, ref SP_DEVINFO_DATA data, Guid fmtid, uint pid)
        {
            try
            {
                var key = new DEVPROPKEY(fmtid, pid);
                uint type = 0;
                int required = 0;
                bool ok = SetupDiGetDeviceProperty(set, ref data, ref key, out type, null, 0, out required, 0);
                int err = Marshal.GetLastWin32Error();
                if (!ok && err != ERROR_INSUFFICIENT_BUFFER) return null;
                if (required < 0 || required > 4096) return null;

                var buf = new byte[Math.Max(required, 8)];
                int got = 0;
                if (!SetupDiGetDeviceProperty(set, ref data, ref key, out type, buf, buf.Length, out got, 0))
                    return null;
                if (buf.Length < 8) return null;
                if (got > 0 && got < required) required = got;

                switch (type)
                {
                    case DEVPROP_TYPE_BYTE: return buf[0];
                    case DEVPROP_TYPE_BOOLEAN: return buf[0] == 0 ? 0 : 1;
                    case DEVPROP_TYPE_INT16: return BitConverter.ToInt16(buf, 0);
                    case DEVPROP_TYPE_UINT16: return BitConverter.ToUInt16(buf, 0);
                    case DEVPROP_TYPE_INT32: return BitConverter.ToInt32(buf, 0);
                    case DEVPROP_TYPE_UINT32:
                        {
                            uint v = BitConverter.ToUInt32(buf, 0);
                            return v > int.MaxValue ? (int?)null : (int)v;
                        }
                    case DEVPROP_TYPE_INT64: return (int)BitConverter.ToInt64(buf, 0);
                    case DEVPROP_TYPE_UINT64: return (int)BitConverter.ToUInt64(buf, 0);
                    case DEVPROP_TYPE_STRING:
                        {
                            int len = Math.Min(required, buf.Length);
                            string s = Encoding.Unicode.GetString(buf, 0, len).TrimEnd('\0').Trim();
                            int v;
                            if (int.TryParse(s, out v)) return v;
                            return null;
                        }
                    default:
                        return null;
                }
            }
            catch { return null; }
        }
    }
}
