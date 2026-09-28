using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Controls.Primitives;
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
// must always wrap within the chat viewport, and the newest content must stay
// reachable/visible when the user is pinned to the bottom. Two fixes are
// covered: (1) the ItemsControl width is pinned to the ScrollViewer viewport so
// a bogus-wide layout slot gets clamped and re-wrapped instead of clipping, and
// (2) padding lives outside the ScrollViewer (otherwise Avalonia's extent falls
// short of the content and the last lines are unreachable) plus a re-pin on
// extent growth so lines/translations arriving after layout keep the newest
// content visible.
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
    public void NewestLineStaysVisibleWhenPinnedToBottom()
    {
        using var harness = new ChatWindowHarness(width: 802, height: 435);

        // History tall enough to scroll.
        for (int i = 0; i < 25; i++)
            harness.AddLine("0039",
                "Updating online status. No longer away from keyboard.",
                "Actualización del estado en línea. Ya no estás lejos del teclado.");

        // The scroll extent must cover the whole content, not fall short of it.
        Assert.True(harness.ChatScroll.Extent.Height >= harness.LastItem.Bounds.Y + harness.LastItem.Bounds.Height - 1.0,
            "scroll extent is shorter than the content — the newest line is unreachable");

        // A new line followed by its translation arriving later must stay visible.
        harness.AddLineDelayed("003D",
            "Oiika Tsunjika:Welcome back, Pumpkina! Ready for a nap?",
            "Oiika Tsuniika: ¡Bienvenida de nuevo, Calabaza! ¿Listo para una siesta?");
        Assert.True(harness.IsLastItemVisible(), "newest line is hidden below the fold after its translation arrived");
    }

    [AvaloniaFact]
    public void ScrollingUpIsNotYankedDownByNewLines()
    {
        using var harness = new ChatWindowHarness(width: 802, height: 435);

        for (int i = 0; i < 25; i++)
            harness.AddLine("0039",
                "Updating online status. No longer away from keyboard.",
                "Actualización del estado en línea. Ya no estás lejos del teclado.");

        harness.ChatScroll.Offset = new Vector(0, 0);
        harness.Layout();

        harness.AddLine("0039",
            "Updating online status. No longer away from keyboard.",
            "Actualización del estado en línea. Ya no estás lejos del teclado.");

        Assert.True(harness.ChatScroll.Offset.Y < 5,
            $"reading history was interrupted: offset {harness.ChatScroll.Offset.Y}");
    }

    [AvaloniaFact]
    public void SwitchingTabsRestoresReadingPositionAndFollowsInactiveTranslations()
    {
        using var harness = new ChatWindowHarness(620, 380,
        [
            new() { Id = "npc", Name = "Story", Channels = ["003D"] },
            new() { Id = "party", Name = "Team", Channels = ["000E"] },
        ]);
        for (int i = 0; i < 30; i++)
        {
            harness.AddLine("003D", $"Cid: Story line {i}, continuing our journey across Eorzea.",
                $"Cid: Línea de historia {i}, continuamos nuestro viaje por Eorzea.");
            harness.AddLine("000E", $"Player: Party message {i}, please wait for the next pull.",
                $"Player: Mensaje de grupo {i}, esperad antes del siguiente combate.");
        }

        harness.ChatScroll.Offset = new Vector(0, 80);
        harness.Layout();
        var readingOffset = harness.ChatScroll.Offset.Y;
        harness.SelectTab("party");
        Assert.True(harness.IsLastItemVisible());
        Assert.All(harness.ChatItems.Items.OfType<ChatLineDisplay>(), line => Assert.Equal("000E", line.Code));

        harness.AddLine("003D", "Cid: An unseen story line.", "Cid: Una línea de historia aún no leída.");
        var pending = harness.AddLineDelayed("000E", "Player: Ready?", "Player: ¿Estamos preparados?");
        harness.SelectTab("npc");
        Assert.Equal(readingOffset, harness.ChatScroll.Offset.Y, 1);
        Assert.All(harness.ChatItems.Items.OfType<ChatLineDisplay>(), line => Assert.Equal("003D", line.Code));

        pending.Translation = string.Join(" ", Enumerable.Repeat("A much longer translation arriving in the inactive tab.", 30));
        harness.Layout();
        Assert.Equal(readingOffset, harness.ChatScroll.Offset.Y, 1);
        harness.SelectTab("party");
        Assert.True(harness.IsLastItemVisible(), "returning to a bottom-pinned tab must include its late translation");
        harness.SelectTab("npc");
        Assert.Equal(readingOffset, harness.ChatScroll.Offset.Y, 1);
    }

    private sealed class ChatWindowHarness : IDisposable
    {
        private readonly MainWindow _window;
        private readonly MainWindowViewModel _vm;
        private readonly ChatCodeRegistry _registry;

        public ScrollViewer ChatScroll { get; }
        public ItemsControl ChatItems { get; }

        public ChatWindowHarness(double width, double height, List<ChatTabSettings>? tabs = null)
        {
            var settingsPath = Path.Combine(Path.GetTempPath(), $"krile-tests-{Guid.NewGuid():N}", "settings.json");
            var settings = new SettingsService(settingsPath);
            if (tabs is not null) settings.Current.ChatTabs = tabs;
            _registry = ChatCodeRegistry.LoadDefault();
            _vm = new MainWindowViewModel(settings, _registry);
            _window = new MainWindow { DataContext = _vm, Width = width, Height = height };
            _window.Show();
            ChatScroll = _window.FindControl<ScrollViewer>("ChatScroll")!;
            ChatItems = _window.FindControl<ItemsControl>("ChatItems")!;
            Layout();
        }

        public void AddLine(string code, string text, string translation)
        {
            var info = _registry.Lookup(code)!;
            var display = ChatLineDisplay.Create(
                new ChatLogItem { TimeStamp = DateTime.Now, Code = code, Line = text }, info);
            display.Translation = translation;
            display.TranslationPending = false;
            _vm.ChatTabs.Add(display);
            Layout();
        }

        // Mimics the real flow: the line appears, and only later (after layout)
        // does its translation land, growing the last item.
        public ChatLineDisplay AddLineDelayed(string code, string text, string translation)
        {
            var info = _registry.Lookup(code)!;
            var display = ChatLineDisplay.Create(
                new ChatLogItem { TimeStamp = DateTime.Now, Code = code, Line = text }, info);
            _vm.ChatTabs.Add(display);
            Layout();
            display.Translation = translation;
            display.TranslationPending = false;
            Layout();
            return display;
        }

        public ContentPresenter LastItem =>
            _window.GetVisualDescendants().OfType<ContentPresenter>()
                .Last(cp => cp.DataContext is ChatLineDisplay);

        public bool IsLastItemVisible()
        {
            var last = LastItem;
            return last.Bounds.Y + last.Bounds.Height - ChatScroll.Offset.Y <= ChatScroll.Viewport.Height + 0.5;
        }

        public void SelectTab(string id)
        {
            _window.FindControl<TabStrip>("ChatTabStrip")!.SelectedItem =
                _vm.ChatTabs.Tabs.Single(tab => tab.Id == id);
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

        public void Layout()
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
