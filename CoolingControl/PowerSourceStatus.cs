namespace CoolingControl;

using System.Runtime.InteropServices;
using Serilog;

public static class PowerSourceStatus
{
    public static bool? GetIsAcPowered()
    {
        if (!GetSystemPowerStatus(out var status))
        {
            Log.Warning("Unable to read system power status: Win32 error {ErrorCode}", Marshal.GetLastWin32Error());
            return null;
        }

        return status.ACLineStatus switch
        {
            0 => false,
            1 => true,
            _ => null
        };
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetSystemPowerStatus(out SystemPowerStatus systemPowerStatus);

    [StructLayout(LayoutKind.Sequential)]
    private struct SystemPowerStatus
    {
        public byte ACLineStatus;
        public byte BatteryFlag;
        public byte BatteryLifePercent;
        public byte SystemStatusFlag;
        public uint BatteryLifeTime;
        public uint BatteryFullLifeTime;
    }
}
