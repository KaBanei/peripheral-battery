using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using Microsoft.Win32;

namespace Pb.Core.Hid
{
    /// <summary>
    /// SetupAPI / hid.dll / cfgmgr32 的原生互操作声明。
    /// 本机（x64）已实测：SP_DEVICE_INTERFACE_DETAIL_DATA.cbSize 必须写 8，
    /// DevicePath 起始偏移为 4；HIDP_VALUE_CAPS 为 72 字节；HidP_GetValueCaps 的
    /// ReportType 枚举为 Input=0 / Output=1 / Feature=2。
    /// </summary>
    internal static class HidNative
    {
        public static readonly Guid GuidDevInterfaceHid = new Guid("4D1E55B2-F16F-11CF-88CB-001111000030");

        public const int DIGCF_PRESENT = 0x02;
        public const int DIGCF_DEVICEINTERFACE = 0x10;

        public const uint GENERIC_READ = 0x80000000;
        public const uint GENERIC_WRITE = 0x40000000;
        public const uint FILE_SHARE_READ = 0x00000001;
        public const uint FILE_SHARE_WRITE = 0x00000002;
        public const uint OPEN_EXISTING = 3;
        public const uint FILE_FLAG_OVERLAPPED = 0x40000000;

        public const int ERROR_IO_PENDING = 997;
        public const int WAIT_OBJECT_0 = 0;
        public const int WAIT_TIMEOUT = 258;

        public const int HIDP_STATUS_SUCCESS = 0x00110000;
        public const int HIDP_INPUT = 0;
        public const int HIDP_OUTPUT = 1;
        public const int HIDP_FEATURE = 2;

        // CM_Get_DevNode_Registry_Property 的属性号
        public const int CM_DRP_DEVICEDESC = 0x00000000;
        public const int CM_DRP_FRIENDLYNAME = 0x0000000C;

        public static readonly IntPtr InvalidHandleValue = new IntPtr(-1);

        public static readonly int ValueCapsSize = Marshal.SizeOf(typeof(HIDP_VALUE_CAPS));
        public static readonly int CapsSize = Marshal.SizeOf(typeof(HIDP_CAPS));
        public static readonly int AttributesSize = Marshal.SizeOf(typeof(HIDD_ATTRIBUTES));

        [StructLayout(LayoutKind.Sequential)]
        public struct SP_DEVICE_INTERFACE_DATA
        {
            public int cbSize;
            public Guid InterfaceClassGuid;
            public int Flags;
            public IntPtr Reserved;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct SP_DEVINFO_DATA
        {
            public int cbSize;
            public Guid ClassGuid;
            public int DevInst;
            public IntPtr Reserved;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct HIDD_ATTRIBUTES
        {
            public int Size;
            public ushort VendorID;
            public ushort ProductID;
            public ushort VersionNumber;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct HIDP_CAPS
        {
            public ushort Usage;
            public ushort UsagePage;
            public ushort InputReportByteLength;
            public ushort OutputReportByteLength;
            public ushort FeatureReportByteLength;
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 17)]
            public ushort[] Reserved;
            public ushort NumberLinkCollectionNodes;
            public ushort NumberInputButtonCaps;
            public ushort NumberInputValueCaps;
            public ushort NumberInputDataIndices;
            public ushort NumberOutputButtonCaps;
            public ushort NumberOutputValueCaps;
            public ushort NumberOutputDataIndices;
            public ushort NumberFeatureButtonCaps;
            public ushort NumberFeatureValueCaps;
            public ushort NumberFeatureDataIndices;
        }

        /// <summary>
        /// HIDP_VALUE_CAPS。结尾 union 为 8 个 USHORT（Range/NotRange 共用同一块内存），
        /// 整个结构 72 字节；按 60 字节遍历会越界读并崩溃。
        /// </summary>
        [StructLayout(LayoutKind.Sequential, Pack = 1)]
        public struct HIDP_VALUE_CAPS
        {
            public ushort UsagePage;
            public byte ReportID;
            public byte IsAlias;
            public ushort BitField;
            public ushort LinkCollection;
            public ushort LinkUsage;
            public ushort LinkUsagePage;
            public byte IsRange;
            public byte IsStringRange;
            public byte IsDesignatorRange;
            public byte IsAbsolute;
            public byte HasNull;
            public byte Reserved;
            public ushort BitSize;
            public ushort ReportCount;
            public ushort Reserved2_0;
            public ushort Reserved2_1;
            public ushort Reserved2_2;
            public ushort Reserved2_3;
            public ushort Reserved2_4;
            public uint UnitsExp;
            public uint Units;
            public int LogicalMin;
            public int LogicalMax;
            public int PhysicalMin;
            public int PhysicalMax;
            // union 起点：Range = UsageMin/UsageMax/String... ；NotRange 时 UsageMin 即 Usage
            public ushort UsageMin;
            public ushort UsageMax;
            public ushort StringMin;
            public ushort StringMax;
            public ushort DesignatorMin;
            public ushort DesignatorMax;
            public ushort DataIndexMin;
            public ushort DataIndexMax;
        }

