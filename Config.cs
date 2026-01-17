using System.Text.Json;
using System.Text.Json.Serialization;
using Serilog;

namespace TgBackupSearch;

[method: JsonConstructor]
public class Config(string databasePath, string channelDir, string discussionGroupDir = null)
{
    public string DatabasePath { get; } = databasePath;

    public string ChannelDir { get; } = channelDir;
    public string DiscussionGroupDir { get; } = discussionGroupDir;

    public static bool TryLoad(string path, out Config config)
    {
        config = null;

        if (!File.Exists(path))
            return false;

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

