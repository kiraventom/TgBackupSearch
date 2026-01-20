using TgBackupSearch.Model;

namespace TgBackupSearch.IO;

public class ConsoleIO(ChannelInfo channelInfo) : IIOInterface
{
    private const int ITEMS_PER_PAGE = 10;

    private string _currentInput = null;
    private int _currentPage = 0;
    private IReadOnlyCollection<Item> _currentItems = null;

    public Task<ISearchQuery> GetInput()
    {
        if (_currentInput != null)
        {
            var cki = Console.ReadKey(true);
            if (cki.Key == ConsoleKey.Q)
            {
                _currentInput = null;
                _currentPage = 0;
                _currentItems = null;
            }
            else if (cki.Key == ConsoleKey.H)
            {
                if (_currentPage > 0)
                    --_currentPage;

                return Task.FromResult<ISearchQuery>(new SearchQuery(_currentInput, _currentPage * ITEMS_PER_PAGE, ITEMS_PER_PAGE));
            }
            else if (cki.Key == ConsoleKey.L)
            {
                ++_currentPage;
                return Task.FromResult<ISearchQuery>(new SearchQuery(_currentInput, _currentPage * ITEMS_PER_PAGE, ITEMS_PER_PAGE));
            }
        }

        Console.WriteLine("Enter your prompt: ");
        _currentInput = Console.ReadLine();
        return Task.FromResult<ISearchQuery>(new SearchQuery(_currentInput, _currentPage * ITEMS_PER_PAGE, ITEMS_PER_PAGE));
    }

    public Task SetOutput(IReadOnlyCollection<Item> items)
    {
        Console.WriteLine("Press [q] to return to search, use [h] and [l] to navigate pages");
        Console.WriteLine($"Page {_currentPage + 1}");
        Console.WriteLine($"Search results for \"{_currentInput}\":");

        if (items.Count == 0)
        {
            if (_currentPage == 0)
            {
                Console.WriteLine("Nothing found...");
                return Task.CompletedTask;
            }

            --_currentPage;
            items = _currentItems;
        }

        int count = 1;
        foreach (var item in items)
        {
            var link = item.BuildLink(channelInfo);
            Console.Write(count);
            Console.WriteLine($". {link}");
        }

        _currentItems = items;

        return Task.CompletedTask;
    }
}
