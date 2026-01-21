using System.Diagnostics;

namespace TgChannelRecognize.Utils;

public static class ProcessHelper
{
    public static Process RunSilent(string filename, string arguments)
    {
        var psi = new ProcessStartInfo()
        {
            FileName = filename,
            Arguments = arguments,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };

        var process = new Process() { StartInfo = psi };
        process.Start();

        return process;
    }
}
