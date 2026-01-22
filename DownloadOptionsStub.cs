using TgChannelBackup.Core;

namespace TgChannelRecognize;

public class DownloadOptionsStub : IDownloadOptions
{
    public bool DryRun => false;
    public bool Reconcile => false;
}

