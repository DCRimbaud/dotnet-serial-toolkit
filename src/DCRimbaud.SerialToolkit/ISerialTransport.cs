// SPDX-License-Identifier: MIT
// Author: Xue Weisen <xueweisen96@gmail.com>
// GitHub: https://github.com/DCRimbaud
using System.IO.Ports;
using System.Text;

namespace DCRimbaud.SerialToolkit;

/// <summary>Minimal serial I/O contract used by command sessions and host fakes.</summary>
public interface ISerialTransport : IDisposable
{
    /// <summary>Whether the port is open.</summary>
    bool IsOpen { get; }

    /// <summary>Text encoding used by <see cref="Write(string)"/>.</summary>
    Encoding Encoding { get; }

    /// <summary>Open the port.</summary>
    void Open();

    /// <summary>Read raw bytes.</summary>
    int Read(byte[] buffer, int offset, int count);

    /// <summary>Write text using <see cref="Encoding"/>.</summary>
    void Write(string text);

    /// <summary>Write raw bytes.</summary>
    void Write(byte[] buffer, int offset, int count);

    /// <summary>Close the port.</summary>
    void Close();
}

/// <summary>Adapter over <see cref="SerialPort"/>.</summary>
public sealed class SerialPortTransport : ISerialTransport
{
    private readonly SerialPort _port;

    /// <summary>Wrap an already configured port.</summary>
    public SerialPortTransport(SerialPort port)
    {
        ArgumentNullException.ThrowIfNull(port);
        _port = port;
    }

    /// <inheritdoc />
    public bool IsOpen => _port.IsOpen;

    /// <inheritdoc />
    public Encoding Encoding => _port.Encoding;

    /// <inheritdoc />
    public void Open() => _port.Open();

    /// <inheritdoc />
    public int Read(byte[] buffer, int offset, int count) => _port.Read(buffer, offset, count);

    /// <inheritdoc />
    public void Write(string text) => _port.Write(text);

    /// <inheritdoc />
    public void Write(byte[] buffer, int offset, int count) => _port.Write(buffer, offset, count);

    /// <inheritdoc />
    public void Close() => _port.Close();

    /// <inheritdoc />
    public void Dispose() => _port.Dispose();
}
