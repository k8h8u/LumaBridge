using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Forms;

namespace LumaBridge;

public partial class App : System.Windows.Application
{
    private static Mutex? _mutex;

    private NotifyIcon? _tray;
    private ToolStripMenuItem _statusItem = null!;
    private ToolStripMenuItem _ezcadItem = null!;
    private ToolStripMenuItem _lightBurnItem = null!;
    private BrandPopup? _popup;

    private Icon _iconEzCad = null!;
    private Icon _iconLightBurn = null!;
    private Icon _iconNone = null!;
    private Icon _iconUnknown = null!;

    private bool _busy;
    private DriverMode _mode = DriverMode.NoBoard;
    private int _boardCount;
    private string? _lastError;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // CLI-режим для тестов и автоматизации:
        // LumaBridge.exe --convert input.ezd output.lbrn2 [report.txt]
        if (e.Args.Length >= 3 && e.Args[0] == "--convert")
        {
            int code = RunCliConvert(e.Args);
            Shutdown(code);
            return;
        }

        _mutex = new Mutex(true, @"Global\LumaBridge.SingleInstance", out bool createdNew);
        if (!createdNew)
        {
            Shutdown();
            return;
        }

        _iconEzCad = Brand.CreateIcon(Brand.Orange);
        _iconLightBurn = Brand.CreateIcon(Brand.Graphite);
        _iconNone = Brand.CreateIcon(null);
        _iconUnknown = Brand.CreateIcon(Brand.LightGray);

        _statusItem = new ToolStripMenuItem("Определение статуса...") { Enabled = false };
        _ezcadItem = new ToolStripMenuItem("Режим EzCAD2", null, (_, _) => RunSwitch(DriverMode.EzCad2));
        _lightBurnItem = new ToolStripMenuItem("Режим LightBurn", null, (_, _) => RunSwitch(DriverMode.LightBurn));
        var convertItem = new ToolStripMenuItem("Макет .ezd → LightBurn…", null, (_, _) => ConvertEzdFile());
        var refreshItem = new ToolStripMenuItem("Обновить статус", null, (_, _) => RefreshStatus());
        var siteItem = new ToolStripMenuItem("luminograph.ru", null, (_, _) => OpenSite())
        {
            Tag = "accent",
            Font = Brand.CreateFont(9f),
        };
        var exitItem = new ToolStripMenuItem("Выход", null, (_, _) => ExitApp());

        var menu = new ContextMenuStrip
        {
            Renderer = new BrandMenuRenderer(),
            Font = Brand.CreateFont(9f),
        };
        menu.Items.Add(_statusItem);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(_ezcadItem);
        menu.Items.Add(_lightBurnItem);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(convertItem);
        menu.Items.Add(refreshItem);
        menu.Items.Add(siteItem);
        menu.Items.Add(exitItem);
        menu.Opening += (_, _) =>
        {
            if (!_busy)
                RefreshStatus();
        };

        _tray = new NotifyIcon
        {
            Icon = _iconNone,
            Text = "LumaBridge",
            Visible = true,
            ContextMenuStrip = menu,
        };
        _tray.MouseClick += Tray_MouseClick;

