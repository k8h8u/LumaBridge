using System;
using System.IO;
using System.Text.Json;

namespace LumaBridge;

/// <summary>Настройки приложения (хранятся в ProgramData\LumaBridge\settings.json).</summary>
public sealed class AppSettings
{
    public double FieldWidth { get; set; } = 110;
    public double FieldHeight { get; set; } = 110;
    public bool AutoCenter { get; set; } = true;
    public string LastEzdDir { get; set; } = string.Empty;

    private static string FilePath =>
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
            "LumaBridge", "settings.json");

    public static AppSettings Load()
    {
        try
        {
            if (File.Exists(FilePath))
            {
                var s = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(FilePath));
                if (s != null)
                    return s;
            }
        }
        catch
        {
            // повреждённый файл — настройки по умолчанию
        }
        return new AppSettings();
    }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(this));
        }
        catch
        {
            // не критично
        }
    }
}
