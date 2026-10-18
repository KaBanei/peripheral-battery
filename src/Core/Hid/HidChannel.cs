using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Threading;

namespace Pb.Core.Hid
{
    /// <summary>
    /// 可选能力：按 HID 描述符解析某个 Feature usage 的数值。
    /// 标准 HID 电量（UsagePage 0x06 / Usage 0x20）用它，不假设字节偏移；伪通道可用于自测。
    /// </summary>
    public interface IHidUsageReader
    {
        bool TryReadUsageValue(ushort usagePage, ushort usage, out int rawValue, out HidUsageRange caps);
    }

    /// <summary>
    /// 已打开的 HID 通道：FILE_FLAG_OVERLAPPED + 共享读写；
    /// Read 用 overlapped + WaitForSingleObject 做毫秒级超时，超时返回 null；
    /// Write 优先 HidD_SetOutputReport（首字节为报告 ID），失败再退化为 WriteFile。
    /// Dispose 幂等，句柄一定关闭。
    /// </summary>
    public sealed class HidChannel : IHidChannel, IHidUsageReader
    {
        private const int WriteTimeoutMs = 1000;
        private const int CancelGraceMs = 500;

        private readonly HidInterface _info;
        private readonly object _sync = new object();
        private IntPtr _handle;
        private IntPtr _preparsed;
        private int _disposed;
        private int _faulted;

        /// <summary>超时取消后仍在内核里挂着的缓冲区，绝不能释放（极少发生，只保活）。</summary>
        private static readonly List<object> StuckBuffers = new List<object>();

        internal HidChannel(HidInterface info)
        {
            if (info == null) throw new ArgumentNullException("info");
            _info = info;
            _handle = HidNative.OpenDevice(info.Path, true);
            if (_handle == HidNative.InvalidHandleValue || _handle == IntPtr.Zero)
            {
                int err = Marshal.GetLastWin32Error();
                _handle = IntPtr.Zero;
                throw new System.ComponentModel.Win32Exception(err, "打开 HID 设备失败: " + info.Path);
            }
            IntPtr preparsed = IntPtr.Zero;
            if (HidNative.HidD_GetPreparsedData(_handle, ref preparsed)) _preparsed = preparsed;
        }

        public HidInterface Info
        {
            get { return _info; }
        }

        public bool IsFaulted
        {
            get { return Volatile.Read(ref _faulted) != 0; }
        }

        public bool Write(byte[] report)
        {
            if (report == null || report.Length == 0) return false;
            lock (_sync)
            {
                if (IsDisposed || _handle == IntPtr.Zero || IsFaulted) return false;

                int length = _info.OutputReportLen > 0
                    ? Math.Max(_info.OutputReportLen, report.Length)
                    : report.Length;
                var buffer = new byte[length];
                Array.Copy(report, buffer, Math.Min(report.Length, length));

                try
                {
                    if (HidNative.HidD_SetOutputReport(_handle, buffer, length)) return true;
                }
                catch
                {
                    // 某些驱动对 SetOutputReport 直接抛异常，继续尝试 WriteFile
                }
                return WriteOverlapped(buffer);
            }
        }

        private bool WriteOverlapped(byte[] data)
        {
            GCHandle pin = GCHandle.Alloc(data, GCHandleType.Pinned);
            IntPtr evt = HidNative.CreateEvent(IntPtr.Zero, true, false, null);
            if (evt == IntPtr.Zero)
            {
                pin.Free();
                return false;
            }
            try
            {
                var overlapped = new NativeOverlapped();
                overlapped.EventHandle = evt;
                int written;
                bool ok = HidNative.WriteFile(_handle, pin.AddrOfPinnedObject(), data.Length, out written,
                    ref overlapped);
                if (!ok)
                {
                    int err = Marshal.GetLastWin32Error();
                    if (err != HidNative.ERROR_IO_PENDING) return false;
                    if (HidNative.WaitForSingleObject(evt, (uint)WriteTimeoutMs) != HidNative.WAIT_OBJECT_0)
                    {
                        HidNative.CancelIoEx(_handle, ref overlapped);
                        HidNative.WaitForSingleObject(evt, CancelGraceMs);
                        return false;
                    }
                    uint transferred;
                    if (!HidNative.GetOverlappedResult(_handle, ref overlapped, out transferred, false))
                        return false;
                }
                return true;
            }
            finally
            {
                HidNative.CloseHandle(evt);
                pin.Free();
            }
        }