        RefreshStatus();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        if (_tray != null)
            _tray.Visible = false;
        _tray?.Dispose();
        _popup?.Close();
        _mutex?.ReleaseMutex();
        base.OnExit(e);
    }

    private void Tray_MouseClick(object? sender, MouseEventArgs e)
    {
        if (e.Button != MouseButtons.Left)
            return;

        if (_popup == null)
        {
            _popup = new BrandPopup();
            _popup.SwitchRequested += mode =>
            {
                _popup.Hide();
                RunSwitch(mode);
            };
            _popup.ConvertRequested += () =>
            {
                _popup.Hide();
                ConvertEzdFile();
            };
        }

        if (_popup.IsVisible)
        {
            _popup.Hide();
            return;
        }

        if (!_busy)
            RefreshStatus();
        _popup.UpdateState(_mode, _boardCount, _lastError, _busy);
        _popup.ShowAtTray();
    }

    private static int RunCliConvert(string[] args)
    {
        string source = args[1];
        string target = args[2];
        string? reportPath = args.Length > 3 && !args[3].StartsWith("field=") ? args[3] : null;

        try
        {
            foreach (var a in args.Skip(3))
            {
                if (a.StartsWith("field="))
                {
                    var parts = a.Substring(6).Replace("x", "×").Split('×')
                        .Select(s => double.TryParse(s.Trim(), System.Globalization.NumberStyles.Float,
                            System.Globalization.CultureInfo.InvariantCulture, out double v) ? v : 0)
                        .ToList();
                    if (parts.Count > 0 && parts[0] > 0)
                    {
                        Convert.EzdToLbrn2.FieldWidth = parts[0];
                        Convert.EzdToLbrn2.FieldHeight = parts.Count > 1 && parts[1] > 0 ? parts[1] : parts[0];
                    }
                }
                else if (a == "autocenter=0")
                {
                    Convert.EzdToLbrn2.AutoCenter = false;
                }
            }
            var r = Convert.EzdToLbrn2.Convert(source, target);
            string report =
                $"OK shapes={r.Shapes} contours={r.Contours} layers={r.Layers} " +
                $"skippedText={r.SkippedText} skippedImages={r.SkippedImages} " +
                $"skippedHidden={r.SkippedHidden} penOverflow={r.PenOverflow}";
            if (reportPath != null)
                System.IO.File.WriteAllText(reportPath, report);
            return 0;
        }
        catch (Exception ex)
        {
            if (reportPath != null)
                System.IO.File.WriteAllText(reportPath, "ERROR " + ex.Message);
            return 1;
        }
    }

    private void ConvertEzdFile()
    {
        if (_busy)
            return;

        var settings = AppSettings.Load();
        var dlg = new OpenFileDialog
        {
            Filter = "Проект EzCAD2 (*.ezd)|*.ezd",
            Title = "Выберите файл .ezd для конвертации",
            InitialDirectory = !string.IsNullOrEmpty(settings.LastEzdDir) && System.IO.Directory.Exists(settings.LastEzdDir)
                ? settings.LastEzdDir
                : Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
        };
        if (dlg.ShowDialog() != DialogResult.OK)
            return;

        settings.LastEzdDir = System.IO.Path.GetDirectoryName(dlg.FileName) ?? string.Empty;

        var optionsDlg = new ConvertOptionsDialog(settings)
        {
            Owner = _popup?.IsVisible == true ? _popup : null,
        };
        if (optionsDlg.ShowDialog() != true)
            return;

        settings.FieldWidth = optionsDlg.FieldWidth;
        settings.FieldHeight = optionsDlg.FieldHeight;
        settings.AutoCenter = optionsDlg.AutoCenter;
        settings.Save();

        string source = dlg.FileName;
        string target = System.IO.Path.ChangeExtension(source, ".lbrn2");

        _busy = true;
        SetMenuEnabled(false);
        SetTrayIcon(_iconUnknown, "Конвертация макета...");
        _popup?.UpdateState(_mode, _boardCount, _lastError, busy: true);

        Task.Run(() =>
        {
            string message;
            ToolTipIcon icon = ToolTipIcon.Info;
            string? driverNote = null;
            try
            {
                Convert.EzdToLbrn2.FieldWidth = settings.FieldWidth;
                Convert.EzdToLbrn2.FieldHeight = settings.FieldHeight;
                Convert.EzdToLbrn2.AutoCenter = settings.AutoCenter;
                var r = Convert.EzdToLbrn2.Convert(source, target);
                message = $"{System.IO.Path.GetFileName(target)}\nОбъектов: {r.Shapes}, слоёв: {r.Layers}.";
                var notes = new List<string>();
                if (r.SkippedText > 0)
                    notes.Add($"текст пропущен: {r.SkippedText}");
                if (r.SkippedImages > 0)
                    notes.Add($"картинки пропущены: {r.SkippedImages}");
                if (r.PenOverflow)
                    notes.Add("пенов больше 30 — лишние слиты в один слой");
                if (notes.Count > 0)
                    message += "\n" + string.Join(", ", notes) + ".";

                try
                {
                    var boards = DriverManager.FindBoards();
                    if (DriverManager.GetMode(boards) == DriverMode.EzCad2)
                    {
                        DriverManager.SwitchToLightBurn();
                        driverNote = "Драйвер переключён на LightBurn.";
                    }
                }
                catch (Exception ex)
                {
                    driverNote = "Драйвер не переключён: " + ex.Message;
                }

                try
                {
                    Process.Start(new ProcessStartInfo { FileName = target, UseShellExecute = true });
                }
                catch
                {
                    // LightBurn не ассоциирован с .lbrn2 — файл просто лежит рядом с исходником
                }
            }
            catch (Exception ex)
            {
                message = "Ошибка конвертации: " + ex.Message;
                icon = ToolTipIcon.Error;
            }

            Dispatcher.Invoke(() =>
            {
                FinishSwitch();
                string note = driverNote != null ? "\n" + driverNote : string.Empty;
                _tray!.ShowBalloonTip(8000, "LumaBridge — конвертация", message + note, icon);
            });
        });
    }

    private void RunSwitch(DriverMode target)
    {
        if (_busy)
            return;
        _busy = true;
        SetMenuEnabled(false);

        string name = target == DriverMode.EzCad2 ? "EzCAD2" : "LightBurn";
        SetTrayIcon(_iconUnknown, $"Переключение на {name}...");
        _popup?.UpdateState(_mode, _boardCount, _lastError, busy: true);

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
                    _tray!.ShowBalloonTip(3000, "LumaBridge",
                        $"Готово: режим {name}.", ToolTipIcon.Info);
                });
            }
            catch (Exception ex)
            {
                Dispatcher.Invoke(() =>
                {
                    FinishSwitch();
                    _tray!.ShowBalloonTip(6000, "LumaBridge — ошибка",
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
        _lastError = null;
        try
        {
            var boards = DriverManager.FindBoards();
            _boardCount = boards.Count;
            _mode = DriverManager.GetMode(boards);
        }
        catch (Exception ex)
        {
            _boardCount = 0;
            _mode = DriverMode.Unknown;
            _lastError = ex.Message;
        }

        string suffix = _boardCount > 1 ? $" ({_boardCount} шт.)" : string.Empty;
        switch (_mode)
        {
            case DriverMode.NoBoard:
                _statusItem.Text = "Плата не найдена";
                SetTrayIcon(_iconNone, "LumaBridge: плата не найдена");
                break;
            case DriverMode.EzCad2:
                _statusItem.Text = "Текущий режим: EzCAD2" + suffix;
                SetTrayIcon(_iconEzCad, "LumaBridge: режим EzCAD2");
                break;
            case DriverMode.LightBurn:
                _statusItem.Text = "Текущий режим: LightBurn" + suffix;
                SetTrayIcon(_iconLightBurn, "LumaBridge: режим LightBurn");
                break;
            default:
                _statusItem.Text = _lastError != null
                    ? "Ошибка: " + _lastError
                    : "Режим не определён" + suffix;
                SetTrayIcon(_iconUnknown, "LumaBridge: режим не определён");
                break;
        }

        _ezcadItem.Checked = _mode == DriverMode.EzCad2;
        _lightBurnItem.Checked = _mode == DriverMode.LightBurn;
        _ezcadItem.Enabled = !_busy && _mode != DriverMode.EzCad2;
        _lightBurnItem.Enabled = !_busy && _mode != DriverMode.LightBurn;

        if (_popup != null && _popup.IsVisible)
            _popup.UpdateState(_mode, _boardCount, _lastError, _busy);
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

    private static void OpenSite()
    {
        Process.Start(new ProcessStartInfo { FileName = Brand.SiteUrl, UseShellExecute = true });
    }

    private void ExitApp()
    {
        if (_tray != null)
            _tray.Visible = false;
        Shutdown();
    }
}
