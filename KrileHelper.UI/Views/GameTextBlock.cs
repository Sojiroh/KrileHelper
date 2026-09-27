using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Media;
using KrileHelper.UI.Services;
using Sharlayan.Core.Dialogue;

namespace KrileHelper.UI.Views;

public sealed class GameTextBlock : TextBlock
{
    public static readonly StyledProperty<string?> GameTextProperty =
        AvaloniaProperty.Register<GameTextBlock, string?>(nameof(GameText));
    public static readonly StyledProperty<GameAssets?> AssetsProperty =
        AvaloniaProperty.Register<GameTextBlock, GameAssets?>(nameof(Assets));

    public string? GameText { get => GetValue(GameTextProperty); set => SetValue(GameTextProperty, value); }
    public GameAssets? Assets { get => GetValue(AssetsProperty); set => SetValue(AssetsProperty, value); }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == GameTextProperty || change.Property == AssetsProperty || change.Property == FontSizeProperty)
            Rebuild();
    }

    private void Rebuild()
    {
        Inlines ??= new InlineCollection();
        Inlines.Clear();
        var text = GameText ?? "";
        int start = 0;
        for (int at = 0; at < text.Length; at++)
        {
            if (!GameIcons.IsMark(text[at])) continue;
            if (at > start) Inlines.Add(new Run(text[start..at]));
            int id = GameIcons.IdOf(text[at]);
            var icon = Assets?.Icon(id);
            if (icon is not null)
                Inlines.Add(new InlineUIContainer(new Image
                {
                    Source = icon, Width = FontSize * icon.Size.AspectRatio, Height = FontSize,
                }) { BaselineAlignment = BaselineAlignment.Center });
            else
                Inlines.Add(new Run(id == CrossWorldNames.CrossWorldIcon ? " · " : $"[icon {id}]"));
            start = at + 1;
        }
        if (start < text.Length) Inlines.Add(new Run(text[start..]));
    }
}
