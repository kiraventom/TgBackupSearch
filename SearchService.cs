using Microsoft.EntityFrameworkCore;
using Serilog;
using TgBackupSearch.Model;

namespace TgBackupSearch;

public class SearchService(ChannelContext context)
{
    public const int MinPromptLength = 3;

    public IQueryable<Item> Search(string prompt)
    {
        ArgumentNullException.ThrowIfNull(prompt);

        prompt = prompt.Trim().ToLowerInvariant();
        if (prompt.Length < MinPromptLength)
            return Enumerable.Empty<Item>().AsQueryable();

        var posts = context.Recognitions
            .AsNoTracking()
            .Where(r => r.Text.Contains(prompt))
            .OrderByDescending(r => r.Confidence)
            .ThenByDescending(r => r.Media.Item.DT)
            .Select(r => r.Media.Item)
            .Distinct();

        return posts;
    }
}
