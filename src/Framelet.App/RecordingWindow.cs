using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Threading;
using Framelet.Core;
using Framelet.Media;
using Framelet.Recording;

namespace Framelet.App;

public sealed class RecordingWindow : Window
{
    private readonly RecordingRequest request;
    private readonly RecordingSessionController session;
    private readonly TextBlock status = Ui.Text("Ready to record");
    private readonly TextBlock stateLabel = Ui.Text("Ready", 14);
    private readonly TextBlock timerText = Ui.Text("00:00", 23);
    private readonly TextBox recoveryPath = new() { IsReadOnly = true, TextWrapping = TextWrapping.Wrap, MaxHeight = 66, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
    private readonly StackPanel recoveryDetails = new() { Margin = new Thickness(0, 0, 0, 10), Visibility = Visibility.Collapsed };
    private readonly Button start, pause, stop, restart, cancel, recover;
    private readonly DispatcherTimer timer = new() { Interval = TimeSpan.FromMilliseconds(200) };
    private bool finished, restarting, closeWhenCancelled;
    public event Action<string>? ClipSaved;

    public RecordingWindow(RecordingRequest request)
    {
        SetResourceReference(StyleProperty, typeof(Window));
        this.request = request;
        session = new RecordingSessionController(request,
            () => RecordingBackendSelector.Create(request.Settings, () => new NativeRecordingBackend(), () => new FfmpegRecordingBackend()));
        session.Changed += _ => Dispatcher.InvokeAsync(Refresh);
        session.ClipSaved += path => Dispatcher.InvokeAsync(() =>
        {
            if (finished) return;
            finished = true;
            ClipSaved?.Invoke(path);
            Close();
        });
        Title = "Framelet recording"; Width = 650; Height = 330; MinWidth = 520; MinHeight = 300;
        ResizeMode = ResizeMode.CanResizeWithGrip; Topmost = true; WindowStartupLocation = WindowStartupLocation.CenterScreen;
        var layout = new Grid { Margin = new Thickness(18, 14, 18, 14) };
        layout.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        layout.RowDefinitions.Add(new RowDefinition());
        layout.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        var panel = new StackPanel();
        var title = new DockPanel { Margin = new Thickness(0, 0, 0, 7) };
        timerText.MinWidth = 104; timerText.TextAlignment = TextAlignment.Right; timerText.TextWrapping = TextWrapping.NoWrap;
        AutomationProperties.SetName(timerText, "Elapsed recording time");
        DockPanel.SetDock(timerText, Dock.Right); title.Children.Add(timerText);
        stateLabel.FontWeight = FontWeights.SemiBold;
        AutomationProperties.SetName(stateLabel, "Recording state"); AutomationProperties.SetLiveSetting(stateLabel, AutomationLiveSetting.Polite);
        title.Children.Add(stateLabel); panel.Children.Add(title);
        var target = Ui.Text(request.Target.Kind + " · " + request.Target.Bounds.Width + " × " + request.Target.Bounds.Height +
            (string.IsNullOrWhiteSpace(request.Target.Title) ? "" : " · " + request.Target.Title), 15);
        target.TextWrapping = TextWrapping.NoWrap; target.TextTrimming = TextTrimming.CharacterEllipsis; target.ToolTip = target.Text; panel.Children.Add(target);
        var engine = request.Settings.UseFfmpeg ? "FFmpeg · " + request.Settings.FfmpegVideoCodec : "Native · H.264 / AAC";
        var note = Ui.Text(engine + " · " + request.Settings.FrameRate + " FPS", 12, true); note.Margin = new Thickness(0, 5, 0, 10); panel.Children.Add(note);
        if (!request.Settings.UseFfmpeg)
        {
            var audio = new WrapPanel { Margin = new Thickness(0, 0, 0, 8) };
            var system = Ui.Check(request.Settings, nameof(RecordingSettings.SystemAudio), "System audio"); system.Margin = new Thickness(0, 0, 22, 0);
            var mic = Ui.Check(request.Settings, nameof(RecordingSettings.Microphone), "Microphone"); mic.Margin = new Thickness(0);
            audio.Children.Add(system); audio.Children.Add(mic); panel.Children.Add(audio); nativeAudio = audio;
        }
        else panel.Children.Add(Ui.Text(string.IsNullOrWhiteSpace(request.Settings.FfmpegAudioDevice) ? "Audio off · choose a DirectShow input in Recording settings" : "Audio · " + request.Settings.FfmpegAudioDevice, 12, true));
        layout.Children.Add(panel);
        var buttons = new WrapPanel { Margin = new Thickness(0, 12, 0, 0) };
        start = Ui.Button("Start", async () => await Start()); pause = Ui.Button("Pause", async () => await Pause()); stop = Ui.Button("Stop & save", async () => await Stop());
        restart = Ui.Button("Restart take", async () => await Restart()); cancel = Ui.Button("Close", async () => await Cancel());
        recover = Ui.Button("Save recording as…", async () => await SaveRecovery());
        foreach (var primary in new[] { start, stop, recover }) primary.SetResourceReference(StyleProperty, "PrimaryButton");
        restart.ToolTip = "Discard the current take and start again";
        stop.ToolTip = "Finish the recording and save the completed clip";
        foreach (var button in new[] { start, pause, stop, restart, recover, cancel })
        {
            button.Margin = new Thickness(0, 0, 8, 5); buttons.Children.Add(button);
        }
        Grid.SetRow(buttons, 2); layout.Children.Add(buttons);
        status.TextWrapping = TextWrapping.Wrap;
        AutomationProperties.SetLiveSetting(status, AutomationLiveSetting.Polite);
        var statusMenu = new ContextMenu();
        var copyStatus = new MenuItem { Header = "Copy details" };
        copyStatus.Click += (_, _) => System.Windows.Clipboard.SetText(status.Text);
        statusMenu.Items.Add(copyStatus); status.ContextMenu = statusMenu;
        var details = new StackPanel();
        var recoveryHeading = new DockPanel { Margin = new Thickness(0, 0, 0, 6) };
        var copyPath = Ui.Button("Copy path", () => System.Windows.Clipboard.SetText(recoveryPath.Text)); copyPath.Margin = new Thickness(8, 0, 0, 0);
        DockPanel.SetDock(copyPath, Dock.Right); recoveryHeading.Children.Add(copyPath); recoveryHeading.Children.Add(Ui.Text("Completed recording", 12, true));
        recoveryDetails.Children.Add(recoveryHeading); recoveryDetails.Children.Add(recoveryPath);
        AutomationProperties.SetName(recoveryPath, "Completed recording recovery path");
        var recoveryNote = Ui.Text("Save a copy to another location, or close this window and keep the completed file here.", 12, true);
        recoveryNote.Margin = new Thickness(0, 6, 0, 0); recoveryDetails.Children.Add(recoveryNote); details.Children.Add(recoveryDetails);
        details.Children.Add(status);
        var detailsScroll = new ScrollViewer { Content = details, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
        Grid.SetRow(detailsScroll, 1); layout.Children.Add(detailsScroll); Content = layout;
        timer.Tick += (_, _) => UpdateTimer(); timer.Start();
        SourceInitialized += (_, _) => SetWindowDisplayAffinity(new WindowInteropHelper(this).Handle, 0x11);
        Closing += OnClosing; Closed += (_, _) => timer.Stop();
        PreviewKeyDown += (_, e) =>
        {
            if (e.Key != Key.Escape || session.Snapshot.IsBusy || session.Snapshot.IsRecording) return;
            e.Handled = true; _ = Cancel();
        };
        Refresh();
    }

    private async Task Start()
    {
        if (finished) return;
        await session.StartAsync();
        Refresh();
    }

    private async Task Pause()
    {
        await session.TogglePauseAsync();
        Refresh();
    }

    public async Task Stop()
    {
        await session.StopAsync();
        Refresh();
    }

    private async Task Restart()
    {
        if (session.Snapshot.IsBusy || !session.Snapshot.IsRecording) return;
        if (MessageBox.Show(this, "Discard this take and begin again?", "Restart recording", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
        restarting = true;
        closeWhenCancelled = false;
        Refresh();
        try
        {
            await session.CancelAsync();
            if (!finished && session.Snapshot.State == RecordingSessionState.Cancelled) await Start();
        }
        finally { restarting = false; Refresh(); }
    }

    private async Task Cancel()
    {
        var snapshot = session.Snapshot;
        if (snapshot.State == RecordingSessionState.Recoverable && !snapshot.IsBusy)
        {
            finished = true;
            Close();
            return;
        }
        if (snapshot.State == RecordingSessionState.Starting)
        {
            closeWhenCancelled = true;
            session.CancelStarting();
            return;
        }
        if (snapshot.IsBusy || restarting) return;
        if (snapshot.IsRecording && MessageBox.Show(this, "Discard this recording?", "Cancel recording", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
        closeWhenCancelled = true;
        await session.CancelAsync();
        Refresh();
    }

    private void Refresh()
    {
        if (finished) return;
        var snapshot = session.Snapshot;
        status.Text = snapshot.Status;
        stateLabel.Text = snapshot.State switch
        {
            RecordingSessionState.Starting => "Starting…",
            RecordingSessionState.Finishing => "Finishing…",
            RecordingSessionState.Recoverable => "Completed · save needs attention",
            RecordingSessionState.Failed => "Recording failed",
            _ => snapshot.State.ToString()
        };
        stateLabel.SetResourceReference(TextBlock.ForegroundProperty, snapshot.State == RecordingSessionState.Failed ? "DangerBrush" :
            snapshot.IsRecording || snapshot.State == RecordingSessionState.Recoverable ? "AccentBrush" : "TextBrush");
        if (closeWhenCancelled && snapshot.State == RecordingSessionState.Cancelled && !snapshot.IsBusy)
        {
            finished = true;
            Close();
            return;
        }
        var busy = snapshot.IsBusy || restarting;
        if (nativeAudio is not null) nativeAudio.IsEnabled = !busy && snapshot.State is RecordingSessionState.Ready or RecordingSessionState.Failed or RecordingSessionState.Cancelled;
        var recovery = snapshot.State == RecordingSessionState.Recoverable;
        if (nativeAudio is not null) nativeAudio.Visibility = recovery ? Visibility.Collapsed : Visibility.Visible;
        recoveryDetails.Visibility = recovery ? Visibility.Visible : Visibility.Collapsed;
        recoveryPath.Text = snapshot.RecoveryPath ?? "";
        start.Visibility = pause.Visibility = stop.Visibility = restart.Visibility = recovery ? Visibility.Collapsed : Visibility.Visible;
        recover.Visibility = recovery ? Visibility.Visible : Visibility.Collapsed;
        recover.IsEnabled = recovery && !busy;
        cancel.Content = snapshot.State == RecordingSessionState.Starting ? "Cancel startup" : snapshot.IsRecording ? "Discard" : "Close";
        cancel.ToolTip = recovery ? "Close and keep the completed recording at its recovery path" : snapshot.IsRecording ? "Discard this take without saving" : "Close recording";
        cancel.SetResourceReference(StyleProperty, snapshot.IsRecording ? "DangerButton" : typeof(Button));
        start.IsEnabled = !busy && !snapshot.IsRecording && snapshot.State is not (RecordingSessionState.Completed or RecordingSessionState.Recoverable);
        pause.IsEnabled = !busy && snapshot.IsRecording && snapshot.CanPause;
        pause.Content = snapshot.State == RecordingSessionState.Paused ? "Resume" : "Pause";
        pause.ToolTip = request.Settings.UseFfmpeg ? "Pause is available with native recording." : "Pause without ending the clip";
        stop.IsEnabled = !busy && snapshot.IsRecording;
        restart.IsEnabled = !busy && snapshot.IsRecording;
        cancel.IsEnabled = !busy || snapshot.State == RecordingSessionState.Starting;
        UpdateTimer();
    }

    private async Task SaveRecovery()
    {
        var dialog = new Microsoft.Win32.SaveFileDialog
        {
            Title = "Save completed recording", Filter = "MP4 video|*.mp4", DefaultExt = ".mp4",
            FileName = System.IO.Path.GetFileNameWithoutExtension(request.OutputPath) + "-recovered.mp4",
            OverwritePrompt = false
        };
        if (dialog.ShowDialog(this) == true) await session.SaveRecoveryAsync(dialog.FileName);
        Refresh();
    }
    private void UpdateTimer()
    {
        var elapsed = session.Snapshot.Elapsed;
        timerText.Text = elapsed.ToString(elapsed.TotalHours >= 1 ? @"h\:mm\:ss" : @"mm\:ss");
    }
    private readonly Panel? nativeAudio;
    private void OnClosing(object? sender, CancelEventArgs e)
    {
        if (finished) return;
        var snapshot = session.Snapshot;
        if (snapshot.IsBusy || snapshot.IsRecording)
        {
            e.Cancel = true;
            if (snapshot.State == RecordingSessionState.Starting)
            {
                closeWhenCancelled = true;
                session.CancelStarting();
            }
            else if (!snapshot.IsBusy) _ = Stop();
        }
    }
    [DllImport("user32.dll")] private static extern bool SetWindowDisplayAffinity(nint window, uint affinity);
}
