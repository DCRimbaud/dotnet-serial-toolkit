// SPDX-License-Identifier: MIT
// Author: Xue Weisen <xueweisen96@gmail.com>
// GitHub: https://github.com/DCRimbaud
using System.IO.Ports;
using System.Management;
using System.Runtime.Versioning;
using System.Text.RegularExpressions;

namespace DCRimbaud.SerialToolkit;

/// <summary>Known USB-to-serial driver families.</summary>
public enum UsbSerialDriverFamily
{
    Unknown,
    Cp210x,
    Ch340,
    Ftdi,
    Pl2303
}

/// <summary>Serial device information discovered from the operating system.</summary>
public sealed record SerialPortDevice(
    string PortName,
    string DisplayName,
    string DeviceId,
    string Manufacturer,
    string Status,
    string? VendorId,
    string? ProductId,
    UsbSerialDriverFamily DriverFamily)
{
    /// <summary>True when the port is present and the PnP status is usable.</summary>
    public bool DriverInstalled => !string.IsNullOrWhiteSpace(PortName) &&
                                   (string.IsNullOrWhiteSpace(Status) ||
                                    string.Equals(Status, "OK", StringComparison.OrdinalIgnoreCase));
}

/// <summary>Enumerates serial ports and classifies common USB-to-serial adapters.</summary>
public static partial class SerialPortDeviceEnumerator
{
    /// <summary>Enumerate devices; Windows uses PnP metadata and other platforms fall back to port names.</summary>
    public static IReadOnlyList<SerialPortDevice> Enumerate()
    {
        if (!OperatingSystem.IsWindows())
        {
            return EnumerateFallback();
        }

        try
        {
            return EnumerateWindows();
        }
        catch
        {
            return EnumerateFallback();
        }
    }

    /// <summary>Extract USB VID/PID from a Windows device instance ID.</summary>
    public static (string? VendorId, string? ProductId) ParseUsbIds(string? deviceId)
    {
        if (string.IsNullOrWhiteSpace(deviceId))
        {
            return (null, null);
        }

        var match = UsbIdPattern().Match(deviceId);
        return match.Success
            ? (match.Groups[1].Value.ToUpperInvariant(), match.Groups[2].Value.ToUpperInvariant())
            : (null, null);
    }

    /// <summary>Extract a COM port name from a display name.</summary>
    public static string? ParsePortName(string? displayName)
    {
        if (string.IsNullOrWhiteSpace(displayName))
        {
            return null;
        }

        var match = PortNamePattern().Match(displayName);
        return match.Success ? match.Groups[1].Value.ToUpperInvariant() : null;
    }

    /// <summary>Classify a USB-to-serial driver family from VID/PID.</summary>
    public static UsbSerialDriverFamily ClassifyDriver(string? vendorId, string? productId)
    {
        var vendor = vendorId?.Trim().ToUpperInvariant();
        _ = productId;
        return vendor switch
        {
            "10C4" => UsbSerialDriverFamily.Cp210x,
            "1A86" => UsbSerialDriverFamily.Ch340,
            "0403" => UsbSerialDriverFamily.Ftdi,
            "067B" => UsbSerialDriverFamily.Pl2303,
            _ => UsbSerialDriverFamily.Unknown
        };
    }

    [SupportedOSPlatform("windows")]
    private static IReadOnlyList<SerialPortDevice> EnumerateWindows()
    {
        var devices = new List<SerialPortDevice>();
        /* Query only devices that expose a COM port in their friendly name. */
        using var searcher = new ManagementObjectSearcher(
            "SELECT Name, DeviceID, Manufacturer, Status FROM Win32_PnPEntity WHERE Name LIKE '%(COM%'");

        foreach (var item in searcher.Get())
        {
            using var managementObject = (ManagementObject)item;
            var displayName = managementObject["Name"]?.ToString() ?? string.Empty;
            var portName = ParsePortName(displayName);
            if (portName is null)
            {
                continue;
            }

            var deviceId = managementObject["DeviceID"]?.ToString() ?? string.Empty;
            var (vendorId, productId) = ParseUsbIds(deviceId);
            devices.Add(new SerialPortDevice(
                portName,
                displayName,
                deviceId,
                managementObject["Manufacturer"]?.ToString() ?? string.Empty,
                managementObject["Status"]?.ToString() ?? string.Empty,
                vendorId,
                productId,
                ClassifyDriver(vendorId, productId)));
        }

        return devices
            .OrderBy(device => device.PortName, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static IReadOnlyList<SerialPortDevice> EnumerateFallback()
    {
        try
        {
            return SerialPort.GetPortNames()
                .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
                .Select(name => new SerialPortDevice(
                    name,
                    name,
                    string.Empty,
                    string.Empty,
                    string.Empty,
                    null,
                    null,
                    UsbSerialDriverFamily.Unknown))
                .ToArray();
        }
        catch
        {
            return Array.Empty<SerialPortDevice>();
        }
    }

    [GeneratedRegex(@"VID_([0-9A-Fa-f]{4}).*?PID_([0-9A-Fa-f]{4})", RegexOptions.IgnoreCase)]
    private static partial Regex UsbIdPattern();

    [GeneratedRegex(@"\((COM\d+)\)", RegexOptions.IgnoreCase)]
    private static partial Regex PortNamePattern();
}
