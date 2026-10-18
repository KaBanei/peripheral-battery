using System;
using System.Runtime.InteropServices;

namespace Pb.Core.Platform
{
    /// <summary>
    /// XInput 互操作。刻意不用 [DllImport("xinput1_4.dll")]，而是运行时 LoadLibrary，
    /// 这样在缺少 xinput 的系统（精简版 Windows/Server Core）上可以安全退出。
    /// </summary>
    internal static class XInputInterop
    {
        public const uint ERROR_SUCCESS = 0;
        public const uint ERROR_DEVICE_NOT_CONNECTED = 1167;

        public const byte BATTERY_DEVTYPE_GAMEPAD = 0;

        public const byte BATTERY_TYPE_DISCONNECTED = 0;
        public const byte BATTERY_TYPE_WIRED = 1;
        public const byte BATTERY_TYPE_ALKALINE = 2;
        public const byte BATTERY_TYPE_NIMH = 3;
        public const byte BATTERY_TYPE_LITHIUM = 4;   // 非官方：xinput.h 只定义到 0x03，4 为部分第三方固件使用
        public const byte BATTERY_TYPE_UNKNOWN = 0xFF;

        public const byte BATTERY_LEVEL_EMPTY = 0;
        public const byte BATTERY_LEVEL_LOW = 1;
        public const byte BATTERY_LEVEL_MEDIUM = 2;
        public const byte BATTERY_LEVEL_FULL = 3;

        [StructLayout(LayoutKind.Sequential)]
        public struct XINPUT_GAMEPAD
        {
            public ushort wButtons;
            public byte bLeftTrigger;
            public byte bRightTrigger;
            public short sThumbLX;
            public short sThumbLY;
            public short sThumbRX;
            public short sThumbRY;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct XINPUT_STATE
        {
            public uint dwPacketNumber;
            public XINPUT_GAMEPAD Gamepad;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct XINPUT_BATTERY_INFORMATION
        {
            public byte BatteryType;
            public byte BatteryLevel;
        }

        [UnmanagedFunctionPointer(CallingConvention.Winapi)]
        public delegate uint XInputGetStateDelegate(uint dwUserIndex, out XINPUT_STATE pState);

        [UnmanagedFunctionPointer(CallingConvention.Winapi)]
        public delegate uint XInputGetBatteryInformationDelegate(
            uint dwUserIndex, byte battType, out XINPUT_BATTERY_INFORMATION pBatteryInformation);

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        public static extern IntPtr LoadLibrary(string lpFileName);

        [DllImport("kernel32.dll", CharSet = CharSet.Ansi, SetLastError = true)]
        public static extern IntPtr GetProcAddress(IntPtr hModule, string lpProcName);

        [DllImport("kernel32.dll", SetLastError = true)]
        public static extern bool FreeLibrary(IntPtr hModule);
    }
}
