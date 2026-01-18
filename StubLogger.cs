using Serilog;
using Serilog.Events;

namespace TgBackupSearch;

public class StubLogger : ILogger
{
    public void Write(LogEvent logEvent)
    {
    }
}

