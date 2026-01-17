using System.Text.Json;
using System.Text.Json.Serialization;
using Serilog;

namespace TgBackupSearch;

[method: JsonConstructor]
public class Config()
{
    private static Config Default { get; } = new Config();

    public static bool TryLoad(string path, out Config config)
    {
        config = null;

        if (!File.Exists(path))
        {
            var json = JsonSerializer.Serialize(Default, new JsonSerializerOptions() { WriteIndented = true } );
            File.WriteAllText(path, json);

            Log.Warning("Config file not found. Created default config file at {path}", path);
            return false;
        }

        try
        {
            using var file = File.OpenRead(path);
            config = JsonSerializer.Deserialize<Config>(file, new JsonSerializerOptions() { AllowTrailingCommas = true });
        }
        catch (Exception ex)
        {
            Log.Fatal(ex.ToString());
            return false;
        }

        return true;
    }
}

