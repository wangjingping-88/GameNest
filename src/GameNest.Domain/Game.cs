namespace GameNest.Domain;

public sealed record Game
{
    public Game(
        Guid id,
        string title,
        string? description,
        string installRoot,
        GameSourceType sourceType,
        bool isFavorite,
        GameAvailability availability,
        DateTimeOffset dateAddedUtc,
        DateTimeOffset? lastPlayedUtc,
        long totalPlaySeconds,
        LaunchProfile launchProfile,
        GameAsset? icon,
        GameDiscoveryMetadata? discoveryMetadata = null,
        GameAsset? cover = null,
        IEnumerable<GameEditableField>? userEditedFields = null,
        GameMetadataAttribution? metadataAttribution = null)
    {
        if (id == Guid.Empty)
        {
            throw new ArgumentException("游戏 ID 不能为空。", nameof(id));
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(title);
        ArgumentException.ThrowIfNullOrWhiteSpace(installRoot);
        ArgumentNullException.ThrowIfNull(launchProfile);
        ArgumentOutOfRangeException.ThrowIfNegative(totalPlaySeconds);

        if (launchProfile.GameId != id)
        {
            throw new ArgumentException("启动配置必须属于当前游戏。", nameof(launchProfile));
        }

        if (icon is not null && icon.GameId != id)
        {
            throw new ArgumentException("图标资产必须属于当前游戏。", nameof(icon));
        }

        if (cover is not null && (cover.GameId != id || cover.AssetType != GameAssetType.Cover))
        {
            throw new ArgumentException("封面资产必须属于当前游戏且类型为封面。", nameof(cover));
        }

        Id = id;
        Title = title.Trim();
        SortTitle = Title.ToUpperInvariant();
        Description = string.IsNullOrWhiteSpace(description) ? null : description.Trim();
        InstallRoot = installRoot;
        SourceType = sourceType;
        IsFavorite = isFavorite;
        Availability = availability;
        DateAddedUtc = dateAddedUtc;
        LastPlayedUtc = lastPlayedUtc;
        TotalPlaySeconds = totalPlaySeconds;
        LaunchProfile = launchProfile;
        Icon = icon;
        DiscoveryMetadata = discoveryMetadata;
        Cover = cover;
        UserEditedFields = new HashSet<GameEditableField>(userEditedFields ?? []);
        MetadataAttribution = metadataAttribution;
    }

    public Guid Id { get; }

    public string Title { get; }

    public string SortTitle { get; }

    public string? Description { get; }

    public string InstallRoot { get; }

    public GameSourceType SourceType { get; }

    public bool IsFavorite { get; }

    public GameAvailability Availability { get; }

    public DateTimeOffset DateAddedUtc { get; }

    public DateTimeOffset? LastPlayedUtc { get; }

    public long TotalPlaySeconds { get; }

    public LaunchProfile LaunchProfile { get; }

    public GameAsset? Icon { get; }

    public GameDiscoveryMetadata? DiscoveryMetadata { get; }

    public GameAsset? Cover { get; }

    public IReadOnlySet<GameEditableField> UserEditedFields { get; }

    public GameMetadataAttribution? MetadataAttribution { get; }

    public Game WithUserEdits(string title, string? description, string? arguments, string workingDirectory) =>
        new(
            Id,
            title,
            description,
            InstallRoot,
            SourceType,
            IsFavorite,
            Availability,
            DateAddedUtc,
            LastPlayedUtc,
            TotalPlaySeconds,
            new LaunchProfile(
                LaunchProfile.Id,
                Id,
                LaunchProfile.Name,
                LaunchProfile.LaunchKind,
                LaunchProfile.ExecutablePath,
                arguments,
                workingDirectory,
                LaunchProfile.RunAsAdministrator,
                LaunchProfile.IsDefault,
                LaunchProfile.ExpectedProcessNames,
                LaunchProfile.GracefulStopTimeoutSeconds),
            Icon,
            DiscoveryMetadata,
            Cover,
            UserEditedFields.Union(
            [
                GameEditableField.Title,
                GameEditableField.Description,
                GameEditableField.Arguments,
                GameEditableField.WorkingDirectory,
            ]),
            MetadataAttribution);

    public Game WithFavorite(bool isFavorite) =>
        new(
            Id,
            Title,
            Description,
            InstallRoot,
            SourceType,
            isFavorite,
            Availability,
            DateAddedUtc,
            LastPlayedUtc,
            TotalPlaySeconds,
            LaunchProfile,
            Icon,
            DiscoveryMetadata,
            Cover,
            UserEditedFields,
            MetadataAttribution);

    public Game WithLastPlayed(DateTimeOffset lastPlayedUtc) =>
        new(
            Id,
            Title,
            Description,
            InstallRoot,
            SourceType,
            IsFavorite,
            Availability,
            DateAddedUtc,
            lastPlayedUtc,
            TotalPlaySeconds,
            LaunchProfile,
            Icon,
            DiscoveryMetadata,
            Cover,
            UserEditedFields,
            MetadataAttribution);

    public Game WithCompletedSession(DateTimeOffset lastPlayedUtc, long durationSeconds) =>
        new(
            Id,
            Title,
            Description,
            InstallRoot,
            SourceType,
            IsFavorite,
            Availability,
            DateAddedUtc,
            lastPlayedUtc,
            checked(TotalPlaySeconds + durationSeconds),
            LaunchProfile,
            Icon,
            DiscoveryMetadata,
            Cover,
            UserEditedFields,
            MetadataAttribution);

    public Game WithIcon(GameAsset? icon) =>
        new(
            Id,
            Title,
            Description,
            InstallRoot,
            SourceType,
            IsFavorite,
            Availability,
            DateAddedUtc,
            LastPlayedUtc,
            TotalPlaySeconds,
            LaunchProfile,
            icon,
            DiscoveryMetadata,
            Cover,
            UserEditedFields,
            MetadataAttribution);

    public Game WithCover(GameAsset? cover, bool isUserEdited) =>
        new(
            Id,
            Title,
            Description,
            InstallRoot,
            SourceType,
            IsFavorite,
            Availability,
            DateAddedUtc,
            LastPlayedUtc,
            TotalPlaySeconds,
            LaunchProfile,
            Icon,
            DiscoveryMetadata,
            cover,
            isUserEdited
                ? UserEditedFields.Append(GameEditableField.Cover)
                : UserEditedFields,
            MetadataAttribution);

    public Game WithMetadata(
        string? title,
        string? description,
        GameMetadataAttribution attribution) =>
        new(
            Id,
            UserEditedFields.Contains(GameEditableField.Title) || string.IsNullOrWhiteSpace(title)
                ? Title
                : title,
            UserEditedFields.Contains(GameEditableField.Description) || string.IsNullOrWhiteSpace(description)
                ? Description
                : description,
            InstallRoot,
            SourceType,
            IsFavorite,
            Availability,
            DateAddedUtc,
            LastPlayedUtc,
            TotalPlaySeconds,
            LaunchProfile,
            Icon,
            DiscoveryMetadata,
            Cover,
            UserEditedFields,
            attribution);

    public Game WithMetadataSnapshot(
        string title,
        string? description,
        GameMetadataAttribution? attribution) =>
        new(
            Id,
            title,
            description,
            InstallRoot,
            SourceType,
            IsFavorite,
            Availability,
            DateAddedUtc,
            LastPlayedUtc,
            TotalPlaySeconds,
            LaunchProfile,
            Icon,
            DiscoveryMetadata,
            Cover,
            UserEditedFields,
            attribution);

    public Game WithAvailability(GameAvailability availability) =>
        new(
            Id,
            Title,
            Description,
            InstallRoot,
            SourceType,
            IsFavorite,
            availability,
            DateAddedUtc,
            LastPlayedUtc,
            TotalPlaySeconds,
            LaunchProfile,
            Icon,
            DiscoveryMetadata,
            Cover,
            UserEditedFields,
            MetadataAttribution);
}