        public byte[] Read(int timeoutMs)
        {
            if (timeoutMs < 0) timeoutMs = 0;
            int length = _info.InputReportLen > 0 ? _info.InputReportLen : 64;
            var buffer = new byte[length];
            lock (_sync)
            {
                if (IsDisposed || _handle == IntPtr.Zero || IsFaulted) return null;

                GCHandle pin = GCHandle.Alloc(buffer, GCHandleType.Pinned);
                IntPtr evt = HidNative.CreateEvent(IntPtr.Zero, true, false, null);
                if (evt == IntPtr.Zero)
                {
                    pin.Free();
                    return null;
                }

                bool keepAlive = false;
                try
                {
                    var overlapped = new NativeOverlapped();
                    overlapped.EventHandle = evt;
                    int read;
                    bool ok = HidNative.ReadFile(_handle, pin.AddrOfPinnedObject(), length, out read, ref overlapped);
                    if (ok) return Trim(buffer, read);

                    int err = Marshal.GetLastWin32Error();
                    if (err != HidNative.ERROR_IO_PENDING) return null;

                    int wait = HidNative.WaitForSingleObject(evt, (uint)timeoutMs);
                    if (wait != HidNative.WAIT_OBJECT_0)
                    {
                        // 超时：取消挂起的 I/O，并短暂等待其真正完成，避免缓冲区被提前释放
                        HidNative.CancelIoEx(_handle, ref overlapped);
                        int grace = HidNative.WaitForSingleObject(evt, CancelGraceMs);
                        if (grace != HidNative.WAIT_OBJECT_0)
                        {
                            keepAlive = true; // 驱动异常：缓冲区交给 GC 之外的地方保活，通道标记失效
                            Volatile.Write(ref _faulted, 1);
                            return null;
                        }
                        return null;
                    }

                    uint transferred;
                    if (!HidNative.GetOverlappedResult(_handle, ref overlapped, out transferred, false)) return null;
                    return Trim(buffer, (int)transferred);
                }
                finally
                {
                    HidNative.CloseHandle(evt);
                    if (keepAlive)
                    {
                        lock (StuckBuffers) { StuckBuffers.Add(pin); }
                    }
                    else
                    {
                        pin.Free();
                    }
                }
            }
        }

        private static byte[] Trim(byte[] buffer, int count)
        {
            if (count <= 0) return null;
            if (count >= buffer.Length) return buffer;
            var result = new byte[count];
            Array.Copy(buffer, result, count);
            return result;
        }

        public byte[] GetFeature(byte reportId, int length)
        {
            lock (_sync)
            {
                if (IsDisposed || _handle == IntPtr.Zero) return null;
                int size = Math.Max(Math.Max(length, _info.FeatureReportLen), 2);
                var buffer = new byte[size];
                buffer[0] = reportId;
                if (!HidNative.HidD_GetFeature(_handle, buffer, size)) return null;
                if (length > 0 && length < size)
                {
                    var slice = new byte[length];
                    Array.Copy(buffer, slice, length);
                    return slice;
                }
                return buffer;
            }
        }

        public bool SetFeature(byte[] report)
        {
            if (report == null || report.Length == 0) return false;
            lock (_sync)
            {
                if (IsDisposed || _handle == IntPtr.Zero) return false;
                int size = _info.FeatureReportLen > 0 ? Math.Max(_info.FeatureReportLen, report.Length) : report.Length;
                byte[] buffer = report;
                if (size != report.Length)
                {
                    buffer = new byte[size];
                    Array.Copy(report, buffer, Math.Min(report.Length, size));
                }
                return HidNative.HidD_SetFeature(_handle, buffer, size);
            }
        }

        /// <summary>
        /// 读取某个 Feature usage 的数值（HID 描述符级解析，不假设字节偏移）。
        /// 标准 HID 电量（0x06:0x20）走这里。
        /// </summary>
        public bool TryReadUsageValue(ushort usagePage, ushort usage, out int rawValue, out HidUsageRange caps)
        {
            rawValue = 0;
            caps = null;
            if (_preparsed == IntPtr.Zero) return false;
            HidUsageRange range = FindFeatureUsage(usagePage, usage);
            if (range == null) return false;

            int size = Math.Max(_info.FeatureReportLen, 2);
            var report = new byte[size];
            report[0] = range.ReportId;
            lock (_sync)
            {
                if (IsDisposed || _handle == IntPtr.Zero) return false;
                if (!HidNative.HidD_GetFeature(_handle, report, size)) return false;
            }

            int value;
            if (!HidNative.TryGetUsageValue(_preparsed, HidNative.HIDP_FEATURE, usagePage, usage, report, out value))
                return false;
            rawValue = value;
            caps = range;
            return true;
        }

        public HidUsageRange FindFeatureUsage(ushort usagePage, ushort usage)
        {
            foreach (HidUsageRange range in _info.FeatureUsages)
            {
                if (range.UsagePage != usagePage) continue;
                if (!range.IsRange && range.UsageMin == usage) return range;
                if (range.IsRange && usage >= range.UsageMin && usage <= range.UsageMax) return range;
            }
            return null;
        }

        public bool IsDisposed
        {
            get { return Volatile.Read(ref _disposed) != 0; }
        }

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
            lock (_sync)
            {
                try
                {
                    if (_preparsed != IntPtr.Zero)
                    {
                        HidNative.HidD_FreePreparsedData(_preparsed);
                        _preparsed = IntPtr.Zero;
                    }
                }
                catch
                {
                }
                try
                {
                    if (_handle != IntPtr.Zero && _handle != HidNative.InvalidHandleValue)
                    {
                        HidNative.CloseHandle(_handle);
                    }
                }
                catch
                {
                }
                _handle = IntPtr.Zero;
            }
        }
    }
}
