namespace GameNest.App.ViewModels;

public sealed record NavigationItemViewModel(
    string Label,
    string Glyph,
    string PageTitle,
    string PageDescription,
    string EmptyTitle,
    string EmptyDescription,
    string EmptyGlyph);
