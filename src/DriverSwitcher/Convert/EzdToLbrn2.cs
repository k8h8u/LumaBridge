using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using DriverSwitcher.Ezd;

namespace DriverSwitcher.Convert;

/// <summary>
/// Конвертер .ezd (EzCAD2) → .lbrn2 (LightBurn).
/// Геометрия, параметры пенов (мощность/скорость/частота/проходы), штриховки.
/// </summary>
public static class EzdToLbrn2
{
    /// <summary>Размер рабочего поля, мм (центр EzCAD → центр поля LightBurn).</summary>
    public static double FieldWidth { get; set; } = 110.0;
    public static double FieldHeight { get; set; } = 110.0;

    public sealed class ConvertResult
    {
        public int Shapes;
        public int Contours;
        public int Layers;
        public int SkippedText;
        public int SkippedImages;
        public int SkippedHidden;
        public bool PenOverflow;
    }

    private sealed class LbShape
    {
        public int Pen;
        public bool Hatched;
        public bool MarkContours;
        public double HatchInterval = 0.1;
        public double HatchAngle;
        public bool HatchCross;
        public List<LbContour> Contours = new();
    }

    private sealed class LbContour
    {
        public bool Closed;
        public double StartX, StartY;
        public List<LbSeg> Segs = new();
    }

    private readonly struct LbSeg
    {
        public readonly bool IsCubic;
        public readonly double X, Y, C0X, C0Y, C1X, C1Y;
        private LbSeg(bool cubic, double c0x, double c0y, double c1x, double c1y, double x, double y)
        {
            IsCubic = cubic; C0X = c0x; C0Y = c0y; C1X = c1x; C1Y = c1y; X = x; Y = y;
        }
        public static LbSeg Line(double x, double y) => new(false, 0, 0, 0, 0, x, y);
        public static LbSeg Cubic(double c0x, double c0y, double c1x, double c1y, double x, double y) =>
            new(true, c0x, c0y, c1x, c1y, x, y);
    }

    public static ConvertResult Convert(string ezdPath, string lbrn2Path)
    {
        var ezd = EzdReader.Load(ezdPath);
        return Convert(ezd, lbrn2Path);
    }

    public static ConvertResult Convert(EzdFile ezd, string lbrn2Path)
    {
        var result = new ConvertResult();
        var shapes = new List<LbShape>();

        foreach (var obj in ezd.Objects)
            Flatten(obj, shapes, result, penOverride: null, hatch: null);

        // Пены → слои LightBurn (макс. 30)
        var usedPens = shapes.Select(s => s.Pen).Distinct().OrderBy(p => p).ToList();
        var penToLayer = new Dictionary<int, int>();
        foreach (int pen in usedPens)
        {
            if (penToLayer.Count >= 30)
            {
                result.PenOverflow = true;
                penToLayer[pen] = 29;
            }
            else
            {
                penToLayer[pen] = penToLayer.Count;
            }
        }

        string xml = BuildXml(ezd, shapes, penToLayer);
        System.IO.File.WriteAllText(lbrn2Path, xml, new UTF8Encoding(false));

        result.Shapes = shapes.Count;
        result.Contours = shapes.Sum(s => s.Contours.Count);
        result.Layers = Math.Min(usedPens.Count, 30);
        return result;
    }

    // ---------- flatten ----------

    private sealed class HatchCtx
    {
        public int Pen;
        public double Interval = 0.1;
        public double Angle;
        public bool Cross;
        public bool MarkContours;
    }

