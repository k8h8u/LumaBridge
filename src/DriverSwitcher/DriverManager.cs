using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Cryptography.X509Certificates;
using System.Text;

namespace DriverSwitcher;

public enum DriverMode
{
    NoBoard,
    Unknown,
    EzCad2,
    LightBurn
}

public sealed class BoardInfo
{
    public required string InstanceId { get; init; }
    public required string HardwareId { get; init; }
    public required string Service { get; init; }
}

public static class DriverManager
{
    public static readonly string[] HardwareIds =
    {
        @"USB\VID_9588&PID_9980",
        @"USB\VID_9588&PID_9899",
        @"USB\VID_9588&PID_9990",
        @"USB\VID_9588&PID_9999",
    };

    private const string ResourcePrefix = "DriverSwitcher.Drivers.";
    private const string CertResource = "DriverSwitcher.Assets.DriverSwitcher.cer";

    public static List<BoardInfo> FindBoards()
    {
        var result = new List<BoardInfo>();
        IntPtr set = NativeMethods.SetupDiGetClassDevs(
            IntPtr.Zero, "USB", IntPtr.Zero,
            NativeMethods.DIGCF_ALLCLASSES | NativeMethods.DIGCF_PRESENT);
        if (set == NativeMethods.InvalidHandleValue)
            throw new Win32Exception(Marshal.GetLastWin32Error());

        try
        {
            var data = NewDevInfoData();
            for (uint i = 0; NativeMethods.SetupDiEnumDeviceInfo(set, i, ref data); i++)
            {
                string[] hwids = GetRegistryMultiSz(set, ref data, NativeMethods.SPDRP_HARDWAREID);
                string? match = hwids.FirstOrDefault(h =>
                    HardwareIds.Any(k => h.Equals(k, StringComparison.OrdinalIgnoreCase)));
                if (match == null)
                    continue;

                result.Add(new BoardInfo
                {
                    HardwareId = match,
                    Service = GetRegistryString(set, ref data, NativeMethods.SPDRP_SERVICE),
                    InstanceId = GetInstanceId(set, ref data),
                });
            }
        }
        finally
        {
            NativeMethods.SetupDiDestroyDeviceInfoList(set);
        }
        return result;
    }

    public static DriverMode GetMode(IReadOnlyCollection<BoardInfo> boards)
    {
        if (boards.Count == 0)
            return DriverMode.NoBoard;

        bool allWinUsb = boards.All(b => b.Service.Equals("WinUSB", StringComparison.OrdinalIgnoreCase));
        if (allWinUsb)
            return DriverMode.LightBurn;

        bool allEzCad = boards.All(b =>
            b.Service.StartsWith("lmcv2u", StringComparison.OrdinalIgnoreCase) ||
            b.Service.StartsWith("lmcusb", StringComparison.OrdinalIgnoreCase));
        if (allEzCad)
            return DriverMode.EzCad2;

        return DriverMode.Unknown;
    }

    public static void SwitchToEzCad2() => Switch("ezcad2", "Lmcv2u.inf");

    public static void SwitchToLightBurn() => Switch("winusb", "BJJCZ_WinUSB.inf");

    private static void Switch(string driverSet, string infFileName)
    {
        var boards = FindBoards();
        if (boards.Count == 0)
            throw new InvalidOperationException(
                "Плата BJJCZ не найдена. Подключите маркиратор по USB и попробуйте снова.");

        if (driverSet == "winusb")
            EnsureCertificateInstalled();

        string dir = ExtractDriverSet(driverSet);
        string inf = Path.Combine(dir, infFileName);

        var errors = new List<string>();
        foreach (var board in boards)
        {
            bool ok = NativeMethods.UpdateDriverForPlugAndPlayDevices(
                IntPtr.Zero, board.HardwareId, inf,
                NativeMethods.INSTALLFLAG_FORCE, out _);
            if (!ok)
            {
                int err = Marshal.GetLastWin32Error();
                errors.Add($"{board.HardwareId}: {new Win32Exception(err).Message} (0x{err:X8})");
            }
        }

        if (errors.Count == boards.Count)
            throw new InvalidOperationException(
                "Не удалось переключить драйвер:\n" + string.Join("\n", errors));

        RestartBoards(boards);

        if (errors.Count > 0)
            throw new InvalidOperationException(
                "Часть плат не переключена:\n" + string.Join("\n", errors));
    }

