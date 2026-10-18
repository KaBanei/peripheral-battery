using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Pb.Core
{
    /// <summary>按固定间隔在后台线程汇总所有 Provider 的结果。</summary>
    public sealed class ProviderHost : IDisposable
    {
        private readonly List<IProvider> _providers = new List<IProvider>();
        private readonly object _sync = new object();
        private readonly AppConfig _config;
        private Timer _timer;
        private int _scanning;
        private List<Peripheral> _devices = new List<Peripheral>();
        private List<ProviderStatus> _statuses = new List<ProviderStatus>();
        private CancellationTokenSource _cts = new CancellationTokenSource();
        private volatile bool _disposed;

        public event EventHandler<HostUpdate> Updated;

        public ProviderHost(AppConfig config)
        {
            _config = config;
        }

        public IReadOnlyList<Peripheral> Devices
        {
            get { lock (_sync) { return _devices.ToList(); } }
        }

        public IReadOnlyList<ProviderStatus> Statuses
        {
            get { lock (_sync) { return _statuses.ToList(); } }
        }

        public void Register(IProvider provider)
        {
            lock (_sync) { _providers.Add(provider); }
        }

        public void Start()
        {
            lock (_sync)
            {
                if (_timer != null) return;
                _timer = new Timer(_ => RefreshAsync(false), null, 200, Timeout.Infinite);
            }
        }

        public void Reschedule()
        {
            lock (_sync)
            {
                if (_timer == null) return;
                int ms = Math.Max(5, _config.RefreshSeconds) * 1000;
                _timer.Change(ms, ms);
            }
        }

        public void RefreshNow()
        {
            RefreshAsync(true);
        }

        private void RefreshAsync(bool manual)
        {
            if (_disposed) return;
            if (Interlocked.Exchange(ref _scanning, 1) == 1)
            {
                if (manual) ScheduleRetry();
                return;
            }
            Task.Run(() =>
            {
                try { DoScan(); }
                catch (Exception ex) { Log.Error("扫描失败", ex); }
                finally { Interlocked.Exchange(ref _scanning, 0); }
            });
            Reschedule();
        }

        private void ScheduleRetry()
        {
            Task.Delay(600).ContinueWith(_ => { if (!_disposed) RefreshAsync(false); });
        }

        private void DoScan()
        {
            var found = new List<Peripheral>();
            var statuses = new List<ProviderStatus>();
            List<IProvider> snapshot;
            lock (_sync) { snapshot = _providers.ToList(); }

            var ct = _cts.Token;
            foreach (var p in snapshot)
            {
                var sw = Stopwatch.StartNew();
                var st = new ProviderStatus
                {
                    Key = p.Key,
                    DisplayName = p.DisplayName,
                    Enabled = p.Enabled,
                    At = DateTime.Now
                };
                if (!p.Enabled)
                {
                    st.Ok = true;
                    statuses.Add(st);
                    continue;
                }
                try
                {
                    var list = p.Scan(ct) ?? new List<Peripheral>();
                    foreach (var d in list)
                    {
                        if (d.Updated == default(DateTime)) d.Updated = DateTime.Now;
                        if (string.IsNullOrEmpty(d.Provider)) d.Provider = p.DisplayName;
                        found.Add(d);
                    }
                    st.Ok = true;
                    st.Count = list.Count;
                }
                catch (Exception ex)
                {
                    st.Ok = false;
                    st.Error = ex.Message;
                    Log.Warn("Provider " + p.Key + " 扫描异常: " + ex.Message);
                }
                st.DurationMs = sw.ElapsedMilliseconds;
                statuses.Add(st);
            }

            var merged = ApplyOverrides(Merge(found));
            lock (_sync)
            {
                _devices = merged;
                _statuses = statuses;
            }
            var handler = Updated;
            if (handler != null)
            {
                try
                {
                    handler(this, new HostUpdate { Devices = merged, Providers = statuses, At = DateTime.Now });
                }
                catch (Exception ex) { Log.Error("UI 更新回调异常", ex); }
            }
        }

        /// <summary>
        /// 去重与用户改名：专用 Provider 已识别的同一物理设备，不再重复显示通用条目；
        /// 最后把用户自定义名称覆盖到显示名上。
        /// </summary>
        private List<Peripheral> ApplyOverrides(List<Peripheral> devices)
        {
            var covered = new HashSet<string>();
            for (int i = 0; i < devices.Count; i++)
            {
                Peripheral d = devices[i];
                if (!d.IsGeneric && d.Vid != 0)
                    covered.Add(d.Vid.ToString("X4") + ":" + d.Pid.ToString("X4"));
            }
            var result = new List<Peripheral>();
            for (int i = 0; i < devices.Count; i++)
            {
                Peripheral d = devices[i];
                if (d.IsGeneric && d.Vid != 0 && covered.Contains(d.Vid.ToString("X4") + ":" + d.Pid.ToString("X4")))
                    continue;
                string custom = _config.GetCustomName(d.Id);
                if (!string.IsNullOrEmpty(custom)) d.Name = custom;
                result.Add(d);
            }
            return result;
        }

        private static List<Peripheral> Merge(List<Peripheral> found)
        {
            var map = new Dictionary<string, Peripheral>();
            foreach (var d in found)
            {
                string key = d.MergeKey;
                Peripheral existing;
                if (!map.TryGetValue(key, out existing)) { map[key] = d; continue; }
                bool existingHas = existing.Percent.HasValue || existing.MilliVolts.HasValue;
                bool newHas = d.Percent.HasValue || d.MilliVolts.HasValue;
                if (newHas && !existingHas) map[key] = d;
                else if (newHas == existingHas && d.Updated > existing.Updated) map[key] = d;
            }
            // 先按类别（鼠标/键盘/耳机/手柄/其它），同类里已读到电量的排前面；
            // 避免“未读电量的耳机”被已读电量的手机/其它设备挤到列表底部看不见。
            return map.Values
                .OrderBy(d => d.Kind)
                .ThenByDescending(d => d.Percent.HasValue)
                .ThenBy(d => d.Name)
                .ToList();
        }

        public void Dispose()
        {
            _disposed = true;
            try { _cts.Cancel(); } catch { }
            lock (_sync)
            {
                if (_timer != null) { _timer.Dispose(); _timer = null; }
                foreach (var p in _providers)
                {
                    try { p.Dispose(); } catch { }
                }
                _providers.Clear();
            }
        }
    }
}