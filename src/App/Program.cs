using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Threading;
using System.Windows.Forms;
using Pb.Core;

namespace Pb.App
{
    internal static class Program
    {
        private const string MutexName = "Global\\PeripheralBattery.v1";
        private const string MutexNameLocal = "Local\\PeripheralBattery.v1";
        private const string EventName = "Global\\PeripheralBattery.Show.v1";
        private const string EventNameLocal = "Local\\PeripheralBattery.Show.v1";

        [STAThread]
        private static void Main(string[] args)
        {
            bool trayOnly = false;
            bool showMain = false;
            bool settings = false;
            bool diagnose = false;
            bool demo = false;
            bool flyout = false;
            bool iconTest = false;
            for (int i = 0; i < args.Length; i++)
            {
                string a = (args[i] ?? "").Trim().ToLowerInvariant();
                if (a == "--tray") trayOnly = true;
                else if (a == "--show") showMain = true;
                else if (a == "--settings") settings = true;
                else if (a == "--diagnose") diagnose = true;
                else if (a == "--demo") demo = true;
                else if (a == "--flyout") flyout = true;
                else if (a == "--icon-test") iconTest = true;
            }

            try
            {
                Log.Info("启动 args=[" + string.Join(" ", args) + "] dpi-free=" + Environment.OSVersion.VersionString);
            }
            catch { }

            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);

            bool crashShown = false;
            Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
            Application.ThreadException += delegate(object sender, ThreadExceptionEventArgs e)
            {
                Log.Error("未处理的界面异常", e.Exception);
                if (!crashShown)
                {
                    crashShown = true;
                    ShowCrash(e.Exception);
                }
            };
            AppDomain.CurrentDomain.UnhandledException += delegate(object sender, UnhandledExceptionEventArgs e)
            {
                Exception ex = e.ExceptionObject as Exception;
                Log.Error("未处理的异常", ex == null ? new Exception("未知异常") : ex);
                if (!crashShown)
                {
                    crashShown = true;
                    ShowCrash(ex);
                }
            };

            Ui.Initialize();

            using (SingleInstance instance = new SingleInstance())
            {
                if (!instance.IsFirst)
                {
                    instance.SignalExisting();
                    return;
                }

                AppConfig config = AppConfig.Load();
                if (demo) config.EnableDemo = true;

                if (iconTest)
                {
                    RunIconTest();
                    return;
                }

                if (diagnose)
                {
                    RunDiagnose(config);
                    return;
                }

                bool startHidden = trayOnly && !showMain && !settings && !flyout;
                using (TrayAppContext ctx = new TrayAppContext(config, demo, startHidden, settings, flyout, instance.WakeHandle))
                {
                    Application.Run(ctx);
                }
            }
        }

        /// <summary>托盘图标自检：把 5 种状态渲染成一张放大图，便于无托盘环境核对。</summary>
        private static void RunIconTest()
        {
            try
            {
                string dir = AppConfig.DataDir;
                Directory.CreateDirectory(dir);
                int size = IconRenderer.PixelSize;
                Peripheral[] samples = new Peripheral[]
                {
                    MakeIconSample(DeviceKind.Mouse, 78, BatteryState.Discharging),
                    MakeIconSample(DeviceKind.Keyboard, 41, BatteryState.Discharging),
                    MakeIconSample(DeviceKind.Headset, 12, BatteryState.Low),
                    MakeIconSample(DeviceKind.Gamepad, 45, BatteryState.Charging),
                    MakeIconSample(DeviceKind.Other, -1, BatteryState.Unknown)
                };
                int scale = 4;
                int cell = size * scale + 16;
                using (Bitmap sheet = new Bitmap(cell * samples.Length + 8, size * scale + 24))
                {
                    using (Graphics g = Graphics.FromImage(sheet))
                    {
                        g.Clear(Theme.Bg);
                        g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.NearestNeighbor;
                        g.PixelOffsetMode = System.Drawing.Drawing2D.PixelOffsetMode.Half;
                        for (int i = 0; i < samples.Length; i++)
                        {
                            List<Peripheral> one = new List<Peripheral>();
                            one.Add(samples[i]);
                            using (Bitmap icon = IconRenderer.RenderBitmap(size, one, "worst"))
                            {
                                g.DrawImage(icon, new Rectangle(8 + i * cell, 8, size * scale, size * scale));
                            }
                            TextRenderer.DrawText(g, "state " + i, Theme.FontSub,
                                new Rectangle(8 + i * cell, size * scale + 10, cell, 18),
                                Theme.Sub,
                                TextFormatFlags.NoPrefix | TextFormatFlags.HorizontalCenter);
                        }
                    }
                    string path = Path.Combine(dir, "icon-sheet.png");
                    sheet.Save(path, System.Drawing.Imaging.ImageFormat.Png);
                    Log.Info("图标自检输出: " + path + " (icon " + size + "px)");
                }
            }
            catch (Exception ex)
            {
                Log.Error("图标自检失败", ex);
            }
        }

        private static Peripheral MakeIconSample(DeviceKind kind, int percent, BatteryState state)
        {
            Peripheral d = new Peripheral();
            d.Name = "sample";
            d.Kind = kind;
            d.State = state;
            d.Percent = percent >= 0 ? (int?)percent : null;
            return d;
        }

