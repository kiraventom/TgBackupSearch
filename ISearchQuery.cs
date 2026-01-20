namespace TgBackupSearch;

public interface ISearchQuery
{
    string Prompt { get; }
    int Offset { get; }
    int Count { get; }
}

