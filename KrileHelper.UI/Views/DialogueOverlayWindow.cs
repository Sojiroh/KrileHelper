using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using KrileHelper.UI.Services;
using Sharlayan.Core.Dialogue;

namespace KrileHelper.UI.Views;

/// <summary>A native click-through copy of dialogue text; game geometry stays in physical pixels.</summary>
public sealed class DialogueOverlayWindow : Window
{
    private readonly Border _frame;
    private readonly GameTextBlock _speaker;
    private readonly GameTextBlock _body;
    private readonly Canvas _choices = new();
    private readonly List<(Border Border, GameTextBlock Text)> _answerRows = new();
    private readonly GameAssets _assets;
    private readonly LinuxGameWindow _native;
    private readonly GameTextBlock _question;
    private readonly Border _questionPanel;
    private DialogueSurface _surface;
    private string _lastText = "";
    private string _lastSpeaker = "";
    private Size _lastSize;
    private bool _clickThrough;
    private string _choiceText = "";
    private string _choiceQuestion = "";
    private string[] _choiceAnswers = [];
    private bool _choiceValid;
    private int _choiceExpectedAnswers;
    private static readonly IBrush MarkedAnswerInk = new SolidColorBrush(Color.Parse("#FFE07A"));

    public DialogueOverlayWindow(GameAssets assets, LinuxGameWindow native)
    {
        _assets = assets;
        _native = native;
        Title = "Krile Dialogue";
        SystemDecorations = SystemDecorations.None;
        TransparencyLevelHint = [WindowTransparencyLevel.Transparent];
        Background = Brushes.Transparent;
        ShowInTaskbar = false;
        ShowActivated = false;
        CanResize = false;
        Topmost = true;
        IsHitTestVisible = false;
        WindowStartupLocation = WindowStartupLocation.Manual;
        _speaker = Text(Brushes.White);
        _speaker.FontWeight = FontWeight.SemiBold;
        _speaker.Margin = new Thickness(26, 4, 24, 0);
        _body = Text(new SolidColorBrush(Color.Parse("#302923")));
        _frame = new Border { Child = _body, CornerRadius = new CornerRadius(10), BorderThickness = new Thickness(3), ClipToBounds = true };
        _question = Text(Brushes.WhiteSmoke);
        _question.TextAlignment = TextAlignment.Center;
        _questionPanel = new Border { Child = _question, Background = new SolidColorBrush(Color.Parse("#F519191E")) };
        _choices.Children.Add(_questionPanel);
        var layout = new Grid();
        layout.Children.Add(_frame);
        layout.Children.Add(_speaker);
        layout.Children.Add(_choices);
        Content = layout;
    }

    private GameTextBlock Text(IBrush ink) => new()
    {
        Assets = _assets, Foreground = ink, TextWrapping = TextWrapping.Wrap,
        VerticalAlignment = VerticalAlignment.Top,
    };

