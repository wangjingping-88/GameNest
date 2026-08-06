using GameNest.Domain;

namespace GameNest.App.ViewModels;

public sealed record OverlayPositionOption(string Label, OverlayPosition Value)
{
    public override string ToString() => Label;
}
