using System;
using System.Collections.Generic;
using System.Drawing;
using System.Text;
using System.Threading;
using System.Windows.Forms;
using Pb.Core;
using Pb.Core.Platform;

namespace Pb.App
{
    /// <summary>托盘宿主：NotifyIcon、右键菜单、悬浮面板、主面板、低电量提醒。</summary>
    internal sealed class TrayAppContext : ApplicationContext
    {
        private readonly AppConfig _config;
        private readonly bool _forcedDemo;
        private readonly ProviderHost _host;
        private readonly IconRenderer _icons = new IconRenderer();
        private readonly NotifyIcon _tray;
        private readonly ContextMenuStrip _menu;
        private ToolStripMenuItem _miAutoStart;
        private readonly System.Windows.Forms.Timer _tick;
        private readonly System.Windows.Forms.Timer _startup;
        private RegisteredWaitHandle _wake;
        private readonly bool _startHidden;
        private readonly bool _openSettings;
        private readonly bool _openFlyout;

        private List<Peripheral> _devices = new List<Peripheral>();
        private List<ProviderStatus> _statuses = new List<ProviderStatus>();
        private DateTime _at;

        private readonly HashSet<string> _alerted = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, string> _rawNames = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        private FlyoutForm _flyout;
        private MainForm _main;
        private EdgeBarForm _edgeBar;
        private bool _edgeMode;
        private bool _exiting;
        private bool _cleanupDone;
        private bool _startupDone;

        public TrayAppContext(AppConfig config, bool forcedDemo, bool startHidden, bool openSettings, bool openFlyout, EventWaitHandle wakeSignal)
        {
            _config = config;
            _forcedDemo = forcedDemo;
            _startHidden = startHidden;
            _openSettings = openSettings;
            _openFlyout = openFlyout;

            // 托盘图标先以“未知”状态出现
            Icon first = _icons.Create(null, "worst");
            _tray = new NotifyIcon();
            _tray.Icon = first;
            _icons.Replace(first);
            _tray.Text = "2.4G 外设电量";
            _tray.Visible = true;
            _tray.MouseClick += OnTrayClick;
            _tray.MouseDoubleClick += OnTrayDoubleClick;
            _tray.BalloonTipClicked += delegate { ShowMain(); };

            _menu = BuildMenu();
            _tray.ContextMenuStrip = _menu;

            // Provider 宿主
            _host = new ProviderHost(_config);
            List<IProvider> providers = ProviderRegistry.CreateAll(_config, new AppContextAdapter(_config));
            if (_config.EnableDemo && !HasSimulation(providers))
            {
                Log.Warn("演示模式已开启：Core 中暂无 SimulationProvider，使用界面兜底演示数据。");
                providers.Add(new DemoFallbackProvider());
            }
            for (int i = 0; i < providers.Count; i++)
            {
                _host.Register(providers[i]);
            }
            _host.Updated += OnHostUpdated;
            _host.Start();

            // 60 秒重建 tooltip，避免 Windows 截断
            _tick = new System.Windows.Forms.Timer();
            _tick.Interval = 60000;
            _tick.Tick += delegate
            {
                UpdateTray();
                RefreshMenuState();
            };
            _tick.Start();

            // 启动后显示窗口（让消息循环先跑起来）
            _startup = new System.Windows.Forms.Timer();
            _startup.Interval = 150;
            _startup.Tick += OnStartupTick;
            _startup.Start();

            // 第二个实例唤出面板
            if (wakeSignal != null)
            {
                try
                {
                    _wake = ThreadPool.RegisterWaitForSingleObject(wakeSignal,
                        delegate { Ui.Post(OnWakeRequested); }, null, Timeout.Infinite, false);
                }
                catch (Exception ex) { Log.Warn("注册单实例唤醒失败: " + ex.Message); }
            }

            // 已启用开机启动时刷新注册表路径（程序可能被移动过）
            try { if (AutoStart.IsEnabled) AutoStart.SetEnabled(true); } catch { }

            UpdateTray();
            try
            {
                Log.Info("托盘图标: " + IconRenderer.PixelSize + "px, Visible=" + _tray.Visible
                    + ", tooltip=[" + (_tray.Text ?? "") + "] 长度=" + (_tray.Text == null ? 0 : _tray.Text.Length));
            }
            catch { }
        }