    private static void Flatten(EzObject obj, List<LbShape> shapes, ConvertResult result,
        int? penOverride, HatchCtx? hatch)
    {
        if (obj.Hidden)
        {
            result.SkippedHidden++;
            return;
        }

        switch (obj)
        {
            case EzCurve curve:
            {
                var shape = NewShape(shapes, hatch, penOverride ?? curve.Pen);
                foreach (var contour in curve.Contours)
                    AddCurveContour(shape, contour, result);
                break;
            }
            case EzRect rect:
            {
                var shape = NewShape(shapes, hatch, penOverride ?? rect.Pen);
                var c = new LbContour { Closed = true };
                (double x, double y) p0 = ApplyMatrix(rect.Matrix, rect.MinX, rect.MinY);
                c.StartX = p0.x; c.StartY = p0.y;
                c.Segs.Add(LbSeg.Line(ApplyMatrix(rect.Matrix, rect.MaxX, rect.MinY).x,
                                      ApplyMatrix(rect.Matrix, rect.MaxX, rect.MinY).y));
                c.Segs.Add(LbSeg.Line(ApplyMatrix(rect.Matrix, rect.MaxX, rect.MaxY).x,
                                      ApplyMatrix(rect.Matrix, rect.MaxX, rect.MaxY).y));
                c.Segs.Add(LbSeg.Line(ApplyMatrix(rect.Matrix, rect.MinX, rect.MaxY).x,
                                      ApplyMatrix(rect.Matrix, rect.MinX, rect.MaxY).y));
                shape.Contours.Add(c);
                break;
            }
            case EzCircle circle:
            {
                var shape = NewShape(shapes, hatch, penOverride ?? circle.Pen);
                shape.Contours.Add(EllipseContour(circle.Cx, circle.Cy, circle.Radius, circle.Radius, circle.Matrix));
                break;
            }
            case EzEllipse ellipse:
            {
                var shape = NewShape(shapes, hatch, penOverride ?? ellipse.Pen);
                double cx = (ellipse.MinX + ellipse.MaxX) / 2, cy = (ellipse.MinY + ellipse.MaxY) / 2;
                double rx = (ellipse.MaxX - ellipse.MinX) / 2, ry = (ellipse.MaxY - ellipse.MinY) / 2;
                double sweep = ellipse.EndAngle - ellipse.StartAngle;
                bool full = Math.Abs(Math.Abs(sweep) - 2 * Math.PI) < 1e-6 ||
                            (Math.Abs(ellipse.StartAngle) < 1e-9 && Math.Abs(ellipse.EndAngle) < 1e-9) ||
                            Math.Abs(sweep) >= 2 * Math.PI;
                if (full)
                {
                    shape.Contours.Add(EllipseContour(cx, cy, rx, ry, ellipse.Matrix));
                }
                else
                {
                    // Дуга → полилиния
                    var c = new LbContour { Closed = false };
                    const int n = 64;
                    for (int i = 0; i <= n; i++)
                    {
                        double t = ellipse.StartAngle + sweep * i / n;
                        var (x, y) = ApplyMatrix(ellipse.Matrix, cx + rx * Math.Cos(t), cy + ry * Math.Sin(t));
                        if (i == 0) { c.StartX = x; c.StartY = y; }
                        else c.Segs.Add(LbSeg.Line(x, y));
                    }
                    shape.Contours.Add(c);
                }
                break;
            }
            case EzPolygon poly:
            {
                var shape = NewShape(shapes, hatch, penOverride ?? poly.Pen);
                int sides = Math.Max(3, poly.Sides);
                double cx = (poly.MinX + poly.MaxX) / 2, cy = (poly.MinY + poly.MaxY) / 2;
                double rx = (poly.MaxX - poly.MinX) / 2, ry = (poly.MaxY - poly.MinY) / 2;
                double step = 2 * Math.PI / sides;
                double theta = step / 2.0;
                var c = new LbContour { Closed = true };
                for (int i = 0; i < sides; i++)
                {
                    var (x, y) = ApplyMatrix(poly.Matrix, cx + Math.Cos(theta) * rx, cy + Math.Sin(theta) * ry);
                    if (i == 0) { c.StartX = x; c.StartY = y; }
                    else c.Segs.Add(LbSeg.Line(x, y));
                    theta += step;
                }
                shape.Contours.Add(c);
                break;
            }
            case EzHatch hz:
            {
                var ctx = new HatchCtx { Pen = hz.EffectivePen, MarkContours = hz.MarkContours };
                if (hz.Hatch1Enabled)
                {
                    ctx.Angle = hz.Hatch1Angle;
                    ctx.Interval = hz.Hatch1LineSpace > 0 ? hz.Hatch1LineSpace : 0.1;
                    ctx.Cross = hz.Hatch1Crosshatch;
                }
                else if (hz.Hatch2Enabled)
                {
                    ctx.Angle = hz.Hatch2Angle;
                    ctx.Interval = hz.Hatch2LineSpace > 0 ? hz.Hatch2LineSpace : 0.1;
                }
                else if (hz.Hatch3Enabled)
                {
                    ctx.Angle = hz.Hatch3Angle;
                    ctx.Interval = hz.Hatch3LineSpace > 0 ? hz.Hatch3LineSpace : 0.1;
                }
                foreach (var child in hz.Children)
                    Flatten(child, shapes, result, penOverride: ctx.Pen, hatch: ctx);
                break;
            }
            case EzGroup or EzCombine or EzVectorFile:
                foreach (var child in obj.Children)
                    Flatten(child, shapes, result, penOverride, hatch);
                break;
            case EzText:
                result.SkippedText++;
                break;
            case EzImage:
                result.SkippedImages++;
                break;
            default:
                // EzOther (таймеры, IO) — игнорируем
                break;
        }
    }

