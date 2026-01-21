using TgChannelLib;

namespace TgChannelRecognize;

public record AppPaths(string AppDataDir, string AppConfigDir, string TesseractDir) : IAppPaths;