        private static bool HasSimulation(List<IProvider> providers)
        {
            for (int i = 0; i < providers.Count; i++)
            {
                IProvider p = providers[i];
                if (p != null && p.GetType().FullName == "Pb.Core.Providers.SimulationProvider") return true;
            }
            return false;
        }

        private ContextMenuStrip BuildMenu()
        {
            ContextMenuStrip m = new ContextMenuStrip();
            m.Renderer = new DarkMenuRenderer();
            m.Font = Theme.FontBody;
            m.BackColor = Theme.Card;
            m.ForeColor = Theme.Text;
            m.ShowImageMargin = true;
            m.Padding = new Padding(2, 4, 2, 4);

            ToolStripMenuItem open = new ToolStripMenuItem("打开面板");
            open.Font = Theme.FontBodyBold;
            open.Click += delegate { ShowMain(); };
            m.Items.Add(open);

            ToolStripMenuItem refresh = new ToolStripMenuItem("立即刷新");
            refresh.Click += delegate
            {
                try { _host.RefreshNow(); } catch (Exception ex) { Log.Warn("刷新失败: " + ex.Message); }
            };
            m.Items.Add(refresh);

            m.Items.Add(new ToolStripSeparator());

            ToolStripMenuItem settings = new ToolStripMenuItem("设置…");
            settings.Click += delegate { ShowSettings(); };
            m.Items.Add(settings);

            _miAutoStart = new ToolStripMenuItem("开机启动");
            _miAutoStart.Click += delegate { ToggleAutoStart(); };
            m.Items.Add(_miAutoStart);

            m.Items.Add(new ToolStripSeparator());

            ToolStripMenuItem diag = new ToolStripMenuItem("诊断信息…");
            diag.Click += delegate { ShowDiagnose(); };
            m.Items.Add(diag);

            ToolStripMenuItem about = new ToolStripMenuItem("关于");
            about.Click += delegate { ShowAbout(); };
            m.Items.Add(about);

            m.Items.Add(new ToolStripSeparator());

            ToolStripMenuItem exit = new ToolStripMenuItem("退出");
            exit.Click += delegate { ExitApp(); };
            m.Items.Add(exit);

            m.Opening += delegate { RefreshMenuState(); };
            return m;
        }

        private void RefreshMenuState()
        {
            try { _miAutoStart.Checked = SafeAutoStart(); } catch { }
        }

        private static bool SafeAutoStart()
        {
            try { return AutoStart.IsEnabled; } catch { return false; }
        }

        // ---------------- 启动流程 ----------------

        private void OnStartupTick(object sender, EventArgs e)
        {
            _startup.Stop();
            if (_startupDone) return;
            _startupDone = true;
            if (_exiting) return;

            if (!_config.HasCompletedFirstRun)
            {
                try
                {
                    using (OnboardingForm f = new OnboardingForm())
                    {
                        DialogResult r = f.ShowDialog();
                        if (r == DialogResult.OK)
                        {
                            _config.HasCompletedFirstRun = true;
                            try { _config.Save(); } catch { }
                        }
                    }
                }
                catch (Exception ex) { Log.Error("显示引导失败", ex); }
            }

            if (_openFlyout) { ShowFlyout(); return; }
            if (_openSettings) { ShowSettings(); return; }
            if (!_startHidden) ShowMain();
        }

        private void OnWakeRequested()
        {
            if (_exiting) return;
            if (_flyout != null && !_flyout.IsDisposed) _flyout.Hide();
            ShowMain();
        }

        // ---------------- 数据 ----------------

        private void OnHostUpdated(object sender, HostUpdate e)
        {
            if (_exiting) return;
            List<Peripheral> devices = e.Devices == null ? new List<Peripheral>() : new List<Peripheral>(e.Devices);
            List<ProviderStatus> providers = e.Providers == null ? new List<ProviderStatus>() : new List<ProviderStatus>(e.Providers);
            DateTime at = e.At;
            Ui.Post(delegate { ApplyUpdate(devices, providers, at); });
        }

