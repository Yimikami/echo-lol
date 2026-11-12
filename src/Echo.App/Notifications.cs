using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Automation.Peers;
using System.Windows.Input;
using Echo.Core;

namespace Echo.App;

public partial class MainWindow
{
    private readonly Dictionary<string, string> _observedErrors = new();
    private string? _toastSource;
    private bool _toastPersistent;
    private Action? _toastAction;

    private void ShowToast(string message) => PresentToast(null, message, null, null, null, null);

    private void ShowError(string title, Exception error, string? actionLabel = null, Action? action = null) =>
        PresentToast(title, ErrorMessage(error.Message), error.ToString(), actionLabel, action, null);

    private static string ErrorMessage(string detail)
    {
        if (detail.Contains("denied", StringComparison.OrdinalIgnoreCase) || detail.Contains("being used", StringComparison.OrdinalIgnoreCase))
            return "Echo couldn't access a file. Close apps using the recording and check that its folder allows changes, then try again.";
        var line = detail.Split(['\r', '\n'], 2)[0];
        if (line.StartsWith("System.", StringComparison.Ordinal) && line.IndexOf(": ", StringComparison.Ordinal) is var colon && colon >= 0)
            line = line[(colon + 2)..];
        return line[..Math.Min(320, line.Length)];
    }

    private void ObserveError(string source, string title, string? detail, string? actionLabel = null, Action? action = null)
    {
        if (string.IsNullOrWhiteSpace(detail)) { ClearObservedError(source); return; }
        if (_observedErrors.TryGetValue(source, out var previous) && previous == detail) return;
        _observedErrors[source] = detail;
        PresentToast(title, ErrorMessage(detail), detail, actionLabel, action, source);
    }

    private void ClearObservedError(string source)
    {
        _observedErrors.Remove(source);
        if (_toastSource == source) DismissToastClick(this, new());
    }

    private void PresentToast(string? title, string message, string? detail, string? actionLabel, Action? action, string? source)
    {
        _toastTimer.Stop();
        _toastPersistent = detail != null;
        _toastSource = source;
        _toastAction = action;
        ToastTitle.Text = title ?? "";
        ToastTitle.Visibility = title == null ? Visibility.Collapsed : Visibility.Visible;
        ToastText.Text = message;
        ToastIcon.Kind = "Info";
        ToastIcon.SetResourceReference(ForegroundProperty, _toastPersistent ? "DeathBrush" : "AccentBrush");
        ToastDetails.Text = detail ?? "";
        ToastDetailsPanel.Visibility = Visibility.Collapsed;
        ToastDetailsButton.Content = "Details";
        ToastCopyButton.Content = "Copy details";
        ToastDetailsButton.Visibility = detail == null ? Visibility.Collapsed : Visibility.Visible;
        ToastActionButton.Content = actionLabel ?? "";
        ToastActionButton.IsEnabled = true;
        ToastActionButton.Visibility = action == null ? Visibility.Collapsed : Visibility.Visible;
        ToastActions.Visibility = detail == null && action == null ? Visibility.Collapsed : Visibility.Visible;
        Toast.Visibility = Visibility.Visible;
        PositionNotifications();
        UpdateToastTimer();
        UIElementAutomationPeer.CreatePeerForElement(ToastText)?.RaiseAutomationEvent(AutomationEvents.LiveRegionChanged);
        if (detail != null) LogUiError(title!, detail);
    }

    private void PositionNotifications() => NotificationHost.Margin = new(24, 24, 24,
        EditorPanel.Visibility == Visibility.Visible ? 126 : 24);

    private void UpdateToastTimer()
    {
        _toastTimer.Stop();
        if (!_toastPersistent && Toast.Visibility == Visibility.Visible && !Toast.IsMouseOver && !Toast.IsKeyboardFocusWithin)
            _toastTimer.Start();
    }

    private void ToastInteractionChanged(object sender, MouseEventArgs e) => UpdateToastTimer();
    private void ToastFocusChanged(object sender, DependencyPropertyChangedEventArgs e) => UpdateToastTimer();
    private void DismissToastClick(object sender, RoutedEventArgs e)
    {
        Toast.Visibility = Visibility.Collapsed; _toastTimer.Stop(); _toastAction = null; _toastSource = null;
    }
    private void ToastDetailsClick(object sender, RoutedEventArgs e)
    {
        bool show = ToastDetailsPanel.Visibility != Visibility.Visible;
        ToastDetailsPanel.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
        ToastDetailsButton.Content = show ? "Hide details" : "Details";
    }
    private void ToastCopyClick(object sender, RoutedEventArgs e)
    {
        try { Clipboard.SetText(ToastTitle.Text + Environment.NewLine + ToastDetails.Text); ToastCopyButton.Content = "Copied"; }
        catch (ExternalException) { ToastCopyButton.Content = "Try copying again"; }
    }
    private void ToastActionClick(object sender, RoutedEventArgs e)
    {
        var action = _toastAction;
        if (action == null) return;
        ToastActionButton.IsEnabled = false;
        try { action(); }
        catch (Exception error) { ShowError("Couldn't finish", error); }
        finally { ToastActionButton.IsEnabled = true; }
    }

    private void LogUiError(string title, string detail)
    {
        try
        {
            var path = Path.Combine(_paths.Root, "ui-errors.log");
            if (File.Exists(path) && new FileInfo(path).Length > 256 * 1024) File.Move(path, path + ".previous", true);
            File.AppendAllText(path, $"{DateTimeOffset.Now:O} [{title}] {detail[..Math.Min(detail.Length, 16384)]}{Environment.NewLine}");
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException) { }
    }

    private void ShowPreparationError(Session session, bool explicitRequest = false)
    {
        var source = "preparation:" + session.Id;
        if (explicitRequest) _observedErrors.Remove(source);
        ObserveError(source, "Couldn't prepare recording", session.Error, "Retry preparation", () => RetryPreparation(session));
    }

    private void RetryPreparation(Session session)
    {
        try
        {
            var metadata = Path.Combine(LibraryCatalog.Folder(_settings.OutputDirectory, session.Id), "session.json");
            var queueFile = Path.Combine(_paths.Root, "preparation", session.Id + ".json");
            // Preserve a finalization snapshot if the original folder could not be updated.
            var previous = JsonStore.Read<PreparationJob>(queueFile) ?? JsonStore.Read<PreparationJob>(queueFile + ".failed");
            var job = previous != null && string.Equals(previous.Metadata, metadata, StringComparison.OrdinalIgnoreCase)
                ? previous with { Error = null } : new PreparationJob(metadata);
            JsonStore.Write(queueFile, job);
            ClearObservedError("preparation:" + session.Id);
            RefreshCatalog(true);
            ShowToast("Preparation queued. Echo will retry when League is closed.");
        }
        catch (Exception error) { ShowError("Couldn't retry preparation", error, "Try again", () => RetryPreparation(session)); }
    }
}
