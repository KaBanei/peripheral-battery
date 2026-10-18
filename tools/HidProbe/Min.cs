using System;
using System.Runtime.InteropServices;
using System.Text;

class Min
{
    static Guid GUID_HID = new Guid("4D1E55B2-F16F-11CF-88CB-001111000030");
    [StructLayout(LayoutKind.Sequential)] struct SP_DEVICE_INTERFACE_DATA { public int cbSize; public Guid g; public int flags; public IntPtr res; }
    [StructLayout(LayoutKind.Sequential)] struct SP_DEVINFO_DATA { public int cbSize; public Guid g; public int dev; public IntPtr res; }
    [StructLayout(LayoutKind.Sequential)] struct HIDD_ATTRIBUTES { public int size; public ushort vid, pid, ver; }
    [StructLayout(LayoutKind.Sequential)] struct HIDP_CAPS {
        public ushort Usage, UsagePage, InLen, OutLen, FeatLen;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 17)] public ushort[] Reserved;
        public ushort NLink, NInBtn, NInVal, NInIdx, NOutBtn, NOutVal, NOutIdx, NFeatBtn, NFeatVal, NFeatIdx;
    }
    [StructLayout(LayoutKind.Sequential)] struct HIDP_VALUE_CAPS {
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
    [DllImport("setupapi.dll", CharSet = CharSet.Unicode)] static extern IntPtr SetupDiGetClassDevs(ref Guid g, IntPtr e, IntPtr h, int f);
    [DllImport("setupapi.dll", CharSet = CharSet.Unicode)] static extern bool SetupDiEnumDeviceInterfaces(IntPtr s, IntPtr d, ref Guid g, int i, ref SP_DEVICE_INTERFACE_DATA x);
    [DllImport("setupapi.dll", CharSet = CharSet.Unicode)] static extern bool SetupDiGetDeviceInterfaceDetail(IntPtr s, ref SP_DEVICE_INTERFACE_DATA x, IntPtr detail, int size, ref int needed, IntPtr d);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] static extern IntPtr CreateFile(string n, uint a, uint sh, IntPtr sec, uint disp, uint fl, IntPtr t);
    [DllImport("kernel32.dll")] static extern bool CloseHandle(IntPtr h);
    [DllImport("hid.dll")] static extern bool HidD_GetAttributes(IntPtr h, ref HIDD_ATTRIBUTES a);
    [DllImport("hid.dll")] static extern bool HidD_GetPreparsedData(IntPtr h, ref IntPtr pp);
    [DllImport("hid.dll")] static extern bool HidD_FreePreparsedData(IntPtr pp);
    [DllImport("hid.dll")] static extern int HidP_GetCaps(IntPtr pp, ref HIDP_CAPS caps);
    [DllImport("hid.dll")] static extern int HidP_GetValueCaps(int type, IntPtr caps, ref ushort len, IntPtr pp);
    static void P(string s) { Console.WriteLine(s); Console.Out.Flush(); }

    static void Main()
    {
        P("SIZEOF_CAPS=" + Marshal.SizeOf(typeof(HIDP_CAPS)) + " SIZEOF_VC=" + Marshal.SizeOf(typeof(HIDP_VALUE_CAPS)));
        Guid g = GUID_HID;
        IntPtr set = SetupDiGetClassDevs(ref g, IntPtr.Zero, IntPtr.Zero, 0x12);
        for (int i = 0; ; i++)
        {
            var did = new SP_DEVICE_INTERFACE_DATA(); did.cbSize = Marshal.SizeOf(did);
            if (!SetupDiEnumDeviceInterfaces(set, IntPtr.Zero, ref g, i, ref did)) break;
            int needed = 0;
            SetupDiGetDeviceInterfaceDetail(set, ref did, IntPtr.Zero, 0, ref needed, IntPtr.Zero);
            IntPtr detail = Marshal.AllocHGlobal(needed);
            Marshal.WriteInt32(detail, 8);
            var dd = new SP_DEVINFO_DATA(); dd.cbSize = Marshal.SizeOf(dd);
            IntPtr ddPtr = Marshal.AllocHGlobal(Marshal.SizeOf(dd));
            Marshal.StructureToPtr(dd, ddPtr, false);
            if (!SetupDiGetDeviceInterfaceDetail(set, ref did, detail, needed, ref needed, ddPtr)) { Marshal.FreeHGlobal(detail); Marshal.FreeHGlobal(ddPtr); continue; }
            string path = Marshal.PtrToStringUni(new IntPtr(detail.ToInt64() + 4));
            Marshal.FreeHGlobal(detail); Marshal.FreeHGlobal(ddPtr);
            IntPtr h = CreateFile(path, 0, 3, IntPtr.Zero, 3, 0, IntPtr.Zero);
            if (h == new IntPtr(-1)) continue;
            var at = new HIDD_ATTRIBUTES(); at.size = Marshal.SizeOf(at);
            HidD_GetAttributes(h, ref at);
            IntPtr pp = IntPtr.Zero;
            if (HidD_GetPreparsedData(h, ref pp))
            {
                var caps = new HIDP_CAPS();
                int rc = HidP_GetCaps(pp, ref caps);
                if (caps.FeatLen > 0)
                {
                    P("DEV " + at.vid.ToString("X4") + ":" + at.pid.ToString("X4") + " featLen=" + caps.FeatLen + " NFeatVal=" + caps.NFeatVal + " rc=" + rc.ToString("X8") + " path=" + path.Substring(Math.Max(0, path.Length - 40)));
                    if (caps.NFeatVal > 0)
                    {
                        int sz = Marshal.SizeOf(typeof(HIDP_VALUE_CAPS));
                        IntPtr buf = Marshal.AllocHGlobal(caps.NFeatVal * sz + 64);
                        ushort len = caps.NFeatVal;
                        try
                        {
                            int r2 = HidP_GetValueCaps(2, buf, ref len, pp);
                            P("  GetValueCaps rc=" + r2.ToString("X8") + " len=" + len);
                            if (at.vid == 0x2808 && caps.NFeatVal == 2) { byte[] raw = new byte[136]; Marshal.Copy(buf, raw, 0, 136); for (int off = 0; off < 136; off += 8) { var sb = new StringBuilder(); for (int q = 0; q < 8 && off + q < 136; q++) sb.Append(raw[off+q].ToString("X2") + " "); P("  @" + off.ToString("D3") + " " + sb.ToString()); } }
                            for (int k = 0; k < len && k < caps.NFeatVal; k++)
                            {
                                var vc = (HIDP_VALUE_CAPS)Marshal.PtrToStructure(new IntPtr(buf.ToInt64() + k * sz), typeof(HIDP_VALUE_CAPS));
                                P(string.Format("  feature page=0x{0:X4} usage=0x{1:X4} range={2} id={3} bits={4} count={5} min={6} max={7}", vc.UsagePage, vc.UsageMin, vc.IsRange, vc.ReportID, vc.BitSize, vc.ReportCount, vc.LogicalMin, vc.LogicalMax));
                            }
                        }
                        catch (Exception ex) { P("  EX " + ex.GetType().Name + " " + ex.Message); }
                        Marshal.FreeHGlobal(buf);
                    }
                }
                HidD_FreePreparsedData(pp);
            }
            CloseHandle(h);
        }
    }
}