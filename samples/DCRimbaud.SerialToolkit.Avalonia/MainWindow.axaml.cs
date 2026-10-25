// SPDX-License-Identifier: MIT
// Author: Xue Weisen <xueweisen96@gmail.com>
// GitHub: https://github.com/DCRimbaud
using Avalonia.Controls;
using DCRimbaud.SerialToolkit.Avalonia.ViewModels;

namespace DCRimbaud.SerialToolkit.Avalonia;

public sealed partial class MainWindow : Window
{
    private readonly MainWindowViewModel _viewModel = new();

    public MainWindow()
    {
        InitializeComponent();
        DataContext = _viewModel;
        RefreshButton.Click += (_, _) => _viewModel.Refresh();
        _viewModel.Refresh();
    }
}
