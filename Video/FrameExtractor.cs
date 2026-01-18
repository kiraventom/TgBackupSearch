using System.Diagnostics;
using System.Globalization;
using Serilog;
using TgBackupSearch.Model;

namespace TgBackupSearch.Video;

public record FfmpegResult(int ExitCode, string Output, string Error);

public class FrameExtractor(ILogger logger)
{
    public async Task<IReadOnlyCollection<string>> ExtractFrames(Media media)
    {
        if (media.Type != MediaType.Document)
        {
            logger.Error("Can't extract frames from media of type {type}", media.Type.ToString());
            return [];
        }

        if (!File.Exists(media.FilePath))
        {
            logger.Error("File {filepath} does not exist", media.FilePath);
            return [];
        }

        if (!(await IsVideo(media.FilePath)))
            return [];

        var frames = await ExtractFrames(media.FilePath);
        return frames;
    }

    private async Task<IReadOnlyCollection<string>> ExtractFrames(string filepath, int frameCount = 10)
    {
        double duration = 0;

        if (frameCount <= 0)
        {
            logger.Error("Can't extract {frameCount} frames", frameCount);
            return [];
        }
        else if (frameCount == 1)
        {
            duration = 0;
        }
        else
        {
            var durationNullable = await GetDurationSeconds(filepath);
            if (durationNullable is null)
                return [];

            duration = durationNullable.Value;
        }

        List<string> frames = [];

        for (int i = 0; i < frameCount; ++i)
        {
            var timestamp = duration * (i / (double)(frameCount - 1));
            var framePath = Path.GetTempFileName();

            var result = await RunFfmpeg($"-hide_banner -y -ss {timestamp} -i \"{filepath}\" -frames:v 1 \"{framePath}\"");

            if (result.ExitCode != 0)
            {
                logger.Error("Couldn't extract frame at {timestamp}, ffmpeg returned {exitCode}", timestamp, result.ExitCode);
                continue;
            }

            frames.Add(framePath);
        }

        return frames;
    }

    private async Task<double?> GetDurationSeconds(string filepath)
    {
        var result = await RunFfprobe($"-v error -show_entries format=duration -of default=nw=1:nk=1 \"{filepath}\"");
        
        if (result.ExitCode != 0)
        {
            logger.Error("Can't get duration: ffprobe returned {exitCode}", result.ExitCode);
            return null;
        }

        var durationStr = result.Output.Trim();
        if (double.TryParse(durationStr, NumberStyles.Float, CultureInfo.InvariantCulture, out var duration) && duration > 0)
            return duration;

        logger.Error("Can't parse duration as double: \"{duration}\"", durationStr);
        return null;
    }

    private async Task<bool> IsVideo(string filepath)
    {
        var result = await RunFfprobe($"-v error -show_entries stream=codec_type -of default=nw=1 \"{filepath}\"");

        if (result.ExitCode != 0)
        {
            logger.Error("Can't check if media is video: ffprobe returned {exitCode}", result.ExitCode);
            return false;
        }

        var isVideo = result.Output
            .Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Any(l => l.Equals("codec_type=video", StringComparison.OrdinalIgnoreCase));

        return isVideo;
    }

    private static Task<FfmpegResult> RunFfmpeg(string arguments) => RunTool("ffmpeg", arguments);

    private static Task<FfmpegResult> RunFfprobe(string arguments) => RunTool("ffprobe", arguments);

    private static async Task<FfmpegResult> RunTool(string toolName, string arguments)
    {
        var psi = new ProcessStartInfo
        {
            FileName = toolName,
            Arguments = arguments,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };

        using var p = new Process { StartInfo = psi, EnableRaisingEvents = true };

        try
        {
            if (!p.Start())
                return new FfmpegResult(-1, string.Empty, $"Failed to start process: {toolName}");
        }
        catch (Exception ex)
        {
            return new FfmpegResult(-1, string.Empty, ex.Message);
        }

        var stdoutTask = p.StandardOutput.ReadToEndAsync();
        var stderrTask = p.StandardError.ReadToEndAsync();

        await p.WaitForExitAsync(CancellationToken.None);

        var stdout = await stdoutTask;
        var stderr = await stderrTask;

        return new FfmpegResult(p.ExitCode, stdout, stderr);
    }
}