        private void ApplyUpdate(List<Peripheral> devices, List<ProviderStatus> providers, DateTime at)
        {
            if (_exiting) return;
            _devices = devices;
            _statuses = providers;
            _at = at;
            // 记录未改名设备的原始名称，供“恢复默认”立即显示
            for (int i = 0; i < devices.Count; i++)
            {
                Peripheral d = devices[i];
                if (d == null || string.IsNullOrEmpty(d.Id)) continue;
                if (string.IsNullOrEmpty(_config.GetCustomName(d.Id)) && !string.IsNullOrEmpty(d.Name))
                {
                    _rawNames[d.Id] = d.Name;
                }
            }
            UpdateTray();
            if (_flyout != null && !_flyout.IsDisposed) _flyout.SetData(_devices, _statuses, _at);
            if (_main != null && !_main.IsDisposed && _main.Visible) _main.SetData(_devices, _statuses, _at);
            if (_edgeBar != null && !_edgeBar.IsDisposed) _edgeBar.SetData(_devices);
            CheckLowBattery();
        }

        private void UpdateTray()
        {
            if (_exiting || _tray == null) return;
            try
            {
                Icon next = _icons.Create(_devices, _config.TrayDisplay);
                _tray.Icon = next;
                _icons.Replace(next);
                _tray.Text = BuildTooltip();
            }
            catch (Exception ex)
            {
                Log.Warn("更新托盘图标失败: " + ex.Message);
            }
        }

        /// <summary>多行 tooltip（NotifyIcon 上限 63 字符，逐级降级）。</summary>
        private string BuildTooltip()
        {
            const string Head = "2.4G 外设电量";
            if (_devices.Count == 0) return Head + "\n未发现支持的外设";
            int max = Math.Min(3, _devices.Count);
            for (int take = max; take >= 1; take--)
            {
                StringBuilder sb = new StringBuilder(Head);
                sb.Append('\n');
                for (int i = 0; i < take; i++)
                {
                    if (i > 0) sb.Append(" · ");
                    sb.Append(_devices[i].KindText).Append(' ').Append(Fmt.Percent(_devices[i].Percent));
                }
                if (_devices.Count > take) sb.Append(" 等").Append(_devices.Count).Append("台");
                string text = sb.ToString();
                if (text.Length <= 63) return text;
            }
            string fallback = Head + "\n" + Fmt.Percent(_devices[0].Percent);
            return fallback.Length <= 63 ? fallback : fallback.Substring(0, 63);
        }

        private void CheckLowBattery()
        {
            if (!_config.NotifyLowBattery) return;
            int th = _config.LowBatteryThreshold;
            for (int i = 0; i < _devices.Count; i++)
            {
                Peripheral d = _devices[i];
                if (d == null || !d.Percent.HasValue) continue;
                string key = d.MergeKey;
                if (d.Percent.Value < th)
                {
                    if (_alerted.Add(key))
                    {
                        ShowBalloon("电量偏低", d.Name + " 剩余 " + d.Percent.Value + "%（提醒阈值 " + th + "%）",
                            ToolTipIcon.Warning);
                    }
                }
                else if (d.Percent.Value >= th + 5)
                {
                    _alerted.Remove(key);
                }
            }
        }

        private void ShowBalloon(string title, string text, ToolTipIcon icon)
        {
            try
            {
                if (_tray == null || !_tray.Visible) return;
                _tray.ShowBalloonTip(5000, title, text, icon);
            }
            catch (Exception ex) { Log.Warn("气泡提示失败: " + ex.Message); }
        }

        // ---------------- 窗口 ----------------

        private void OnTrayClick(object sender, MouseEventArgs e)
        {
            if (e.Button != MouseButtons.Left) return;
            if (_exiting) return;
            if (_flyout != null && !_flyout.IsDisposed && _flyout.Visible)
            {
                _flyout.Hide();
                return;
            }
            if (_flyout != null && !_flyout.IsDisposed
                && (DateTime.Now - _flyout.LastHiddenAt).TotalMilliseconds < 300) return;
            ShowFlyout();
        }

        private void OnTrayDoubleClick(object sender, MouseEventArgs e)
        {
            if (e.Button != MouseButtons.Left) return;
            if (_exiting) return;
            if (_flyout != null && !_flyout.IsDisposed) _flyout.Hide();
            ShowMain();
        }

