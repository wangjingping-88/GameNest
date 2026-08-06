namespace GameNest.Application;

public sealed record GameLibraryQuery(string? SearchText = null, bool FavoritesOnly = false);
