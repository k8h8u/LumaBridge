using System.Drawing;
using System.Drawing.Drawing2D;
using System.Reflection;
using System.Windows.Forms;

namespace DriverSwitcher;

internal static class Brand
{
    public static readonly Color Orange = Color.FromArgb(0xE7, 0x58, 0x23);
    public static readonly Color OrangeHover = Color.FromArgb(0xD1, 0x4A, 0x1B);
    public static readonly Color Graphite = Color.FromArgb(0x20, 0x21, 0x24);
    public static readonly Color LightBg = Color.FromArgb(0xF3, 0xF4, 0xF6);
    public static readonly Color GrayText = Color.FromArgb(0x88, 0x8B, 0x8D);
    public static readonly Color LightGray = Color.FromArgb(0x9E, 0xA0, 0xA2);
    public static readonly Color Divider = Color.FromArgb(0xE9, 0xEA, 0xEB);

    public const string SiteUrl = "https://luminograph.ru";

    /// <summary>Трей-иконка: официальная монограмма LUMINOGRAPH + точка режима (null — без точки).</summary>
    public static System.Drawing.Icon CreateIcon(Color? modeDot)
    {
        const int s = 64;
        using var bmp = new Bitmap(s, s);
        using (var g = Graphics.FromImage(bmp))
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.Clear(Color.Transparent);

            using var bg = new SolidBrush(LightBg);
            using var rounded = RoundedRect(new Rectangle(1, 1, s - 3, s - 3), 13);
            g.FillPath(bg, rounded);

            using var logo = LoadLogo();
            float h = 50f;
            float w = h * logo.Width / (float)logo.Height;
            g.DrawImage(logo, (s - w) / 2f, (s - h) / 2f, w, h);

            if (modeDot.HasValue)
            {
                using var white = new SolidBrush(Color.White);
                g.FillEllipse(white, s - 21, s - 21, 19, 19);
                using var dot = new SolidBrush(modeDot.Value);
                g.FillEllipse(dot, s - 18, s - 18, 13, 13);
            }
        }
        return System.Drawing.Icon.FromHandle(bmp.GetHicon());
    }

    private static Bitmap LoadLogo()
    {
        var uri = new System.Uri("pack://application:,,,/Assets/LuminoLogo.png");
        using var stream = System.Windows.Application.GetResourceStream(uri).Stream;
        return new Bitmap(stream);
    }

    private static GraphicsPath RoundedRect(Rectangle bounds, int radius)
    {
        var path = new GraphicsPath();
        int d = radius * 2;
        path.AddArc(bounds.X, bounds.Y, d, d, 180, 90);
        path.AddArc(bounds.Right - d, bounds.Y, d, d, 270, 90);
        path.AddArc(bounds.Right - d, bounds.Bottom - d, d, d, 0, 90);
        path.AddArc(bounds.X, bounds.Bottom - d, d, d, 90, 90);
        path.CloseFigure();
        return path;
    }

    public static Font CreateFont(float size)
    {
        string family = "Segoe UI";
        using var collection = new System.Drawing.Text.InstalledFontCollection();
        foreach (var f in collection.Families)
        {
            if (f.Name == "MiSans")
            {
                family = "MiSans";
                break;
            }
        }
        return new Font(family, size);
    }
}

internal sealed class BrandColorTable : ProfessionalColorTable
{
    public override Color MenuItemSelected => Brand.Orange;
    public override Color MenuItemSelectedGradientBegin => Brand.Orange;
    public override Color MenuItemSelectedGradientEnd => Brand.Orange;
    public override Color MenuItemPressedGradientBegin => Brand.OrangeHover;
    public override Color MenuItemPressedGradientMiddle => Brand.OrangeHover;
    public override Color MenuItemPressedGradientEnd => Brand.OrangeHover;
    public override Color MenuItemBorder => Brand.Orange;
    public override Color MenuStripGradientBegin => Color.White;
    public override Color MenuStripGradientEnd => Color.White;
    public override Color MenuBorder => Brand.Divider;
    public override Color ToolStripDropDownBackground => Color.White;
    public override Color ImageMarginGradientBegin => Color.White;
    public override Color ImageMarginGradientMiddle => Color.White;
    public override Color ImageMarginGradientEnd => Color.White;
    public override Color SeparatorDark => Brand.Divider;
    public override Color SeparatorLight => Brand.Divider;
    public override Color CheckBackground => Brand.LightBg;
    public override Color CheckSelectedBackground => Brand.Orange;
    public override Color CheckPressedBackground => Brand.OrangeHover;
}

internal sealed class BrandMenuRenderer : ToolStripProfessionalRenderer
{
    public BrandMenuRenderer() : base(new BrandColorTable()) { }

    protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e)
    {
        bool accent = e.Item.Tag as string == "accent";
        if (!e.Item.Enabled)
            e.TextColor = Brand.LightGray;
        else if (e.Item.Selected)
            e.TextColor = Color.White;
        else if (accent)
            e.TextColor = Brand.Orange;
        else
            e.TextColor = Brand.Graphite;
        base.OnRenderItemText(e);
    }
}