        private static void RunDiagnose(AppConfig config)
        {
            ProviderHost host = new ProviderHost(config);
            List<Peripheral> devices = new List<Peripheral>();
            List<ProviderStatus> statuses = new List<ProviderStatus>();
            DateTime at = DateTime.Now;
            ManualResetEventSlim done = new ManualResetEventSlim(false);
            try
            {
                List<IProvider> providers = ProviderRegistry.CreateAll(config, new AppContextAdapter(config));
                if (config.EnableDemo)
                {
                    bool hasSim = false;
                    for (int i = 0; i < providers.Count; i++)
                    {
                        if (providers[i].GetType().FullName == "Pb.Core.Providers.SimulationProvider") hasSim = true;
                    }
                    if (!hasSim) providers.Add(new DemoFallbackProvider());
                }
                for (int i = 0; i < providers.Count; i++) host.Register(providers[i]);
                host.Updated += delegate(object sender, HostUpdate e)
                {
                    devices = e.Devices == null ? new List<Peripheral>() : e.Devices;
                    statuses = e.Providers == null ? new List<ProviderStatus>() : e.Providers;
                    at = e.At;
                    done.Set();
                };
                host.Start();
                done.Wait(4000);
                Log.Info("诊断: 设备 " + devices.Count + " 台 / 数据源 " + statuses.Count + " 个");
                string text = Diag.Build(config, devices, statuses, at);
                Log.Info("诊断: 文本 " + text.Length + " 字符，准备显示窗口");
                using (DiagnoseForm form = new DiagnoseForm(text))
                {
                    Application.Run(form);
                }
                Log.Info("诊断: 窗口已关闭");
            }
            catch (Exception ex)
            {
                Log.Error("诊断模式失败", ex);
            }
            finally
            {
                try { host.Dispose(); } catch { }
                try { done.Dispose(); } catch { }
            }
        }

        private static void ShowCrash(Exception ex)
        {
            try
            {
                string detail = ex == null ? "未知错误" : (ex.Message + "\n" + ex.GetType().Name);
                MessageBox.Show("程序遇到一个问题，但仍会继续运行。\n\n" + detail
                    + "\n\n详细信息已写入日志：\n" + Log.FilePath,
                    "2.4G 外设电量", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
            catch { }
        }

        /// <summary>单实例：命名的 Mutex + 唤醒事件；Global 命名空间无权限时退回 Local。</summary>
        private sealed class SingleInstance : IDisposable
        {
            private Mutex _mutex;
            private EventWaitHandle _wake;
            private readonly bool _owns;

            public bool IsFirst { get; private set; }

            public EventWaitHandle WakeHandle { get { return _wake; } }

            public SingleInstance()
            {
                bool created = false;
                string scope = "Global\\";
                try
                {
                    _mutex = new Mutex(true, MutexName, out created);
                    if (!created)
                    {
                        // 另一个实例已存在：本进程不持有该 Mutex
                        _mutex.Dispose();
                        _mutex = new Mutex(false, MutexName);
                    }
                }
                catch (UnauthorizedAccessException)
                {
                    scope = "Local\\";
                    try
                    {
                        _mutex = new Mutex(true, MutexNameLocal, out created);
                        if (!created)
                        {
                            _mutex.Dispose();
                            _mutex = new Mutex(false, MutexNameLocal);
                        }
                    }
                    catch (Exception ex)
                    {
                        Log.Warn("创建单实例互斥体失败: " + ex.Message);
                        created = true;
                    }
                }
                catch (Exception ex)
                {
                    Log.Warn("创建单实例互斥体失败: " + ex.Message);
                    created = true;
                }

                IsFirst = created;
                _owns = created;

                if (created)
                {
                    try
                    {
                        _wake = new EventWaitHandle(false, EventResetMode.AutoReset, scope + "PeripheralBattery.Show.v1");
                    }
                    catch (Exception ex)
                    {
                        Log.Warn("创建唤醒事件失败: " + ex.Message);
                    }
                }
            }

            public void SignalExisting()
            {
                string[] names = new string[] { EventName, EventNameLocal };
                for (int i = 0; i < names.Length; i++)
                {
                    try
                    {
                        using (EventWaitHandle ev = EventWaitHandle.OpenExisting(names[i]))
                        {
                            ev.Set();
                            return;
                        }
                    }
                    catch { }
                }
                // 退化方案：找到主面板窗口并前台显示
                try
                {
                    IntPtr h = Native.FindWindow(null, "2.4G 外设电量");
                    if (h != IntPtr.Zero)
                    {
                        Native.ShowWindow(h, 5);
                        Native.SetForegroundWindow(h);
                    }
                }
                catch { }
            }

            public void Dispose()
            {
                if (_mutex != null)
                {
                    try { if (_owns) _mutex.ReleaseMutex(); } catch { }
                    try { _mutex.Dispose(); } catch { }
                    _mutex = null;
                }
                if (_wake != null)
                {
                    try { _wake.Dispose(); } catch { }
                    _wake = null;
                }
            }
        }
    }
}