    private static LbShape NewShape(List<LbShape> shapes, HatchCtx? hatch, int pen)
    {
        var shape = new LbShape { Pen = pen };
        if (hatch != null)
        {
            shape.Hatched = true;
            shape.MarkContours = hatch.MarkContours;
            shape.HatchInterval = hatch.Interval;
            shape.HatchAngle = hatch.Angle;
            shape.HatchCross = hatch.Cross;
        }
        shapes.Add(shape);
        return shape;
    }

    private static void AddCurveContour(LbShape shape, EzContour contour, ConvertResult result)
    {
        var pts = contour.Points;
        if (pts.Count < 4)
            return;
        var c = new LbContour { Closed = contour.Closed };
        c.StartX = pts[0];
        c.StartY = pts[1];

        switch (contour.CurveType)
        {
            case 1: // линии
                for (int i = 2; i + 1 < pts.Count; i += 2)
                    c.Segs.Add(LbSeg.Line(pts[i], pts[i + 1]));
                break;
            case 2: // квадратичные Безье → кубические
                for (int i = 2; i + 3 < pts.Count; i += 4)
                {
                    double x0 = c.Segs.Count == 0 ? c.StartX : LastX(c);
                    double y0 = c.Segs.Count == 0 ? c.StartY : LastY(c);
                    double qx = pts[i], qy = pts[i + 1];
                    double x1 = pts[i + 2], y1 = pts[i + 3];
                    c.Segs.Add(LbSeg.Cubic(
                        x0 + 2.0 / 3.0 * (qx - x0), y0 + 2.0 / 3.0 * (qy - y0),
                        x1 + 2.0 / 3.0 * (qx - x1), y1 + 2.0 / 3.0 * (qy - y1),
                        x1, y1));
                }
                break;
            case 3: // кубические
                for (int i = 2; i + 5 < pts.Count; i += 6)
                    c.Segs.Add(LbSeg.Cubic(pts[i], pts[i + 1], pts[i + 2], pts[i + 3], pts[i + 4], pts[i + 5]));
                break;
        }

        if (c.Segs.Count > 0)
            shape.Contours.Add(c);
    }

    private static double LastX(LbContour c) => c.Segs[^1].X;
    private static double LastY(LbContour c) => c.Segs[^1].Y;

    private static LbContour EllipseContour(double cx, double cy, double rx, double ry, double[]? matrix)
    {
        const double k = 0.5522847498307936;
        var c = new LbContour { Closed = true };
        var pts = new (double x, double y)[]
        {
            (cx + rx, cy),
            (cx + rx, cy + k * ry), (cx + k * rx, cy + ry), (cx, cy + ry),
            (cx - k * rx, cy + ry), (cx - rx, cy + k * ry), (cx - rx, cy),
            (cx - rx, cy - k * ry), (cx - k * rx, cy - ry), (cx, cy - ry),
            (cx + k * rx, cy - ry), (cx + rx, cy - k * ry), (cx + rx, cy),
        };
        for (int i = 0; i < pts.Length; i++)
            pts[i] = ApplyMatrix(matrix, pts[i].x, pts[i].y);
        c.StartX = pts[0].x;
        c.StartY = pts[0].y;
        for (int i = 1; i + 2 < pts.Length; i += 3)
            c.Segs.Add(LbSeg.Cubic(pts[i].x, pts[i].y, pts[i + 1].x, pts[i + 1].y, pts[i + 2].x, pts[i + 2].y));
        return c;
    }

    private static (double x, double y) ApplyMatrix(double[]? m, double x, double y)
    {
        if (m == null)
            return (x, y);
        return (m[0] * x + m[3] * y + m[6], m[1] * x + m[4] * y + m[7]);
    }

    // ---------- lbrn2 writer ----------

    private static string F(double v) =>
        (Math.Abs(v) < 1e-12 ? 0 : v).ToString("0.######", CultureInfo.InvariantCulture);

    private static (double x, double y) ToLb(double x, double y) =>
        (x + FieldWidth / 2.0, FieldHeight / 2.0 - y);

