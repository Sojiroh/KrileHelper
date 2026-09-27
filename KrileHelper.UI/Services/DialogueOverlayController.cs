using Avalonia.Controls;
using KrileHelper.UI.ViewModels;
using KrileHelper.UI.Views;
using Sharlayan.Core.Dialogue;

namespace KrileHelper.UI.Services;

public sealed class DialogueOverlayController : IDisposable
{
    private readonly MainWindowViewModel _viewModel;
    private readonly Window _chatWindow;
    private readonly LinuxGameWindow _game = new();
    private DialogueOverlayWindow? _window;

    public DialogueOverlayController(MainWindowViewModel viewModel, Window chatWindow)
    {
        _viewModel = viewModel;
        _chatWindow = chatWindow;
        _viewModel.DialogueUpdated += Update;
    }

    private void Update()
    {
        var snapshot = _viewModel.CurrentDialogue;
        var settings = _viewModel.Settings.Current;
        if (!settings.Dialogue.Enabled || snapshot is null)
        {
            _window?.Hide();
            _viewModel.DialogueStatus = settings.Dialogue.Enabled ? "" : "Live dialogue reading is disabled.";
            return;
        }
        if (!snapshot.SourceAvailable)
        {
            _window?.Hide();
            _viewModel.DialogueStatus = snapshot.Status;
            return;
        }
        _viewModel.DialogueStatus = snapshot.Status;
        var channel = _viewModel.Registry.Lookup(snapshot.Code);
        if (!settings.Dialogue.OverlayEnabled || !_chatWindow.IsVisible || !snapshot.IsVisible ||
            snapshot.Surface is DialogueSurface.None or DialogueSurface.Bubble || channel is null ||
            _viewModel.Settings.GetChannel(channel) is not { Show: true, Translate: true })
        {
            _window?.Hide();
            return;
        }
        if (!_game.TryGet(_viewModel.AttachedPid, out var game) || !game.IsForeground)
        {
            _window?.Hide();
            _viewModel.DialogueStatus = _game.Status;
            return;
        }
        var translation = _viewModel.CurrentDialogueTranslation;
        var bounds = snapshot.Surface == DialogueSurface.Choice && snapshot.ChoiceBounds.IsKnown
            ? snapshot.ChoiceBounds : snapshot.Bounds;
        _window ??= new DialogueOverlayWindow(_viewModel.Assets, _game);
        bool shown = _window.Present(game, bounds, snapshot.Surface,
            translation?.Speaker ?? snapshot.Speaker, translation?.Body ?? snapshot.Text, snapshot.Choice);
        if (!shown) _viewModel.DialogueStatus = "Aligned overlay hidden: " + _game.Status;
    }

    public void Dispose()
    {
        _viewModel.DialogueUpdated -= Update;
        _window?.Close();
        _game.Dispose();
    }
}
