using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading;
using Pb.Core;
using Pb.Core.Hid;

internal static class VgnProbeProgram
{
    private static readonly object Sync = new object();
    private static StreamWriter _log;
    private static volatile bool _stop;

    private static void L(string s)
    {
        lock (Sync)
        {
            Console.WriteLine(s);
            if (_log != null) { _log.WriteLine(s); _log.Flush(); }
        }
    }

    private static string Hex(byte[] b, int max)
    {
        if (b == null) return "(null)";
        var p = new List<string>();
        for (int i = 0; i < b.Length && i < max; i++) p.Add(b[i].ToString("X2"));
        return string.Join(" ", p.ToArray()) + (b.Length > max ? " ...(" + b.Length + "B)" : "");
    }

    private static void Main(string[] args)
    {
        int seconds = 45;
        string outPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "vgn-probe.log");
        for (int i = 0; i < args.Length; i++)
        {
            if (args[i] == "--seconds" && i + 1 < args.Length) int.TryParse(args[i + 1], out seconds);
            if (args[i] == "--out" && i + 1 < args.Length) outPath = args[i + 1];
        }
        if (seconds < 5) seconds = 5;
        try { _log = new StreamWriter(outPath, false, new UTF8Encoding(false)); } catch { }

        L("VGN 电量诊断工具  " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
        L("说明：运行期间请持续移动 VGN 鼠标、敲击 VGN 键盘，让设备保持唤醒。");
        L("日志文件：" + outPath);
        L("");

        HidBus bus = new HidBus(delegate(string m) { if (m.IndexOf("打不开", StringComparison.Ordinal) < 0) L("[bus] " + m); });
        List<HidInterface> all = bus.EnumerateFresh();
        HidInterface mouseCtrl = null, kbdCtrl = null;
        L("== 相关 HID 接口 ==");
        for (int i = 0; i < all.Count; i++)
        {
            HidInterface it = all[i];
            if (it.Vid != 0x3554 && it.Vid != 0x320F) continue;
            L(string.Format("{0:X4}:{1:X4} usage={2:X4}:{3:X4} in/out/feat={4}/{5}/{6} svc={7}",
                it.Vid, it.Pid, it.UsagePage, it.Usage, it.InputReportLen, it.OutputReportLen, it.FeatureReportLen, it.Service));
            L("   " + it.InstanceId);
            if (it.Vid == 0x3554 && it.Pid == 0xF503 && it.UsagePage == 0xFF02 && it.OutputReportLen >= 16) mouseCtrl = it;
            if (it.Vid == 0x320F && it.Pid == 0x5088 && it.UsagePage == 0xFF1C && it.OutputReportLen >= 60) kbdCtrl = it;
            else if (it.Vid == 0x320F && it.Pid == 0x5088 && it.UsagePage == 0xFFEF && it.OutputReportLen >= 60 && kbdCtrl == null) kbdCtrl = it;
        }
        L("");
        L("mouseCtrl=" + (mouseCtrl == null ? "未找到" : mouseCtrl.InstanceId));
        L("kbdCtrl=" + (kbdCtrl == null ? "未找到" : kbdCtrl.InstanceId));
        L("");

        IHidChannel mouse = null, kbd = null;
        try { if (mouseCtrl != null) mouse = bus.Open(mouseCtrl); } catch (Exception ex) { L("打开鼠标控制接口失败: " + ex.Message); }
        try { if (kbdCtrl != null) kbd = bus.Open(kbdCtrl); } catch (Exception ex) { L("打开键盘控制接口失败: " + ex.Message); }

        Thread mr = null, kr = null;
        if (mouse != null) { mr = new Thread(delegate() { ReadLoop(mouse, "MOUSE"); }); mr.IsBackground = true; mr.Start(); }
        if (kbd != null) { kr = new Thread(delegate() { ReadLoop(kbd, "KBD"); }); kr.IsBackground = true; kr.Start(); }

        DateTime end = DateTime.UtcNow.AddSeconds(seconds);
        int round = 0;
        while (DateTime.UtcNow < end)
        {
            if (mouse != null)
            {
                if (round == 0) SendMouse(mouse, 2, new byte[] { 1 });
                SendMouse(mouse, 3, null);
                Thread.Sleep(50);
                SendMouse(mouse, 4, null);
            }
            if (kbd != null) SendKeyboard(kbd);
            round++;
            Thread.Sleep(600);
        }

        _stop = true;
        Thread.Sleep(700);
        try { if (mouse != null) mouse.Dispose(); } catch { }
        try { if (kbd != null) kbd.Dispose(); } catch { }
        L("");
        L("采集结束，共 " + round + " 轮。请把日志文件发回：");
        L(outPath);
        if (_log != null) { _log.Flush(); _log.Dispose(); }
        Console.WriteLine("按回车退出...");
        try { Console.ReadLine(); } catch { }
    }

    private static void ReadLoop(IHidChannel ch, string tag)
    {
        while (!_stop)
        {
            try
            {
                byte[] r = ch.Read(200);
                if (r == null) continue;
                L(string.Format("{0} RX {1}", tag, Hex(r, 32)));
                if (tag == "MOUSE" && r.Length > 9 && r[1] == 3)
                    L("    -> 在线标志=" + r[6]);
                if (tag == "MOUSE" && r.Length > 9 && r[1] == 4)
                    L("    -> 电量=" + r[6] + "% 充电=" + r[7] + " 电压=" + ((r[8] << 8) | r[9]) + "mV");
                if (tag == "KBD" && r.Length > 7)
                    L("    -> 候选电量=" + r[6] + "% 充电位=" + ((r[7] >> 4) & 15));
            }
            catch { Thread.Sleep(100); }
        }
    }

    private static void SendMouse(IHidChannel ch, byte cmd, byte[] value)
    {
        byte[] data = new byte[16];
        data[0] = cmd;
        if (value != null && value.Length > 0)
        {
            data[4] = (byte)value.Length;
            for (int i = 0; i < value.Length && 5 + i < 15; i++) data[5 + i] = value[i];
        }
        int sum = 0;
        for (int i = 0; i < 15; i++) sum += data[i];
        int crc = (85 - (sum & 0xFF)) & 0xFF;
        data[15] = (byte)((crc - 8) & 0xFF);
        byte[] report = new byte[17];
        report[0] = 8;
        Array.Copy(data, 0, report, 1, 16);
        bool ok = ch.Write(report);
        L("MOUSE TX cmd=" + cmd + " -> " + ok + "  " + Hex(report, 17));
    }

    private static void SendKeyboard(IHidChannel ch)
    {
        byte[] buf = new byte[64];
        // VHUB 实测：COL04（FF1C:0092）/ report 4，电量命令 0x1A，数据 [0,0,26,6,0,0,0]
        buf[0] = 4;
        buf[1] = 0;
        buf[2] = 0;
        buf[3] = 26;
        buf[4] = 6;
        bool ok = ch.Write(buf);
        L("KBD TX -> " + ok + "  " + Hex(buf, 16));
    }
}