        private void ShowFlyout()
        {
            try
            {
                if (_flyout == null || _flyout.IsDisposed)
                {
                    _flyout = new FlyoutForm();
                    _flyout.SettingsRequested += delegate { ShowSettings(); };
                    _flyout.RefreshRequested += delegate
                    {
                        try { _host.RefreshNow(); } catch (Exception ex) { Log.Warn("刷新失败: " + ex.Message); }
                    };
                    _flyout.FormClosed += delegate { _flyout = null; };
                }
                _flyout.SetData(_devices, _statuses, _at);
                _flyout.ShowAtTray();
            }
            catch (Exception ex)
            {
                Log.Error("显示悬浮面板失败", ex);
            }
        }

        private void ShowMain()
        {
            if (_exiting) return;
            try
            {
                if (_edgeMode && _edgeBar != null && !_edgeBar.IsDisposed)
                {
                    _edgeBar.HideBar();
                }
                _edgeMode = false;
                if (_main == null || _main.IsDisposed)
                {
                    _main = new MainForm(_config);
                    _main.MinimizeRequested += delegate { MinimizeToEdge(); };
                    _main.RefreshRequested += delegate
                    {
                        try { _host.RefreshNow(); } catch (Exception ex) { Log.Warn("刷新失败: " + ex.Message); }
                    };
                    _main.SettingsRequested += delegate { ShowSettings(); };
                    _main.RenameRequested += delegate(Peripheral d) { ShowRenameDialog(d); };
                    _main.ExitRequested += delegate { ExitApp(); };
                    _main.FormClosed += delegate { _main = null; };
                }
                _main.SetData(_devices, _statuses, _at);
                if (!_main.Visible)
                {
                    _main.Show();
                    Ui.FadeIn(_main, 150);
                }
                _main.Activate();
                _main.BringToFront();
            }
            catch (Exception ex)
            {
                Log.Error("显示主面板失败", ex);
            }
        }

        private void MinimizeToEdge()
        {
            if (_exiting || _main == null || _main.IsDisposed) return;
            try
            {
                if (_flyout != null && !_flyout.IsDisposed) _flyout.Hide();
                if (_edgeBar == null || _edgeBar.IsDisposed)
                {
                    _edgeBar = new EdgeBarForm();
                    _edgeBar.RestoreRequested += delegate { RestoreFromEdge(); };
                    _edgeBar.RefreshRequested += delegate
                    {
                        try { _host.RefreshNow(); } catch (Exception ex) { Log.Warn("刷新失败: " + ex.Message); }
                    };
                    _edgeBar.ExitRequested += delegate { ExitApp(); };
                    _edgeBar.FormClosed += delegate { _edgeBar = null; };
                }
                Screen scr = Screen.FromControl(_main);
                _edgeBar.SetData(_devices);
                _edgeBar.ShowBar(scr);
                _main.Hide();
                _edgeMode = true;
            }
            catch (Exception ex)
            {
                Log.Error("最小化到侧边栏失败", ex);
            }
        }

        private void RestoreFromEdge()
        {
            if (_exiting) return;
            _edgeMode = false;
            if (_edgeBar != null && !_edgeBar.IsDisposed) _edgeBar.HideBar();
            ShowMain();
        }

