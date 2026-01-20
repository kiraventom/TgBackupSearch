namespace TgBackupSearch;

[Flags]
public enum SearchItem
{
    Post = 0b01,
    Comment = 0b10,
    All = 0b11,
}

