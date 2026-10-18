using System;
using System.Runtime.InteropServices;
using System.Text;

class PropProbe
{
    const int DIGCF_PRESENT = 0x02, DIGCF_ALLCLASSES = 0x04;
    [StructLayout(LayoutKind.Sequential)]
    struct SP_DEVINFO_DATA { public int cbSize; public Guid ClassGuid; public int DevInst; public IntPtr Reserved; }
    [StructLayout(LayoutKind.Sequential)]
    struct DEVPROPKEY { public Guid fmtid; public uint pid; }
    [DllImport("setupapi.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    static extern IntPtr SetupDiGetClassDevs(IntPtr g, IntPtr en, IntPtr hwnd, int flags);
    [DllImport("setupapi.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    static extern bool SetupDiEnumDeviceInfo(IntPtr set, int idx, ref SP_DEVINFO_DATA d);
    [DllImport("setupapi.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    static extern bool SetupDiGetDeviceInstanceId(IntPtr set, ref SP_DEVINFO_DATA d, StringBuilder id, int size, ref int needed);
    [DllImport("setupapi.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    static extern bool SetupDiGetDeviceProperty(IntPtr set, ref SP_DEVINFO_DATA d, ref DEVPROPKEY key, out uint type, byte[] buf, int size, out int needed, int flags);
    [DllImport("setupapi.dll")]
    static extern bool SetupDiDestroyDeviceInfoList(IntPtr set);

    static string StrProp(IntPtr set, ref SP_DEVINFO_DATA d, Guid g, uint pid)
    {
        var k = new DEVPROPKEY(); k.fmtid = g; k.pid = pid;
        byte[] buf = new byte[1024]; int needed = 0; uint type = 0;
        if (SetupDiGetDeviceProperty(set, ref d, ref k, out type, buf, buf.Length, out needed, 0) && needed > 0)
        {
            try { return Encoding.Unicode.GetString(buf, 0, Math.Min(needed, buf.Length)).TrimEnd('\0'); } catch { return null; }
        }
        return null;
    }
    static int? ByteProp(IntPtr set, ref SP_DEVINFO_DATA d, Guid g, uint pid)
    {
        var k = new DEVPROPKEY(); k.fmtid = g; k.pid = pid;
        byte[] buf = new byte[64]; int needed = 0; uint type = 0;
        if (SetupDiGetDeviceProperty(set, ref d, ref k, out type, buf, buf.Length, out needed, 0) && needed > 0)
        {
            if (type == 17 && needed >= 1) return buf[0];               // DEVPROP_TYPE_BYTE
            if (type == 19 && needed >= 4) return BitConverter.ToInt32(buf, 0); // UINT32
            if (type == 2) return null;
        }
        return null;
    }

    static void Main()
    {
        Guid friendly = new Guid("a45c254e-df1c-4efd-8020-67d146a850e0");
        Guid btBattery = new Guid("104ea319-6ee2-4701-bd47-8ddbf425bbe5");
        IntPtr set = SetupDiGetClassDevs(IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, DIGCF_PRESENT | DIGCF_ALLCLASSES);
        if (set == new IntPtr(-1)) { Console.WriteLine("enum fail " + Marshal.GetLastWin32Error()); return; }
        int total = 0, withBt = 0;
        for (int i = 0; ; i++)
        {
            var d = new SP_DEVINFO_DATA(); d.cbSize = Marshal.SizeOf(d);
            if (!SetupDiEnumDeviceInfo(set, i, ref d)) break;
            total++;
            var sb = new StringBuilder(512); int n = 0;
            if (!SetupDiGetDeviceInstanceId(set, ref d, sb, 512, ref n)) continue;
            string inst = sb.ToString();
            string name = StrProp(set, ref d, friendly, 14);
            int? bt2 = ByteProp(set, ref d, btBattery, 2);
            int? bt4 = ByteProp(set, ref d, btBattery, 4);
            if (bt2.HasValue || bt4.HasValue)
            {
                withBt++;
                Console.WriteLine("BATTERY  inst=" + inst + "  name=" + name + "  pid2=" + (bt2.HasValue ? bt2.ToString() : "-") + "  pid4=" + (bt4.HasValue ? bt4.ToString() : "-"));
            }
        }
        SetupDiDestroyDeviceInfoList(set);
        Console.WriteLine("TOTAL_DEVICES=" + total + " WITH_BT_BATTERY=" + withBt);
    }
}
