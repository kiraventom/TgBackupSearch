using Microsoft.EntityFrameworkCore;
using Serilog;
using TgBackupSearch.Model;

namespace TgBackupSearch;

public class SearchService(ILogger logger, ChannelContext context)
{
    public const int MinPromptLength = 3;

    public async Task<IReadOnlyCollection<Item>> GetResults(ISearchQuery query, SearchMode mode = SearchMode.Recognition, SearchItem item = SearchItem.Post)
    {
        var prompt = query.Prompt;

        ArgumentNullException.ThrowIfNull(prompt);

        prompt = prompt.Trim().ToLowerInvariant();
        if (prompt.Length < MinPromptLength)
            return Array.Empty<Item>();

        IQueryable<Item> posts = context.Posts.AsNoTracking();
        IQueryable<Item> comments = context.Comments.AsNoTracking();

        IQueryable<Item> items = item switch
        {
            SearchItem.Post => posts,
            SearchItem.Comment => comments,
            SearchItem.All => posts.Concat(comments),
            _ => throw new NotSupportedException($"Unexpected {nameof(SearchItem)}: {item.ToString()}")
        };

        IQueryable<Item> results = Enumerable.Empty<Item>().AsQueryable();

        if (mode == SearchMode.Text)
        {
            results = items
                .AsNoTracking()
                .Where(p => p.Text.ToLower().Contains(prompt))
                .OrderByDescending(p => p.DT);
        }
        else if (mode == SearchMode.Recognition)
        {
            results = context.Recognitions
                .AsNoTracking()
                .Where(r => r.Text.Contains(prompt))
                .OrderByDescending(r => r.Confidence)
                .ThenByDescending(r => r.Media.Item.DT)
                .Select(r => r.Media.Item)
                .Distinct();
        }
        else
        {
            logger.Warning("Unexpected {name} = {mode}", nameof(SearchMode), mode.ToString());
        }


        var list = await results.Skip(query.Offset).Take(query.Count).ToListAsync();
        return list;
    }
}
