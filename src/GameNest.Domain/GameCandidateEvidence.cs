namespace GameNest.Domain;

public sealed record GameCandidateEvidence(string Code, string Description, int Score)
{
    public string Code { get; } = string.IsNullOrWhiteSpace(Code)
        ? throw new ArgumentException("证据代码不能为空。", nameof(Code))
        : Code.Trim();

    public string Description { get; } = string.IsNullOrWhiteSpace(Description)
        ? throw new ArgumentException("证据说明不能为空。", nameof(Description))
        : Description.Trim();
}