        // ---------------- setupapi ----------------

        [DllImport("setupapi.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        public static extern IntPtr SetupDiGetClassDevs(ref Guid classGuid, IntPtr enumerator, IntPtr hwndParent, int flags);

        [DllImport("setupapi.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        public static extern bool SetupDiEnumDeviceInterfaces(IntPtr deviceInfoSet, IntPtr deviceInfoData,
            ref Guid interfaceClassGuid, int memberIndex, ref SP_DEVICE_INTERFACE_DATA deviceInterfaceData);

        [DllImport("setupapi.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        public static extern bool SetupDiGetDeviceInterfaceDetail(IntPtr deviceInfoSet,
            ref SP_DEVICE_INTERFACE_DATA deviceInterfaceData, IntPtr deviceInterfaceDetailData,
            int deviceInterfaceDetailDataSize, ref int requiredSize, IntPtr deviceInfoData);

        [DllImport("setupapi.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        public static extern bool SetupDiGetDeviceInstanceId(IntPtr deviceInfoSet, ref SP_DEVINFO_DATA deviceInfoData,
            StringBuilder deviceInstanceId, int deviceInstanceIdSize, ref int requiredSize);

        [DllImport("setupapi.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        public static extern bool SetupDiGetDeviceRegistryProperty(IntPtr deviceInfoSet, ref SP_DEVINFO_DATA deviceInfoData,
            int property, ref int propertyRegDataType, byte[] propertyBuffer, int propertyBufferSize, ref int requiredSize);

        [DllImport("setupapi.dll", SetLastError = true)]
        public static extern bool SetupDiDestroyDeviceInfoList(IntPtr deviceInfoSet);

        // ---------------- hid.dll ----------------

        [DllImport("hid.dll", SetLastError = true)]
        public static extern bool HidD_GetAttributes(IntPtr hidDeviceObject, ref HIDD_ATTRIBUTES attributes);

        [DllImport("hid.dll", SetLastError = true)]
        public static extern bool HidD_GetPreparsedData(IntPtr hidDeviceObject, ref IntPtr preparsedData);

        [DllImport("hid.dll", SetLastError = true)]
        public static extern bool HidD_FreePreparsedData(IntPtr preparsedData);

        [DllImport("hid.dll", SetLastError = true)]
        public static extern bool HidD_GetFeature(IntPtr hidDeviceObject, byte[] reportBuffer, int reportBufferLength);

        [DllImport("hid.dll", SetLastError = true)]
        public static extern bool HidD_SetFeature(IntPtr hidDeviceObject, byte[] reportBuffer, int reportBufferLength);

        [DllImport("hid.dll", SetLastError = true)]
        public static extern bool HidD_SetOutputReport(IntPtr hidDeviceObject, byte[] reportBuffer, int reportBufferLength);

        [DllImport("hid.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        public static extern bool HidD_GetProductString(IntPtr hidDeviceObject, StringBuilder buffer, int bufferLength);

        [DllImport("hid.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        public static extern bool HidD_GetManufacturerString(IntPtr hidDeviceObject, StringBuilder buffer, int bufferLength);

        [DllImport("hid.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        public static extern bool HidD_GetSerialNumberString(IntPtr hidDeviceObject, StringBuilder buffer, int bufferLength);

        [DllImport("hid.dll")]
        public static extern int HidP_GetCaps(IntPtr preparsedData, ref HIDP_CAPS capabilities);

        [DllImport("hid.dll")]
        public static extern int HidP_GetValueCaps(int reportType, IntPtr valueCaps, ref int valueCapsLength,
            IntPtr preparsedData);

        [DllImport("hid.dll")]
        public static extern int HidP_GetUsageValue(int reportType, ushort usagePage, ushort linkCollection,
            ushort usage, out uint usageValue, IntPtr preparsedData, byte[] report, uint reportLength);

        // ---------------- kernel32 ----------------

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        public static extern IntPtr CreateFile(string fileName, uint desiredAccess, uint shareMode,
            IntPtr securityAttributes, uint creationDisposition, uint flagsAndAttributes, IntPtr templateFile);

        [DllImport("kernel32.dll", SetLastError = true)]
        public static extern bool CloseHandle(IntPtr handle);

        [DllImport("kernel32.dll", SetLastError = true)]
        public static extern bool ReadFile(IntPtr file, IntPtr buffer, int bytesToRead, out int bytesRead,
            ref NativeOverlapped overlapped);

        [DllImport("kernel32.dll", SetLastError = true)]
        public static extern bool WriteFile(IntPtr file, IntPtr buffer, int bytesToWrite, out int bytesWritten,
            ref NativeOverlapped overlapped);

        [DllImport("kernel32.dll", SetLastError = true)]
        public static extern bool GetOverlappedResult(IntPtr file, ref NativeOverlapped overlapped,
            out uint bytesTransferred, bool wait);

        [DllImport("kernel32.dll", SetLastError = true)]
        public static extern bool CancelIoEx(IntPtr file, ref NativeOverlapped overlapped);

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        public static extern IntPtr CreateEvent(IntPtr eventAttributes, bool manualReset, bool initialState,
            string name);

        [DllImport("kernel32.dll", SetLastError = true)]
        public static extern bool ResetEvent(IntPtr handle);

        [DllImport("kernel32.dll", SetLastError = true)]
        public static extern int WaitForSingleObject(IntPtr handle, uint milliseconds);

        // ---------------- cfgmgr32（父设备信息） ----------------

        [DllImport("cfgmgr32.dll", CharSet = CharSet.Unicode)]
        public static extern int CM_Locate_DevNodeW(out int devInst, string deviceId, int flags);

        [DllImport("cfgmgr32.dll")]
        public static extern int CM_Get_Parent(out int devInst, int devInstChild, int flags);

        [DllImport("cfgmgr32.dll", CharSet = CharSet.Unicode)]
        public static extern int CM_Get_Device_IDW(int devInst, StringBuilder buffer, int bufferLength, int flags);

        [DllImport("cfgmgr32.dll", CharSet = CharSet.Unicode)]
        public static extern int CM_Get_DevNode_Registry_PropertyW(int devInst, int property, out int regDataType,
            byte[] buffer, ref int length, int flags);

        // ---------------- 辅助 ----------------

        public static IntPtr OpenDevice(string path, bool overlapped)
        {
            uint flags = overlapped ? FILE_FLAG_OVERLAPPED : 0u;
            return CreateFile(path, GENERIC_READ | GENERIC_WRITE, FILE_SHARE_READ | FILE_SHARE_WRITE,
                IntPtr.Zero, OPEN_EXISTING, flags, IntPtr.Zero);
        }

        /// <summary>只查询（0 访问权限）打开，用于读属性/字符串，避免独占冲突。</summary>
        public static IntPtr OpenDeviceQuery(string path)
        {
            return CreateFile(path, 0, FILE_SHARE_READ | FILE_SHARE_WRITE, IntPtr.Zero, OPEN_EXISTING, 0,
                IntPtr.Zero);
        }

        public static string CleanString(string s)
        {
            if (s == null) return null;
            int end = s.IndexOf('\0');
            if (end >= 0) s = s.Substring(0, end);
            s = s.Trim();
            return s.Length == 0 ? null : s;
        }

        public static string GetProductString(IntPtr handle)
        {
            var sb = new StringBuilder(256);
            if (!HidD_GetProductString(handle, sb, sb.Capacity * 2)) return null;
            return CleanString(sb.ToString());
        }

        public static string GetManufacturerString(IntPtr handle)
        {
            var sb = new StringBuilder(256);
            if (!HidD_GetManufacturerString(handle, sb, sb.Capacity * 2)) return null;
            return CleanString(sb.ToString());
        }

        public static string GetSerialNumberString(IntPtr handle)
        {
            var sb = new StringBuilder(256);
            if (!HidD_GetSerialNumberString(handle, sb, sb.Capacity * 2)) return null;
            return CleanString(sb.ToString());
        }

        /// <summary>读取 HKLM\SYSTEM\CurrentControlSet\Enum\&lt;instanceId&gt; 下的字符串值。</summary>
        public static string ReadEnumValue(string instanceId, string valueName)
        {
            if (string.IsNullOrEmpty(instanceId)) return null;
            try
            {
                using (RegistryKey key = Registry.LocalMachine.OpenSubKey(
                    @"SYSTEM\CurrentControlSet\Enum\" + instanceId))
                {
                    if (key == null) return null;
                    object value = key.GetValue(valueName);
                    if (value == null) return null;
                    string s = value as string;
                    return CleanString(s);
                }
            }
            catch
            {
                return null;
            }
        }

        /// <summary>通过 CfgMgr32 找到父设备实例 ID 与友好名（如接收器名）。</summary>
        public static bool TryGetParentDevice(string instanceId, out string parentInstanceId, out string parentName)
        {
            parentInstanceId = null;
            parentName = null;
            if (string.IsNullOrEmpty(instanceId)) return false;
            try
            {
                int devInst;
                if (CM_Locate_DevNodeW(out devInst, instanceId, 0) != 0) return false;
                int parent;
                if (CM_Get_Parent(out parent, devInst, 0) != 0) return false;
                var sb = new StringBuilder(512);
                if (CM_Get_Device_IDW(parent, sb, sb.Capacity, 0) == 0) parentInstanceId = CleanString(sb.ToString());
                string friendly = GetDevNodeProperty(parent, CM_DRP_FRIENDLYNAME);
                string desc = GetDevNodeProperty(parent, CM_DRP_DEVICEDESC);
                parentName = friendly != null ? friendly : desc;
                return parentInstanceId != null || parentName != null;
            }
            catch
            {
                return false;
            }
        }

        private static string GetDevNodeProperty(int devInst, int property)
        {
            try
            {
                int regType;
                int length = 2048;
                byte[] buffer = new byte[length];
                if (CM_Get_DevNode_Registry_PropertyW(devInst, property, out regType, buffer, ref length, 0) != 0)
                    return null;
                if (length <= 2) return null;
                return CleanString(Encoding.Unicode.GetString(buffer, 0, length - 2));
            }
            catch
            {
                return null;
            }
        }

        /// <summary>读取某个报告类型的全部 value caps（Feature=2 / Input=0）。</summary>
        public static List<HidUsageRange> ReadValueCaps(IntPtr preparsedData, int reportType, int count)
        {
            var list = new List<HidUsageRange>();
            if (preparsedData == IntPtr.Zero || count <= 0) return list;

            int size = ValueCapsSize;
            int capacity = count;
            IntPtr buffer = IntPtr.Zero;
            try
            {
                for (int attempt = 0; attempt < 2; attempt++)
                {
                    if (buffer != IntPtr.Zero) Marshal.FreeHGlobal(buffer);
                    buffer = Marshal.AllocHGlobal(size * capacity);
                    int length = capacity;
                    int rc = HidP_GetValueCaps(reportType, buffer, ref length, preparsedData);
                    if (rc != HIDP_STATUS_SUCCESS) return list;
                    if (length > capacity)
                    {
                        capacity = length; // 驱动在两次调用之间报告了更多 caps，重试一次
                        continue;
                    }

                    for (int i = 0; i < length; i++)
                    {
                        var caps = (HIDP_VALUE_CAPS)Marshal.PtrToStructure(
                            new IntPtr(buffer.ToInt64() + (long)i * size), typeof(HIDP_VALUE_CAPS));
                        var range = new HidUsageRange();
                        range.UsagePage = caps.UsagePage;
                        range.UsageMin = caps.UsageMin;
                        range.UsageMax = caps.IsRange != 0 ? caps.UsageMax : caps.UsageMin;
                        range.ReportId = caps.ReportID;
                        range.IsRange = caps.IsRange != 0;
                        range.BitSize = caps.BitSize;
                        range.LogicalMin = caps.LogicalMin;
                        range.LogicalMax = caps.LogicalMax;
                        list.Add(range);
                    }
                    return list;
                }
                return list;
            }
            catch
            {
                return list;
            }
            finally
            {
                if (buffer != IntPtr.Zero) Marshal.FreeHGlobal(buffer);
            }
        }

        public static bool TryGetUsageValue(IntPtr preparsedData, int reportType, ushort usagePage, ushort usage,
            byte[] report, out int value)
        {
            value = 0;
            if (preparsedData == IntPtr.Zero || report == null || report.Length == 0) return false;
            uint raw;
            int rc = HidP_GetUsageValue(reportType, usagePage, 0, usage, out raw, preparsedData, report,
                (uint)report.Length);
            if (rc != HIDP_STATUS_SUCCESS) return false;
            value = unchecked((int)raw);
            return true;
        }

        public static string PropString(IntPtr deviceInfoSet, ref SP_DEVINFO_DATA deviceInfoData, int property)
        {
            var buffer = new byte[1024];
            int type = 0;
            int needed = 0;
            if (!SetupDiGetDeviceRegistryProperty(deviceInfoSet, ref deviceInfoData, property, ref type, buffer,
                buffer.Length, ref needed))
                return null;
            if (needed <= 2) return null;
            return CleanString(Encoding.Unicode.GetString(buffer, 0, needed - 2));
        }
    }
}
