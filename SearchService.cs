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
                .OrderByDescending(p => p.DT)
                .Skip(query.Offset)
                .Take(query.Count);
        }
        else if (mode == SearchMode.Recognition)
        {
            results = items
                .AsNoTracking()
                .Where(i => i.Media.Any(m => m.Recognitions.Any(r => r.Text.Contains(prompt))))
                .Select(i => new
                {
                    Item = i,
                    Best = i.Media.SelectMany(m => m.Recognitions)
                        .Where(r => r.Text.Contains(prompt))
                        .OrderByDescending(r => r.Confidence)
                        .Select(r => r.Confidence)
                        .First()
                })
                .OrderByDescending(x => x.Best)
                .ThenByDescending(x => x.Item.DT)
                .Select(x => x.Item)
                .Skip(query.Offset)
                .Take(query.Count);
        }
        else
        {
            logger.Warning("Unexpected {name} = {mode}", nameof(SearchMode), mode.ToString());
        }


        var list = await results.ToListAsync();
        return list;
    }
}
