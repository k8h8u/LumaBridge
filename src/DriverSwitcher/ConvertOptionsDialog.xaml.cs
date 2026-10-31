using System;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using System.Windows;

namespace DriverSwitcher;

public partial class ConvertOptionsDialog : Window
{
    public double FieldWidth { get; private set; }
    public double FieldHeight { get; private set; }
    public bool AutoCenter { get; private set; }

    public ConvertOptionsDialog(AppSettings settings)
    {
        InitializeComponent();
        FieldCombo.Text = $"{settings.FieldWidth.ToString("0.##", CultureInfo.InvariantCulture)}×{settings.FieldHeight.ToString("0.##", CultureInfo.InvariantCulture)}";
        AutoCenterCheck.IsChecked = settings.AutoCenter;
    }

    private void Ok_Click(object sender, RoutedEventArgs e)
    {
        var nums = Regex.Matches(FieldCombo.Text.Replace("x", "×"), @"[\d.,]+")
            .Cast<Match>()
            .Select(m => double.TryParse(m.Value.Replace(",", "."), NumberStyles.Float, CultureInfo.InvariantCulture, out double v) ? v : 0)
            .Where(v => v > 0)
            .ToList();

        double w = nums.Count > 0 ? nums[0] : 110;
        double h = nums.Count > 1 ? nums[1] : w;
        FieldWidth = Math.Clamp(w, 10, 2000);
        FieldHeight = Math.Clamp(h, 10, 2000);
        AutoCenter = AutoCenterCheck.IsChecked == true;
        DialogResult = true;
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
    }
}