        private void ShowSettings()
        {
            if (_exiting) return;
            try
            {
                using (SettingsForm f = new SettingsForm(_config, _forcedDemo))
                {
                    DialogResult r = f.ShowDialog();
                    if (r == DialogResult.OK)
                    {
                        try { _host.Reschedule(); } catch { }
                        UpdateTray();
                        if (_main != null && !_main.IsDisposed) _main.SetData(_devices, _statuses, _at);
                        if (f.RestartRequired)
                        {
                            ShowBalloon("设置已保存", "数据源开关将在重新启动程序后生效", ToolTipIcon.Info);
                        }
                        RefreshMenuState();
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Error("显示设置失败", ex);
            }
        }

        /// <summary>设备的原始（Provider 提供的）名称。</summary>
        private string RawNameOf(Peripheral d)
        {
            if (d == null) return "";
            string raw;
            if (!string.IsNullOrEmpty(d.Id) && _rawNames.TryGetValue(d.Id, out raw) && !string.IsNullOrEmpty(raw))
                return raw;
            return d.Name == null ? "" : d.Name;
        }

        /// <summary>重命名设备：写配置后立即刷新主面板/悬浮面板/托盘。</summary>
        private void ShowRenameDialog(Peripheral d)
        {
            if (_exiting || d == null) return;
            try
            {
                Log.Info("RENAME open: name=" + d.Name + " id=" + d.Id + " raw=" + RawNameOf(d));
                bool rawKnown = !string.IsNullOrEmpty(d.Id) && _rawNames.ContainsKey(d.Id);
                using (RenameForm f = new RenameForm(_config, d, RawNameOf(d), rawKnown))
                {
                    DialogResult r = f.ShowDialog();
                    string applied = f.AppliedName == null ? "" : f.AppliedName;
                    Log.Info("RENAME result: " + r + " applied=[" + applied + "]");
                    if (r != DialogResult.OK) return;
                    if (applied.Length > 0)
                    {
                        d.Name = applied;
                    }
                    else
                    {
                        string raw;
                        if (!string.IsNullOrEmpty(d.Id) && _rawNames.TryGetValue(d.Id, out raw) && !string.IsNullOrEmpty(raw))
                        {
                            d.Name = raw;
                        }
                        else
                        {
                            // 原始名称未知：立刻重扫，下一轮更新会带回 Provider 名称
                            try { _host.RefreshNow(); } catch { }
                        }
                    }
                    UpdateTray();
                    if (_main != null && !_main.IsDisposed) _main.SetData(_devices, _statuses, _at);
                    if (_flyout != null && !_flyout.IsDisposed) _flyout.SetData(_devices, _statuses, _at);
                }
            }
            catch (Exception ex)
            {
                Log.Error("重命名失败", ex);
            }
        }

        private void ShowDiagnose()
        {
            try
            {
                string text = Diag.Build(_config, _devices, _statuses, _at);
                using (DiagnoseForm f = new DiagnoseForm(text))
                {
                    f.ShowDialog();
                }
            }
            catch (Exception ex)
            {
                Log.Error("显示诊断信息失败", ex);
            }
        }

        private void ShowAbout()
        {
            try
            {
                using (AboutForm f = new AboutForm())
                {
                    f.ShowDialog();
                }
            }
            catch (Exception ex)
            {
                Log.Error("显示关于失败", ex);
            }
        }

        private void ToggleAutoStart()
        {
            bool now = !SafeAutoStart();
            bool ok = false;
            try { ok = AutoStart.SetEnabled(now); } catch { }
            if (!ok)
            {
                ShowBalloon("开机启动", "写入注册表失败，可能被安全软件拦截", ToolTipIcon.Warning);
                return;
            }
            _config.StartWithWindows = now;
            try { _config.Save(); } catch { }
            RefreshMenuState();
            ShowBalloon("开机启动", now ? "已设置为随 Windows 启动" : "已取消开机启动", ToolTipIcon.Info);
        }

        // ---------------- 退出 ----------------

        private void ExitApp()
        {
            Cleanup();
            ExitThread();
        }

        private void Cleanup()
        {
            if (_cleanupDone) return;
            _cleanupDone = true;
            _exiting = true;
            try { if (_tick != null) { _tick.Stop(); _tick.Dispose(); } } catch { }
            try { if (_startup != null) { _startup.Stop(); _startup.Dispose(); } } catch { }
            try { if (_wake != null) { _wake.Unregister(null); _wake = null; } } catch { }
            try { if (_tray != null) _tray.Visible = false; } catch { }
            try { if (_host != null) { _host.Updated -= OnHostUpdated; _host.Dispose(); } } catch { }
            try { if (_flyout != null && !_flyout.IsDisposed) { _flyout.Hide(); _flyout.Dispose(); } } catch { }
            try { if (_main != null && !_main.IsDisposed) { _main.CloseForExit(); _main.Dispose(); } } catch { }
            try { if (_edgeBar != null && !_edgeBar.IsDisposed) { _edgeBar.HideBar(); _edgeBar.Dispose(); } } catch { }
            try { if (_tray != null) _tray.Dispose(); } catch { }
            try { _icons.Dispose(); } catch { }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) Cleanup();
            base.Dispose(disposing);
        }
    }
}