// SPDX-License-Identifier: MIT
// Author: Xue Weisen <xueweisen96@gmail.com>
// GitHub: https://github.com/DCRimbaud
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace DCRimbaud.SerialToolkit.Avalonia.ViewModels;

public sealed class MainWindowViewModel : INotifyPropertyChanged
{
    private string _status = "Refresh to enumerate serial ports.";

    public ObservableCollection<SerialPortDevice> Devices { get; } = new();

    public string Status
    {
        get => _status;
        private set
        {
            _status = value;
            OnPropertyChanged();
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public void Refresh()
    {
        Devices.Clear();
        foreach (var device in SerialPortDeviceEnumerator.Enumerate())
        {
            Devices.Add(device);
        }

        Status = $"Found {Devices.Count} serial device(s).";
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