    private static void RestartBoards(IEnumerable<BoardInfo> boards)
    {
        var wanted = new HashSet<string>(boards.Select(b => b.InstanceId),
            StringComparer.OrdinalIgnoreCase);

        IntPtr set = NativeMethods.SetupDiGetClassDevs(
            IntPtr.Zero, "USB", IntPtr.Zero,
            NativeMethods.DIGCF_ALLCLASSES | NativeMethods.DIGCF_PRESENT);
        if (set == NativeMethods.InvalidHandleValue)
            return;

        try
        {
            var data = NewDevInfoData();
            for (uint i = 0; NativeMethods.SetupDiEnumDeviceInfo(set, i, ref data); i++)
            {
                string instanceId = GetInstanceId(set, ref data);
                if (!wanted.Contains(instanceId))
                    continue;
                var copy = data;
                NativeMethods.SetupDiChangeState(set, ref copy);
            }
        }
        finally
        {
            NativeMethods.SetupDiDestroyDeviceInfoList(set);
        }
    }

    private static void EnsureCertificateInstalled()
    {
        byte[] bytes;
        var asm = Assembly.GetExecutingAssembly();
        using (var src = asm.GetManifestResourceStream(CertResource)
               ?? throw new InvalidOperationException("Сертификат не найден в ресурсах приложения."))
        using (var ms = new MemoryStream())
        {
            src.CopyTo(ms);
            bytes = ms.ToArray();
        }

        var cert = new X509Certificate2(bytes);
        foreach (var storeName in new[] { StoreName.Root, StoreName.TrustedPublisher })
        {
            using var store = new X509Store(storeName, StoreLocation.LocalMachine);
            store.Open(OpenFlags.ReadWrite | OpenFlags.OpenExistingOnly);
            bool exists = store.Certificates
                .Cast<X509Certificate2>()
                .Any(c => string.Equals(c.Thumbprint, cert.Thumbprint, StringComparison.OrdinalIgnoreCase));
            if (!exists)
                store.Add(cert);
        }
    }

    private static string ExtractDriverSet(string set)
    {
        string root = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
            "DriverSwitcher", "drivers", set);
        Directory.CreateDirectory(root);

        var asm = Assembly.GetExecutingAssembly();
        string prefix = ResourcePrefix + set + ".";
        foreach (string name in asm.GetManifestResourceNames())
        {
            if (!name.StartsWith(prefix, StringComparison.Ordinal))
                continue;
            string file = name.Substring(prefix.Length);
            using var src = asm.GetManifestResourceStream(name)!;
            using var dst = File.Create(Path.Combine(root, file));
            src.CopyTo(dst);
        }
        return root;
    }

    private static NativeMethods.SP_DEVINFO_DATA NewDevInfoData() =>
        new() { cbSize = Marshal.SizeOf<NativeMethods.SP_DEVINFO_DATA>() };

    private static string GetInstanceId(IntPtr set, ref NativeMethods.SP_DEVINFO_DATA data)
    {
        var sb = new StringBuilder(512);
        NativeMethods.SetupDiGetDeviceInstanceId(set, ref data, sb, sb.Capacity, out _);
        return sb.ToString();
    }

    private static string GetRegistryString(IntPtr set, ref NativeMethods.SP_DEVINFO_DATA data, uint property)
    {
        string[] values = GetRegistryMultiSz(set, ref data, property);
        return values.FirstOrDefault() ?? string.Empty;
    }

    private static string[] GetRegistryMultiSz(IntPtr set, ref NativeMethods.SP_DEVINFO_DATA data, uint property)
    {
        NativeMethods.SetupDiGetDeviceRegistryProperty(
            set, ref data, property, out _, null, 0, out int required);
        if (required <= 0)
            return Array.Empty<string>();

        var buffer = new byte[required];
        bool ok = NativeMethods.SetupDiGetDeviceRegistryProperty(
            set, ref data, property, out _, buffer, buffer.Length, out _);
        if (!ok)
            return Array.Empty<string>();

        string text = Encoding.Unicode.GetString(buffer).TrimEnd('\0');
        if (text.Length == 0)
            return Array.Empty<string>();
        return text.Split('\0', StringSplitOptions.RemoveEmptyEntries);
    }
}
