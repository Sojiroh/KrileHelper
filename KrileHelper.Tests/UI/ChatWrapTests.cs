using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using KrileHelper.UI;
using KrileHelper.UI.Models;
using KrileHelper.UI.Services;
using KrileHelper.UI.Views;
using KrileHelper.UI.ViewModels;
using Sharlayan.Core.ChatLog;

// Regression tests for the chat overlay layout contract: translated chat text
// must always wrap within the chat viewport. A user report showed the newest
// translation rendered on a single line and clipped at the window edge once
// message history existed; the fix pins the ItemsControl width to the
// ScrollViewer viewport so a bogus-wide layout slot gets clamped and re-wrapped.
[assembly: AvaloniaTestApplication(typeof(KrileHelper.Tests.UI.ChatLayoutTestApp))]

namespace KrileHelper.Tests.UI;

public static class ChatLayoutTestApp
{
    public static Avalonia.AppBuilder BuildAvaloniaApp() => Avalonia.AppBuilder.Configure<App>()
        .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = true });
}

public sealed class ChatWrapTests
{
    private static readonly string[] Codes = { "0048", "0039", "003D" };

    [AvaloniaFact]
    public void ChatTextNeverRendersWiderThanViewport()
    {
        using var harness = new ChatWindowHarness(width: 802, height: 435);

        // Build history (vertical scrollbar appears), translations included.
        for (int i = 0; i < 30; i++)
        {
            harness.AddLine(Codes[i % Codes.Length],
                "Of the 144 parties currently recruiting, all match your search conditions.",
                "De los 144 partidos que actualmente reclutan, todos coinciden con sus condiciones de búsqueda.");
        }

        // The long NPC line from the bug report, with its even longer translation.
        harness.AddLine("003D",
            "Oiika Tsunjika:Welcome back, Pumpkina! Ready for a nap?",
            "Oiika Tsuniika: ¡Bienvenida de nuevo. Calabaza! ¿Listo para una siesta?");

        // Resize like a user dragging the grip, then settle back.
        harness.Resize(950, 500);
        harness.Resize(802, 435);
        harness.AddLine("003D",
            "Oiika Tsunjika:Welcome back, Pumpkina! Ready for a nap?",
            "Oiika Tsuniika: ¡Bienvenida de nuevo. Calabaza! ¿Listo para una siesta?");

        harness.AssertAllTextWithinViewport();
    }

    [AvaloniaFact]
    public void ChatContentWidthIsPinnedToViewportAndTracksResize()
    {
        using var harness = new ChatWindowHarness(width: 802, height: 435);
        harness.AddLine("0039",
            "Updating online status. No longer away from keyboard.",
            "Actualización del estado en línea. Ya no estás lejos del teclado.");

        Assert.Equal(harness.ChatScroll.Viewport.Width, harness.ChatItems.MaxWidth, 0.5);

        harness.Resize(1100, 700);
        Assert.Equal(harness.ChatScroll.Viewport.Width, harness.ChatItems.MaxWidth, 0.5);
    }

    private sealed class ChatWindowHarness : IDisposable
    {
        private readonly MainWindow _window;
        private readonly MainWindowViewModel _vm;
        private readonly ChatCodeRegistry _registry;

        public ScrollViewer ChatScroll { get; }
        public ItemsControl ChatItems { get; }

        public ChatWindowHarness(double width, double height)
        {
            var settingsPath = Path.Combine(Path.GetTempPath(), $"krile-tests-{Guid.NewGuid():N}", "settings.json");
            var settings = new SettingsService(settingsPath);
            _registry = ChatCodeRegistry.LoadDefault();
            _vm = new MainWindowViewModel(settings, _registry);
            _window = new MainWindow { DataContext = _vm, Width = width, Height = height };
            _window.Show();
            ChatScroll = _window.FindControl<ScrollViewer>("ChatScroll")!;
            ChatItems = _window.GetVisualDescendants().OfType<ItemsControl>().First();
            Layout();
        }

        public void AddLine(string code, string text, string translation)
        {
            var info = _registry.Lookup(code)!;
            var display = ChatLineDisplay.Create(
                new ChatLogItem { TimeStamp = DateTime.Now, Code = code, Line = text }, info);
            display.Translation = translation;
            display.TranslationPending = false;
            _vm.Lines.Add(display);
            ChatScroll.ScrollToEnd();
            Layout();
        }

        public void Resize(double width, double height)
        {
            _window.Width = width;
            _window.Height = height;
            Layout();
        }

        public void AssertAllTextWithinViewport()
        {
            Assert.True(ChatScroll.Extent.Width <= ChatScroll.Viewport.Width + 0.5,
                $"horizontal overflow: extent {ChatScroll.Extent.Width} > viewport {ChatScroll.Viewport.Width}");

            var items = _window.GetVisualDescendants().OfType<ContentPresenter>()
                .Where(cp => cp.DataContext is ChatLineDisplay);
            foreach (var item in items)
            {
                foreach (var tb in item.GetVisualDescendants().OfType<TextBlock>())
                {
                    // Width = inked glyph extent; trailing-whitespace overhang on wrapped
                    // lines is not visible and would false-positive here.
                    var renderedWidth = tb.TextLayout.Width;
                    Assert.True(renderedWidth <= tb.Bounds.Width + 1.0,
                        $"text renders {renderedWidth:F0}px wide but its slot is {tb.Bounds.Width:F0}px: \"{Trim(tb.Text)}\"");
                }
            }
        }

        private void Layout()
        {
            Dispatcher.UIThread.RunJobs();
            _window.UpdateLayout();
            Dispatcher.UIThread.RunJobs();
            _window.UpdateLayout();
        }

        private static string Trim(string? s) =>
            s is null ? "" : s.Length > 50 ? s[..50] + "…" : s;

        public void Dispose()
        {
            _window.Close();
            _vm.Dispose();
        }
    }
}