    public bool Present(GameWindowState game, AddonBounds bounds, DialogueSurface surface,
        string speaker, string text, GameChoice? choice)
    {
        if (!bounds.IsKnown || !float.IsFinite(bounds.X) || !float.IsFinite(bounds.Y) ||
            !float.IsFinite(bounds.Width) || !float.IsFinite(bounds.Height) || bounds.Width < 40 || bounds.Height < 20)
        {
            Hide();
            return false;
        }
        var position = new PixelPoint(game.Origin.X + (int)Math.Round(bounds.X), game.Origin.Y + (int)Math.Round(bounds.Y));
        Position = position;
        var scale = Screens.ScreenFromPoint(position)?.Scaling ?? RenderScaling;
        var size = new Size(bounds.Width / scale, bounds.Height / scale);
        Width = size.Width;
        Height = size.Height;

        bool changed = _surface != surface || _lastText != text || _lastSpeaker != speaker || _lastSize != size;
        _surface = surface;
        _lastText = text;
        _lastSpeaker = speaker;
        _lastSize = size;
        bool asking = surface == DialogueSurface.Choice && choice is { IsBeingAsked: true };
        _choices.IsVisible = asking;
        _frame.IsVisible = !asking;
        _speaker.IsVisible = !asking && surface == DialogueSurface.Window && speaker.Length > 0;
        if (asking && choice is not null)
        {
            if (_choiceText != text || _choiceExpectedAnswers != choice!.Answers.Count)
            {
                _choiceText = text;
                _choiceExpectedAnswers = choice.Answers.Count;
                _choiceValid = GameChoice.TryReadBlock(text, choice.Answers.Count, out _choiceQuestion, out _choiceAnswers);
            }
            if (!_choiceValid || choice.AnswerBounds.Count != _choiceAnswers.Length ||
                choice.AnswerBounds.Any(row => !row.IsKnown))
            {
                Hide();
                return false;
            }
            LayoutChoices(game, bounds, choice, _choiceQuestion, _choiceAnswers, scale);
        }
        else if (changed)
        {
            _speaker.GameText = speaker;
            _body.GameText = text;
            bool parchment = surface == DialogueSurface.Window;
            _frame.Background = new SolidColorBrush(Color.Parse(parchment ? "#F0DEC1" : "#F219191E"));
            _frame.BorderBrush = new SolidColorBrush(Color.Parse(parchment ? "#635040" : "#746B55"));
            _body.Foreground = parchment ? new SolidColorBrush(Color.Parse("#302923")) : Brushes.WhiteSmoke;
            _body.TextAlignment = surface == DialogueSurface.Subtitle ? TextAlignment.Center : TextAlignment.Left;
            _frame.Padding = new Thickness(size.Width * .045, parchment ? size.Height * .25 : size.Height * .12,
                size.Width * .045, size.Height * .1);
            _speaker.Foreground = new SolidColorBrush(Color.Parse("#302923"));
            _speaker.FontSize = Math.Clamp(size.Height * .10, 12, 25);
            _speaker.Margin = new Thickness(size.Width * .045, size.Height * .075, 20, 0);
            Fit(_body, size.Width - _frame.Padding.Left - _frame.Padding.Right - 6,
                size.Height - _frame.Padding.Top - _frame.Padding.Bottom - 6, Math.Clamp(size.Height / 6, 14, 30));
        }

        // Avalonia allocates the platform window before its first Show. Do not expose a window unless
        // its native input region is empty; IsHitTestVisible alone does not pass clicks to another app.
        if (!_clickThrough)
        {
            var handle = TryGetPlatformHandle();
            if (handle is null || handle.HandleDescriptor != "XID" || !_native.MakeClickThrough(handle.Handle))
            {
                Hide();
                return false;
            }
            _clickThrough = true;
        }
        if (!IsVisible)
        {
            // Withdrawing an X11 window lets the WM discard _NET_WM_STATE_ABOVE.
            // Reapply it after mapping so a pending withdrawal cannot erase a
            // property written before the WM starts managing the window again.
            Show();
            Topmost = false;
            Topmost = true;
        }
        return true;
    }

    private void LayoutChoices(GameWindowState game, AddonBounds bounds, GameChoice choice,
        string question, string[] answers, double scale)
    {
        while (_answerRows.Count < answers.Length)
        {
            var label = Text(Brushes.WhiteSmoke);
            var panel = new Border { Child = label, Background = new SolidColorBrush(Color.Parse("#F519191E")) };
            _answerRows.Add((panel, label));
            _choices.Children.Add(panel);
        }
        bool questionChanged = _question.GameText != question ||
            _questionPanel.Width != choice.QuestionBounds.Width / scale || _questionPanel.Height != choice.QuestionBounds.Height / scale;
        _question.GameText = question;
        Place(_questionPanel, choice.QuestionBounds, bounds, scale);
        _questionPanel.IsVisible = question.Length > 0 && choice.QuestionBounds.IsKnown;
        if (_questionPanel.IsVisible && questionChanged)
            Fit(_question, _questionPanel.Width, _questionPanel.Height, Math.Clamp(_questionPanel.Height * .6, 12, 28));
        for (int i = 0; i < _answerRows.Count; i++)
        {
            var (panel, label) = _answerRows[i];
            panel.IsVisible = i < answers.Length;
            if (!panel.IsVisible) continue;
            var row = choice.AnswerBounds[i];
            bool hovered = game.Cursor is { } cursor && cursor.X >= game.Origin.X + row.X &&
                cursor.X < game.Origin.X + row.X + row.Width && cursor.Y >= game.Origin.Y + row.Y &&
                cursor.Y < game.Origin.Y + row.Y + row.Height;
            label.Foreground = hovered ? MarkedAnswerInk : Brushes.WhiteSmoke;
            bool rowChanged = label.GameText != answers[i] || panel.Width != row.Width / scale || panel.Height != row.Height / scale;
            label.GameText = answers[i];
            Place(panel, row, bounds, scale);
            if (rowChanged) Fit(label, panel.Width, panel.Height, Math.Clamp(panel.Height * .65, 12, 28));
        }
    }

    private static void Place(Control control, AddonBounds row, AddonBounds outer, double scale)
    {
        if (!row.IsKnown) return;
        Canvas.SetLeft(control, (row.X - outer.X) / scale);
        Canvas.SetTop(control, (row.Y - outer.Y) / scale);
        control.Width = row.Width / scale;
        control.Height = row.Height / scale;
    }

    private static void Fit(GameTextBlock label, double width, double height, double desired)
    {
        if (width <= 0 || height <= 0) return;
        label.FontSize = desired;
        label.Measure(new Size(width, double.PositiveInfinity));
        while (label.DesiredSize.Height > height && label.FontSize > 8)
        {
            label.FontSize = Math.Max(8, label.FontSize - 1);
            label.Measure(new Size(width, double.PositiveInfinity));
        }
    }
}
