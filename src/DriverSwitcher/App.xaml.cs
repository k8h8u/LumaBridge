using System;
using System.Drawing;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Forms;

namespace DriverSwitcher;

public partial class App : System.Windows.Application
{
    private static Mutex? _mutex;

    private NotifyIcon? _tray;
    private ToolStripMenuItem _statusItem = null!;
    private ToolStripMenuItem _ezcadItem = null!;
    private ToolStripMenuItem _lightBurnItem = null!;

    private Icon _iconEzCad = null!;
    private Icon _iconLightBurn = null!;
    private Icon _iconNone = null!;
    private Icon _iconUnknown = null!;

    private bool _busy;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        _mutex = new Mutex(true, @"Global\DriverSwitcher.SingleInstance", out bool createdNew);
        if (!createdNew)
        {
            Shutdown();
            return;
        }

        _iconEzCad = CreateIcon(Color.FromArgb(46, 160, 67));
        _iconLightBurn = CreateIcon(Color.FromArgb(9, 105, 218));
        _iconNone = CreateIcon(Color.FromArgb(110, 118, 129));
        _iconUnknown = CreateIcon(Color.FromArgb(210, 130, 20));

        _statusItem = new ToolStripMenuItem("Определение статуса...") { Enabled = false };
        _ezcadItem = new ToolStripMenuItem("Режим EzCAD2", null, (_, _) => RunSwitch(DriverMode.EzCad2));
        _lightBurnItem = new ToolStripMenuItem("Режим LightBurn", null, (_, _) => RunSwitch(DriverMode.LightBurn));
        var refreshItem = new ToolStripMenuItem("Обновить статус", null, (_, _) => RefreshStatus());
        var exitItem = new ToolStripMenuItem("Выход", null, (_, _) => ExitApp());

        var menu = new ContextMenuStrip();
        menu.Items.Add(_statusItem);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(_ezcadItem);
        menu.Items.Add(_lightBurnItem);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(refreshItem);
        menu.Items.Add(exitItem);
        menu.Opening += (_, _) =>
        {
            if (!_busy)
                RefreshStatus();
        };

        _tray = new NotifyIcon
        {
            Icon = _iconNone,
            Text = "DriverSwitcher",
            Visible = true,
            ContextMenuStrip = menu,
        };

        RefreshStatus();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        if (_tray != null)
            _tray.Visible = false;
        _tray?.Dispose();
        _mutex?.ReleaseMutex();
        base.OnExit(e);
    }

    private void RunSwitch(DriverMode target)
    {
        if (_busy)
            return;
        _busy = true;
        SetMenuEnabled(false);

        string name = target == DriverMode.EzCad2 ? "EzCAD2" : "LightBurn";
        SetTrayIcon(_iconUnknown, $"Переключение на {name}...");

        Task.Run(() =>
        {
            try
            {
                if (target == DriverMode.EzCad2)
                    DriverManager.SwitchToEzCad2();
                else
                    DriverManager.SwitchToLightBurn();

                Dispatcher.Invoke(() =>
                {
                    FinishSwitch();
                    _tray!.ShowBalloonTip(3000, "DriverSwitcher",
                        $"Готово: режим {name}.", ToolTipIcon.Info);
                });
            }
            catch (Exception ex)
            {
                Dispatcher.Invoke(() =>
                {
                    FinishSwitch();
                    _tray!.ShowBalloonTip(6000, "DriverSwitcher — ошибка",
                        ex.Message, ToolTipIcon.Error);
                });
            }
        });
    }

    private void FinishSwitch()
    {
        _busy = false;
        SetMenuEnabled(true);
        RefreshStatus();
    }

    private void RefreshStatus()
    {
        DriverMode mode;
        string suffix = string.Empty;
        try
        {
            var boards = DriverManager.FindBoards();
            mode = DriverManager.GetMode(boards);
            if (boards.Count > 1)
                suffix = $" ({boards.Count} шт.)";
        }
        catch (Exception ex)
        {
            mode = DriverMode.Unknown;
            _statusItem.Text = "Ошибка: " + ex.Message;
        }

        switch (mode)
        {
            case DriverMode.NoBoard:
                _statusItem.Text = "Плата не найдена";
                SetTrayIcon(_iconNone, "DriverSwitcher: плата не найдена");
                break;
            case DriverMode.EzCad2:
                _statusItem.Text = "Текущий режим: EzCAD2" + suffix;
                SetTrayIcon(_iconEzCad, "DriverSwitcher: режим EzCAD2");
                break;
            case DriverMode.LightBurn:
                _statusItem.Text = "Текущий режим: LightBurn" + suffix;
                SetTrayIcon(_iconLightBurn, "DriverSwitcher: режим LightBurn");
                break;
            default:
                if (_statusItem.Text.StartsWith("Ошибка", StringComparison.Ordinal))
                    SetTrayIcon(_iconUnknown, "DriverSwitcher: ошибка");
                else
                {
                    _statusItem.Text = "Режим не определён" + suffix;
                    SetTrayIcon(_iconUnknown, "DriverSwitcher: режим не определён");
                }
                break;
        }

        _ezcadItem.Checked = mode == DriverMode.EzCad2;
        _lightBurnItem.Checked = mode == DriverMode.LightBurn;
        _ezcadItem.Enabled = !_busy && mode != DriverMode.EzCad2;
        _lightBurnItem.Enabled = !_busy && mode != DriverMode.LightBurn;
    }

    private void SetMenuEnabled(bool enabled)
    {
        _ezcadItem.Enabled = enabled;
        _lightBurnItem.Enabled = enabled;
    }

    private void SetTrayIcon(Icon icon, string text)
    {
        if (_tray == null)
            return;
        _tray.Icon = icon;
        _tray.Text = text.Length > 63 ? text.Substring(0, 63) : text;
    }

    private void ExitApp()
    {
        if (_tray != null)
            _tray.Visible = false;
        Shutdown();
    }

    private static Icon CreateIcon(Color color)
    {
        using var bmp = new Bitmap(32, 32);
        using (var g = Graphics.FromImage(bmp))
        {
            g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            g.Clear(Color.Transparent);
            using var brush = new SolidBrush(color);
            g.FillEllipse(brush, 2, 2, 28, 28);
            using var pen = new Pen(Color.White, 2f);
            g.DrawEllipse(pen, 3, 3, 26, 26);
        }
        return Icon.FromHandle(bmp.GetHicon());
    }
}
