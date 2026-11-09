using System;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace LumaBridge;

public partial class BrandPopup : Window
{
    private static readonly SolidColorBrush OrangeBrush = Freeze(new SolidColorBrush(Color.FromRgb(0xE7, 0x58, 0x23)));
    private static readonly SolidColorBrush GraphiteBrush = Freeze(new SolidColorBrush(Color.FromRgb(0x20, 0x21, 0x24)));
    private static readonly SolidColorBrush LightGrayBrush = Freeze(new SolidColorBrush(Color.FromRgb(0x9E, 0xA0, 0xA2)));
    private static readonly SolidColorBrush WhiteBrush = Freeze(new SolidColorBrush(Colors.White));
    private static readonly SolidColorBrush DividerBrush = Freeze(new SolidColorBrush(Color.FromRgb(0xE9, 0xEA, 0xEB)));

    private DriverMode _mode = DriverMode.NoBoard;
    private bool _busy;

    public event Action<DriverMode>? SwitchRequested;
    public event Action? ConvertRequested;

    public BrandPopup()
    {
        InitializeComponent();
        ApplyButtonStyle(ConvertButton, active: false);
    }

    public void UpdateState(DriverMode mode, int boardCount, string? error, bool busy)
    {
        _mode = mode;
        _busy = busy;

        if (busy)
        {
            StatusDot.Fill = LightGrayBrush;
            StatusText.Text = "Переключение...";
        }
        else if (error != null)
        {
            StatusDot.Fill = LightGrayBrush;
            StatusText.Text = "Ошибка: " + error;
        }
        else
        {
            string suffix = boardCount > 1 ? $" ({boardCount} шт.)" : string.Empty;
            switch (mode)
            {
                case DriverMode.EzCad2:
                    StatusDot.Fill = OrangeBrush;
                    StatusText.Text = "Текущий режим: EzCAD2" + suffix;
                    break;
                case DriverMode.LightBurn:
                    StatusDot.Fill = GraphiteBrush;
                    StatusText.Text = "Текущий режим: LightBurn" + suffix;
                    break;
                case DriverMode.NoBoard:
                    StatusDot.Fill = LightGrayBrush;
                    StatusText.Text = "Плата не найдена";
                    break;
                default:
                    StatusDot.Fill = LightGrayBrush;
                    StatusText.Text = "Режим не определён" + suffix;
                    break;
            }
        }

        ApplyButtonStyle(EzCadButton, active: mode == DriverMode.EzCad2);
        ApplyButtonStyle(LightBurnButton, active: mode == DriverMode.LightBurn);
        ConvertButton.IsEnabled = !busy;
    }

    public void ShowAtTray()
    {
        Show();
        var workArea = SystemParameters.WorkArea;
        Left = workArea.Right - ActualWidth - 12;
        Top = workArea.Bottom - ActualHeight - 12;
        Activate();
    }

    private void ApplyButtonStyle(Button button, bool active)
    {
        if (active)
        {
            button.Background = OrangeBrush;
            button.Foreground = WhiteBrush;
            button.BorderBrush = OrangeBrush;
        }
        else
        {
            button.Background = WhiteBrush;
            button.Foreground = GraphiteBrush;
            button.BorderBrush = DividerBrush;
        }
        button.IsEnabled = !_busy;
    }

    private void EzCadButton_Click(object sender, RoutedEventArgs e)
    {
        if (_mode == DriverMode.EzCad2)
            return;
        SwitchRequested?.Invoke(DriverMode.EzCad2);
    }

    private void LightBurnButton_Click(object sender, RoutedEventArgs e)
    {
        if (_mode == DriverMode.LightBurn)
            return;
        SwitchRequested?.Invoke(DriverMode.LightBurn);
    }

    private void ConvertButton_Click(object sender, RoutedEventArgs e)
    {
        ConvertRequested?.Invoke();
    }

    private void SiteLink_Click(object sender, MouseButtonEventArgs e)
    {
        Process.Start(new ProcessStartInfo { FileName = Brand.SiteUrl, UseShellExecute = true });
    }

    private void Window_Deactivated(object? sender, EventArgs e)
    {
        Hide();
    }

    private static SolidColorBrush Freeze(SolidColorBrush brush)
    {
        brush.Freeze();
        return brush;
    }
}
