using System.Collections.ObjectModel;

namespace GameNest.App.ViewModels;

public sealed class RecentPlayGroupViewModel(string title, IEnumerable<GameCardViewModel> games)
{
    public string Title { get; } = title;

    public ObservableCollection<GameCardViewModel> Games { get; } = new(games);
}
