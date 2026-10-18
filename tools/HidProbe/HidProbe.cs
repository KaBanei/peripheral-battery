using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32;

class HidProbe
{
    static Guid GUID_DEVINTERFACE_HID = new Guid("4D1E55B2-F16F-11CF-88CB-001111000030");
    const int DIGCF_PRESENT = 0x02, DIGCF_DEVICEINTERFACE = 0x10;
    const uint GENERIC_READ = 0x80000000, GENERIC_WRITE = 0x40000000;
    const uint FILE_SHARE_READ = 1, FILE_SHARE_WRITE = 2;
    const uint OPEN_EXISTING = 3;

    [StructLayout(LayoutKind.Sequential)]
    struct SP_DEVICE_INTERFACE_DATA { public int cbSize; public Guid InterfaceClassGuid; public int Flags; public IntPtr Reserved; }
    [StructLayout(LayoutKind.Sequential)]
    struct SP_DEVINFO_DATA { public int cbSize; public Guid ClassGuid; public int DevInst; public IntPtr Reserved; }

    [DllImport("setupapi.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    static extern IntPtr SetupDiGetClassDevs(ref Guid g, IntPtr enumerator, IntPtr hwnd, int flags);
    [DllImport("setupapi.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    static extern bool SetupDiEnumDeviceInterfaces(IntPtr h, IntPtr devInfo, ref Guid g, int idx, ref SP_DEVICE_INTERFACE_DATA d);
    [DllImport("setupapi.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    static extern bool SetupDiGetDeviceInterfaceDetail(IntPtr h, ref SP_DEVICE_INTERFACE_DATA d, IntPtr detail, int size, ref int needed, IntPtr devInfo);
    [DllImport("setupapi.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    static extern bool SetupDiGetDeviceInstanceId(IntPtr devInfo, ref SP_DEVINFO_DATA d, StringBuilder id, int size, ref int needed);
    [DllImport("setupapi.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    static extern bool SetupDiGetDeviceRegistryProperty(IntPtr devInfo, ref SP_DEVINFO_DATA d, int prop, ref int type, byte[] buf, int size, ref int needed);
    [DllImport("setupapi.dll", SetLastError = true)]
    static extern bool SetupDiDestroyDeviceInfoList(IntPtr h);

    [DllImport("hid.dll", SetLastError = true)]
    static extern bool HidD_GetAttributes(IntPtr h, ref HIDD_ATTRIBUTES a);
    [DllImport("hid.dll", SetLastError = true)]
    static extern bool HidD_GetPreparsedData(IntPtr h, ref IntPtr pp);
    [DllImport("hid.dll", SetLastError = true)]
    static extern bool HidD_FreePreparsedData(IntPtr pp);
    [DllImport("hid.dll", SetLastError = true)]
    static extern bool HidD_GetFeature(IntPtr h, byte[] buf, int len);
    [DllImport("hid.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    static extern bool HidD_GetProductString(IntPtr h, StringBuilder s, int len);
    [DllImport("hid.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    static extern bool HidD_GetManufacturerString(IntPtr h, StringBuilder s, int len);
    [DllImport("hid.dll")]
    static extern int HidP_GetCaps(IntPtr pp, ref HIDP_CAPS caps);
    [DllImport("hid.dll")]
    static extern int HidP_GetValueCaps(int kind, byte[] buf, ref int len, IntPtr pp);

    [StructLayout(LayoutKind.Sequential)]
    struct HIDD_ATTRIBUTES { public int Size; public ushort VendorID; public ushort ProductID; public ushort VersionNumber; }
    [StructLayout(LayoutKind.Sequential)]
    struct HIDP_CAPS {
        public ushort Usage; public ushort UsagePage; public ushort InputReportByteLength;
        public ushort OutputReportByteLength; public ushort FeatureReportByteLength;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 17)] public ushort[] Reserved;
        public ushort NumberLinkCollectionNodes; public ushort NumberInputButtonCaps; public ushort NumberInputValueCaps;
        public ushort NumberInputDataIndices; public ushort NumberOutputButtonCaps; public ushort NumberOutputValueCaps;
        public ushort NumberOutputDataIndices; public ushort NumberFeatureButtonCaps; public ushort NumberFeatureValueCaps;
        public ushort NumberFeatureDataIndices;
    }
    [StructLayout(LayoutKind.Sequential)]
    struct HIDP_VALUE_CAPS {
        public ushort UsagePage; public byte ReportID; public byte IsAlias; public ushort BitField;
        public ushort LinkCollection; public ushort LinkUsage; public ushort LinkUsagePage;
        public byte IsRange; public byte IsStringRange; public byte IsDesignatorRange; public byte IsAbsolute;
        public byte HasNull; public byte Reserved; public ushort BitSize; public ushort ReportCount;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 5)] public ushort[] Reserved2;
        public uint UnitsExp; public uint Units; public int LogicalMin; public int LogicalMax;
        public int PhysicalMin; public int PhysicalMax;
        public ushort UsageMin; public ushort UsageMax; public ushort StringMin; public ushort StringMax;
        public ushort DesignatorMin; public ushort DesignatorMax; public ushort DataIndexMin; public ushort DataIndexMax;
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    static extern IntPtr CreateFile(string name, uint access, uint share, IntPtr sec, uint disp, uint flags, IntPtr tmpl);
    [DllImport("kernel32.dll", SetLastError = true)]
    static extern bool CloseHandle(IntPtr h);

    static string Prop(IntPtr devInfo, ref SP_DEVINFO_DATA d, int prop)
    {
        byte[] buf = new byte[1024]; int type = 0, needed = 0;
        if (SetupDiGetDeviceRegistryProperty(devInfo, ref d, prop, ref type, buf, buf.Length, ref needed))
            return Encoding.Unicode.GetString(buf, 0, Math.Max(0, needed - 2));
        return null;
    }

    static void Main()
    {
      try {
        Console.WriteLine("START");
        Guid g = GUID_DEVINTERFACE_HID;
        IntPtr set = SetupDiGetClassDevs(ref g, IntPtr.Zero, IntPtr.Zero, DIGCF_PRESENT | DIGCF_DEVICEINTERFACE);
        Console.WriteLine("SET=" + set.ToInt64() + " ERR=" + Marshal.GetLastWin32Error());
        if (set == new IntPtr(-1)) { Console.WriteLine("SetupDiGetClassDevs failed " + Marshal.GetLastWin32Error()); return; }
        { var d0 = new SP_DEVICE_INTERFACE_DATA(); d0.cbSize = Marshal.SizeOf(d0); bool ok0 = SetupDiEnumDeviceInterfaces(set, IntPtr.Zero, ref g, 0, ref d0); Console.WriteLine("ENUM0=" + ok0 + " ERR=" + Marshal.GetLastWin32Error()); }
        var groups = new SortedDictionary<string, List<string>>();
        for (int i = 0; ; i++)
        {
            SP_DEVICE_INTERFACE_DATA did = new SP_DEVICE_INTERFACE_DATA();
            did.cbSize = Marshal.SizeOf(did);
            if (!SetupDiEnumDeviceInterfaces(set, IntPtr.Zero, ref g, i, ref did)) break;
            int needed = 0;
            SP_DEVINFO_DATA dd = new SP_DEVINFO_DATA();
            dd.cbSize = Marshal.SizeOf(dd);
            IntPtr ddPtr = Marshal.AllocHGlobal(Marshal.SizeOf(typeof(SP_DEVINFO_DATA)));
            Marshal.StructureToPtr(dd, ddPtr, false);
            SetupDiGetDeviceInterfaceDetail(set, ref did, IntPtr.Zero, 0, ref needed, IntPtr.Zero);
            IntPtr detail = Marshal.AllocHGlobal(needed);
            Marshal.WriteInt32(detail, IntPtr.Size == 8 ? 8 : 6);
            if (!SetupDiGetDeviceInterfaceDetail(set, ref did, detail, needed, ref needed, ddPtr)) { Marshal.FreeHGlobal(detail); Marshal.FreeHGlobal(ddPtr); continue; }
            dd = (SP_DEVINFO_DATA)Marshal.PtrToStructure(ddPtr, typeof(SP_DEVINFO_DATA));
            Marshal.FreeHGlobal(ddPtr);
            int off = 4;
            string path = Marshal.PtrToStringUni(new IntPtr(detail.ToInt64() + off));
            Marshal.FreeHGlobal(detail);

            StringBuilder inst = new StringBuilder(1024); int n2 = 0;
            SetupDiGetDeviceInstanceId(set, ref dd, inst, 1024, ref n2);
            string desc = Prop(set, ref dd, 0x0C); // SPDRP_DEVICEDESC
            string friendly = Prop(set, ref dd, 0x0E); // SPDRP_FRIENDLYNAME
            string parent = "";
            try {
                RegistryKey rk = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Enum\" + inst.ToString());
                if (rk != null) { parent = rk.GetValue("ParentIdPrefix") as string ?? ""; rk.Close(); }
            } catch {}

            IntPtr h = CreateFile(path, 0, FILE_SHARE_READ | FILE_SHARE_WRITE, IntPtr.Zero, OPEN_EXISTING, 0, IntPtr.Zero);
            if (h == new IntPtr(-1)) { Console.WriteLine("OPEN FAIL " + path + " err=" + Marshal.GetLastWin32Error()); continue; }
            HIDD_ATTRIBUTES att = new HIDD_ATTRIBUTES(); att.Size = Marshal.SizeOf(att);
            HidD_GetAttributes(h, ref att);
            StringBuilder prod = new StringBuilder(256);
            try { HidD_GetProductString(h, prod, prod.Capacity * 2); } catch {}
            IntPtr pp = IntPtr.Zero;
            string capsText = "";
            var featUsages = new List<string>();
            if (HidD_GetPreparsedData(h, ref pp)) {
                HIDP_CAPS caps = new HIDP_CAPS();
                int rc = HidP_GetCaps(pp, ref caps);
                if (rc == 0x110000) {
                    capsText = string.Format("UP=0x{0:X4} U=0x{1:X4} in={2} out={3} feat={4} featVC={5}", caps.UsagePage, caps.Usage, caps.InputReportByteLength, caps.OutputReportByteLength, caps.FeatureReportByteLength, caps.NumberFeatureValueCaps);
                    if (caps.NumberFeatureValueCaps > 0) {
                        int len = caps.NumberFeatureValueCaps * Marshal.SizeOf(typeof(HIDP_VALUE_CAPS));
                        byte[] buf = new byte[len];
                        int l2 = len;
                        if (HidP_GetValueCaps(2, buf, ref l2, pp) == 0x110000) {
                            int sz = Marshal.SizeOf(typeof(HIDP_VALUE_CAPS));
                            IntPtr vbuf = Marshal.AllocHGlobal(l2);
                            Marshal.Copy(buf, 0, vbuf, l2);
                            for (int k = 0; k < l2 / sz; k++) {
                                HIDP_VALUE_CAPS vc = (HIDP_VALUE_CAPS)Marshal.PtrToStructure(new IntPtr(vbuf.ToInt64() + k * sz), typeof(HIDP_VALUE_CAPS));
                                featUsages.Add(string.Format("feat 0x{0:X4}:0x{1:X4}{2} id={3} bits={4} count={5} min={6} max={7}", vc.UsagePage, vc.UsageMin, vc.IsRange != 0 ? ("-0x" + vc.UsageMax.ToString("X4")) : "", vc.ReportID, vc.BitSize, vc.ReportCount, vc.LogicalMin, vc.LogicalMax));
                                if (vc.UsagePage == 0x06) Console.WriteLine("    *** BATTERY USAGE FOUND id=" + vc.ReportID + " page=0x" + vc.UsagePage.ToString("X4"));
                            }
                            Marshal.FreeHGlobal(vbuf);
                        }
                    }
                } else capsText = "HidP_GetCaps rc=0x" + rc.ToString("X");
                HidD_FreePreparsedData(pp);
            }
            CloseHandle(h);
            string key = "VID_" + att.VendorID.ToString("X4") + " PID_" + att.ProductID.ToString("X4");
            if (!groups.ContainsKey(key)) groups[key] = new List<string>();
            groups[key].Add(string.Format("{0}\r\n    path={1}\r\n    inst={2} product='{3}' desc='{4}' friendly='{5}' parent='{6}'\r\n    {7}\r\n    {8}",
                "", path, inst, prod.ToString(), desc, friendly, parent, capsText, string.Join("\r\n    ", featUsages)));
        }
        SetupDiDestroyDeviceInfoList(set);
        Console.WriteLine("GROUPS=" + groups.Count);
        foreach (var kv in groups) {
            Console.WriteLine("==== " + kv.Key + " ====");
            foreach (var s in kv.Value) Console.WriteLine(s);
        }
      } catch (Exception ex) { Console.WriteLine("EX: " + ex); }
    }
}