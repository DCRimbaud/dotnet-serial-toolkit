// SPDX-License-Identifier: MIT
// Author: Xue Weisen <xueweisen96@gmail.com>
// GitHub: https://github.com/DCRimbaud
using System.IO.Ports;

namespace DCRimbaud.SerialToolkit;

/// <summary>Common serial-port connection settings.</summary>
public sealed record SerialPortSettings(
    int BaudRate = 115200,
    int DataBits = 8,
    Parity Parity = Parity.None,
    StopBits StopBits = StopBits.One,
    Handshake Handshake = Handshake.None,
    bool DtrEnable = false,
    bool RtsEnable = false,
    int ReadTimeoutMs = 250,
    int WriteTimeoutMs = 500,
    string EncodingName = "UTF-8",
    int ReadBufferSize = 65536,
    int WriteBufferSize = 65536)
{
    /// <summary>Default 115200 8N1 settings with no flow control.</summary>
    public static SerialPortSettings Default { get; } = new();
}