    private static string BuildXml(EzdFile ezd, List<LbShape> shapes, Dictionary<int, int> penToLayer)
    {
        var sb = new StringBuilder();
        sb.AppendLine("<?xml version=\"1.0\" encoding=\"UTF-8\"?>");
        sb.AppendLine("<LightBurnProject AppVersion=\"2.1.03\" FormatVersion=\"1\" MaterialHeight=\"0\" MirrorX=\"False\" MirrorY=\"False\">");

        // --- CutSettings ---
        var layerPens = penToLayer
            .GroupBy(kv => kv.Value)
            .Select(g => g.OrderBy(kv => kv.Key).First())
            .OrderBy(kv => kv.Value)
            .ToList();

        foreach (var kv in layerPens)
        {
            int layer = kv.Value;
            var pen = ezd.Pens.ElementAtOrDefault(kv.Key) ?? new EzdPen();
            var layerShapes = shapes.Where(s => penToLayer[s.Pen] == layer).ToList();
            bool anyHatch = layerShapes.Any(s => s.Hatched);
            bool anyLine = layerShapes.Any(s => !s.Hatched || s.MarkContours);
            string type = anyHatch && anyLine ? "Scan+Cut" : anyHatch ? "Scan" : "Cut";
            var hatched = layerShapes.FirstOrDefault(s => s.Hatched);
            double interval = hatched?.HatchInterval ?? 0.1;
            double angle = hatched?.HatchAngle ?? 0;
            bool cross = hatched?.HatchCross ?? false;

            sb.AppendLine($"    <CutSetting type=\"{type}\">");
            WriteSetting(sb, "index", layer);
            WriteSetting(sb, "name", $"C{layer:D2}" + (string.IsNullOrEmpty(pen.Label) ? "" : $" {pen.Label}"));
            WriteSetting(sb, "minPower", pen.Power);
            WriteSetting(sb, "maxPower", pen.Power);
            WriteSetting(sb, "minPower2", 10);
            WriteSetting(sb, "maxPower2", 20);
            WriteSetting(sb, "speed", pen.Speed);
            WriteSetting(sb, "kerf", 0);
            WriteSetting(sb, "zOffset", 0);
            WriteSetting(sb, "enableLaser1", 1);
            WriteSetting(sb, "enableLaser2", 0);
            WriteSetting(sb, "startDelay", pen.StartTc);
            WriteSetting(sb, "endDelay", pen.EndTc);
            WriteSetting(sb, "throughPower", 0);
            WriteSetting(sb, "throughPower2", 0);
            WriteSetting(sb, "enableCutThroughStart", 0);
            WriteSetting(sb, "enableCutThroughEnd", 0);
            WriteSetting(sb, "priority", 0);
            WriteSetting(sb, "frequency", pen.Frequency);
            WriteSetting(sb, "overrideFrequency", 1);
            WriteSetting(sb, "PPI", 0);
            WriteSetting(sb, "enablePPI", 0);
            WriteSetting(sb, "doOutput", 1);
            WriteSetting(sb, "hide", 0);
            WriteSetting(sb, "runBlower", 0);
            WriteSetting(sb, "autoBlower", 0);
            WriteSetting(sb, "blowerSpeedOverride", 0);
            WriteSetting(sb, "blowerSpeedPercent", 100);
            WriteSetting(sb, "overcut", 0);
            WriteSetting(sb, "rampLength", 0);
            WriteSetting(sb, "rampOuter", 0);
            WriteSetting(sb, "forceConstantPower", 0);
            WriteSetting(sb, "numPasses", Math.Max(1, pen.Passes));
            WriteSetting(sb, "zPerPass", 0);
            WriteSetting(sb, "perforate", 0);
            WriteSetting(sb, "perfLen", 0.1);
            WriteSetting(sb, "perfSkip", 0.1);
            WriteSetting(sb, "dotMode", 0);
            WriteSetting(sb, "dotTime", 1);
            WriteSetting(sb, "dotSpacing", 0.1);
            WriteSetting(sb, "tabsEnabled", 0);
            WriteSetting(sb, "manualTabs", 1);
            WriteSetting(sb, "tabSize", 0.5);
            WriteSetting(sb, "tabCount", 1);
            WriteSetting(sb, "tabCountMax", 1);
            WriteSetting(sb, "limitMaxTabs", 0);
            WriteSetting(sb, "tabSpacing", 50);
            WriteSetting(sb, "skipInnerTabs", 0);
            WriteSetting(sb, "tabsUseSpacing", 1);
            WriteSetting(sb, "tabCutPower", 0);
            WriteSetting(sb, "usePen", 0);
            WriteSetting(sb, "scanOpt", "mergeAll");
            WriteSetting(sb, "bidir", 1);
            WriteSetting(sb, "crossHatch", cross ? 1 : 0);
            WriteSetting(sb, "overscan", 0);
            WriteSetting(sb, "overscanPercent", 2.5);
            WriteSetting(sb, "floodFill", 0);
            WriteSetting(sb, "interval", interval);
            WriteSetting(sb, "angle", angle);
            WriteSetting(sb, "cellsPerInch", 50);
            WriteSetting(sb, "halftoneAngle", 22.5);
            sb.AppendLine("    </CutSetting>");
        }

        // --- Shapes ---
        int vertIdCounter = 0;
        foreach (var shape in shapes)
        {
            foreach (var contour in shape.Contours)
            {
                int layer = penToLayer[shape.Pen];
                vertIdCounter += contour.Segs.Count + 1;
                var verts = new List<(double x, double y, bool hasC0, double c0x, double c0y, bool hasC1, double c1x, double c1y)>();
                var prims = new StringBuilder();
                bool allLines = contour.Segs.All(s => !s.IsCubic);

                (double x, double y) start = ToLb(contour.StartX, contour.StartY);
                verts.Add((start.x, start.y, false, 0, 0, false, 0, 0));

                for (int i = 0; i < contour.Segs.Count; i++)
                {
                    var seg = contour.Segs[i];
                    var end = ToLb(seg.X, seg.Y);
                    int i0 = verts.Count - 1;
                    if (seg.IsCubic)
                    {
                        var c0 = ToLb(seg.C0X, seg.C0Y);
                        var c1 = ToLb(seg.C1X, seg.C1Y);
                        // c0 — исходящая точка начальной вершины
                        var v0 = verts[i0];
                        verts[i0] = (v0.x, v0.y, true, c0.x, c0.y, v0.hasC1, v0.c1x, v0.c1y);
                        verts.Add((end.x, end.y, false, 0, 0, true, c1.x, c1.y));
                        prims.Append($"B{i0} {verts.Count - 1}");
                    }
                    else
                    {
                        verts.Add((end.x, end.y, false, 0, 0, false, 0, 0));
                        prims.Append($"L{i0} {verts.Count - 1}");
                    }
                }

                sb.AppendLine($"    <Shape Type=\"Path\" CutIndex=\"{layer}\" VertID=\"{vertIdCounter}\">");
                sb.AppendLine("        <XForm>1 0 0 1 0 0</XForm>");

                var vertStr = new StringBuilder();
                foreach (var v in verts)
                {
                    vertStr.Append($"V{F(v.x)} {F(v.y)}");
                    vertStr.Append(v.hasC0 ? $"c0x{F(v.c0x)}c0y{F(v.c0y)}" : "c0x1");
                    vertStr.Append(v.hasC1 ? $"c1x{F(v.c1x)}c1y{F(v.c1y)}" : "c1x1");
                }
                sb.AppendLine($"        <VertList>{vertStr}</VertList>");

                string primText;
                if (contour.Closed && allLines)
                {
                    primText = "LineClosed";
                }
                else
                {
                    if (contour.Closed)
                    {
                        // замыкаем на первую вершину, если последний сегмент не замкнул сам
                        var last = contour.Segs[^1];
                        bool closedByItself =
                            Math.Abs(last.X - contour.StartX) < 1e-9 &&
                            Math.Abs(last.Y - contour.StartY) < 1e-9;
                        if (!closedByItself)
                            prims.Append($"L{verts.Count - 1} 0");
                    }
                    primText = prims.ToString();
                }
                sb.AppendLine($"        <PrimList>{primText}</PrimList>");
                sb.AppendLine("    </Shape>");
            }
        }

        sb.AppendLine("    <Notes ShowOnLoad=\"0\" Notes=\"Converted from EzCAD2 by DriverSwitcher\"/>");
        sb.AppendLine("</LightBurnProject>");
        return sb.ToString();
    }

    private static void WriteSetting(StringBuilder sb, string name, double value) =>
        sb.AppendLine($"        <{name} Value=\"{F(value)}\"/>");

    private static void WriteSetting(StringBuilder sb, string name, string value) =>
        sb.AppendLine($"        <{name} Value=\"{System.Security.SecurityElement.Escape(value)}\"/>");
}
