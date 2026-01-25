using System.Text.Json;
using System.Text.Json.Serialization;
using Serilog;

namespace TgChannelRecognize;

[method: JsonConstructor]
public class Config(IReadOnlyCollection<string> languages, string tesseractFile = null, string tessdataDir = null)
{
    private static Config Default { get; } = new Config(
        languages: [ "eng", "rus" ],
        tesseractFile: "/usr/bin/tesseract",
        tessdataDir: "/usr/share/tesseract-ocr/5/"
    );

    public IReadOnlyCollection<string> Languages { get; } = languages;

    public string TesseractFile { get; } = tesseractFile;
    public string TessdataDir { get; } = tessdataDir;

    public static bool TryLoad(string path, out Config config)
    {
        config = null;

        if (!File.Exists(path))
        {
            var json = JsonSerializer.Serialize(Default, new JsonSerializerOptions() { WriteIndented = true } );
            File.WriteAllText(path, json);

            Log.Warning("Config file not found. Created default config file at {path}", path);
        }

        try
        {
            using var file = File.OpenRead(path);
            config = JsonSerializer.Deserialize<Config>(file, new JsonSerializerOptions() { AllowTrailingCommas = true });
        }
        catch (Exception ex)
        {
            Log.Error(ex.ToString());
            return false;
        }

        return true;
    }
}

