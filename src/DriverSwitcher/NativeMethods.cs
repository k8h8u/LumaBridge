using System;
using System.Runtime.InteropServices;
using System.Text;

namespace DriverSwitcher;

internal static class NativeMethods
{
    public const uint DIGCF_PRESENT = 0x00000002;
    public const uint DIGCF_ALLCLASSES = 0x00000004;
    public const uint SPDRP_HARDWAREID = 0x00000001;
    public const uint SPDRP_SERVICE = 0x00000004;
    public const uint INSTALLFLAG_FORCE = 0x00000001;

    public static readonly IntPtr InvalidHandleValue = new IntPtr(-1);

    [StructLayout(LayoutKind.Sequential)]
    public struct SP_DEVINFO_DATA
    {
        public int cbSize;
        public Guid ClassGuid;
        public uint DevInst;
        public IntPtr Reserved;
    }

    [DllImport("setupapi.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    public static extern IntPtr SetupDiGetClassDevs(
        IntPtr classGuid, string? enumerator, IntPtr hwndParent, uint flags);

    [DllImport("setupapi.dll", SetLastError = true)]
    public static extern bool SetupDiEnumDeviceInfo(
        IntPtr deviceInfoSet, uint memberIndex, ref SP_DEVINFO_DATA deviceInfoData);

    [DllImport("setupapi.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    public static extern bool SetupDiGetDeviceInstanceId(
        IntPtr deviceInfoSet, ref SP_DEVINFO_DATA deviceInfoData,
        StringBuilder deviceInstanceId, int deviceInstanceIdSize, out int requiredSize);

    [DllImport("setupapi.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    public static extern bool SetupDiGetDeviceRegistryProperty(
        IntPtr deviceInfoSet, ref SP_DEVINFO_DATA deviceInfoData, uint property,
        out uint propertyRegDataType, byte[]? propertyBuffer, int propertyBufferSize,
        out int requiredSize);

    [DllImport("setupapi.dll", SetLastError = true)]
    public static extern bool SetupDiChangeState(
        IntPtr deviceInfoSet, ref SP_DEVINFO_DATA deviceInfoData);

    [DllImport("setupapi.dll", SetLastError = true)]
    public static extern bool SetupDiDestroyDeviceInfoList(IntPtr deviceInfoSet);

    [DllImport("newdev.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    public static extern bool UpdateDriverForPlugAndPlayDevices(
        IntPtr hwndParent, string hardwareId, string fullInfPath,
        uint installFlags, out bool rebootRequired);
}
