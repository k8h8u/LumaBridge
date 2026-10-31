using System.Collections.Generic;

namespace DriverSwitcher.Ezd;

/// <summary>Параметры пера EzCAD2 (мощность, скорость, частота и т.д.).</summary>
public sealed class EzdPen
{
    public int Index { get; set; }
    public int ColorBgr { get; set; }
    public string Label { get; set; } = string.Empty;
    public bool MarkEnable { get; set; }
    public int Passes { get; set; } = 1;
    /// <summary>Скорость маркировки, мм/с.</summary>
    public double Speed { get; set; }
    /// <summary>Мощность, % (0-100).</summary>
    public double Power { get; set; }
    /// <summary>Частота, Гц.</summary>
    public double Frequency { get; set; }
    public double StartTc { get; set; }
    public double EndTc { get; set; }
    public double PolygonTc { get; set; }
    public double JumpSpeed { get; set; }
}

public abstract class EzObject
{
    public int Pen { get; set; }
    public int State { get; set; }
    public string Label { get; set; } = string.Empty;
    public double PosX { get; set; }
    public double PosY { get; set; }
    public List<EzObject> Children { get; } = new();
    public bool Hidden => (State & 0x01) != 0;
}

public sealed class EzContour
{
    /// <summary>1 = линии, 2 = квадратичные Безье, 3 = кубические Безье.</summary>
    public int CurveType { get; set; }
    public bool Closed { get; set; }
    public List<double> Points { get; } = new(); // x,y попарно
}

public sealed class EzCurve : EzObject
{
    public List<EzContour> Contours { get; } = new();
}

public sealed class EzRect : EzObject
{
    public double MinX { get; set; }
    public double MinY { get; set; }
    public double MaxX { get; set; }
    public double MaxY { get; set; }
    public double[]? Matrix { get; set; }
}

public sealed class EzCircle : EzObject
{
    public double Cx { get; set; }
    public double Cy { get; set; }
    public double Radius { get; set; }
    public double[]? Matrix { get; set; }
}

public sealed class EzEllipse : EzObject
{
    public double MinX { get; set; }
    public double MinY { get; set; }
    public double MaxX { get; set; }
    public double MaxY { get; set; }
    public double StartAngle { get; set; }
    public double EndAngle { get; set; }
    public double[]? Matrix { get; set; }
}

public sealed class EzPolygon : EzObject
{
    public double MinX { get; set; }
    public double MinY { get; set; }
    public double MaxX { get; set; }
    public double MaxY { get; set; }
    public int Sides { get; set; }
    public double[]? Matrix { get; set; }
}

public sealed class EzGroup : EzObject { }

public sealed class EzCombine : EzObject { }

public sealed class EzVectorFile : EzObject { }

public sealed class EzHatch : EzObject
{
    public bool UnsupportedFormat { get; set; }
    public bool MarkContours { get; set; }

    public bool Hatch1Enabled { get; set; }
    public int Hatch1Pen { get; set; }
    public double Hatch1Angle { get; set; }
    public double Hatch1LineSpace { get; set; } = 0.1;
    public bool Hatch1Crosshatch { get; set; }

    public bool Hatch2Enabled { get; set; }
    public int Hatch2Pen { get; set; }
    public double Hatch2Angle { get; set; }
    public double Hatch2LineSpace { get; set; } = 0.1;

    public bool Hatch3Enabled { get; set; }
    public int Hatch3Pen { get; set; }
    public double Hatch3Angle { get; set; }
    public double Hatch3LineSpace { get; set; } = 0.1;

    /// <summary>Действующее перо штриховки (первая включённая).</summary>
    public int EffectivePen
    {
        get
        {
            if (Hatch1Enabled) return Hatch1Pen;
            if (Hatch2Enabled) return Hatch2Pen;
            if (Hatch3Enabled) return Hatch3Pen;
            return Pen;
        }
    }
}

public sealed class EzText : EzObject
{
    public string Text { get; set; } = string.Empty;
    public string Font { get; set; } = string.Empty;
    public double Height { get; set; }
}

public sealed class EzImage : EzObject { }

/// <summary>Объекты управления (таймеры, IO) — пропускаем, но храним для статистики.</summary>
public sealed class EzOther : EzObject { }

public sealed class EzdFile
{
    public List<EzdPen> Pens { get; } = new();
    public List<EzObject> Objects { get; } = new();
    public List<string> Fonts { get; } = new();
}
