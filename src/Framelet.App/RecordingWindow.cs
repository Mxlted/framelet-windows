using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
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
    private readonly TextBlock timerText = Ui.Text("00:00", 23);
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
        Title = "Framelet recording"; Width = 660; Height = 290; ResizeMode = ResizeMode.NoResize; Topmost = true; WindowStartupLocation = WindowStartupLocation.CenterScreen;
        var panel = new StackPanel { Margin = new Thickness(20, 15, 20, 15) };
        var title = new DockPanel(); DockPanel.SetDock(timerText, Dock.Right); title.Children.Add(timerText);
        title.Children.Add(Ui.Text(request.Target.Kind + " · " + request.Target.Bounds.Width + " × " + request.Target.Bounds.Height, 17)); panel.Children.Add(title);
        var engine = request.Settings.UseFfmpeg ? "FFmpeg · " + request.Settings.FfmpegVideoCodec : "Native · H.264 / AAC";
        var note = Ui.Text(engine + " · " + request.Settings.FrameRate + " FPS", 12, true); note.Margin = new Thickness(0, 7, 0, 10); panel.Children.Add(note);
        if (!request.Settings.UseFfmpeg)
        {
            var audio = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 5) };
            var system = Ui.Check(request.Settings, nameof(RecordingSettings.SystemAudio), "System audio"); system.Margin = new Thickness(0, 0, 22, 0);
            var mic = Ui.Check(request.Settings, nameof(RecordingSettings.Microphone), "Microphone"); mic.Margin = new Thickness(0);
            audio.Children.Add(system); audio.Children.Add(mic); panel.Children.Add(audio); nativeAudio = audio;
        }
        else panel.Children.Add(Ui.Text(string.IsNullOrWhiteSpace(request.Settings.FfmpegAudioDevice) ? "Audio off · choose a DirectShow input in Recording settings" : "Audio · " + request.Settings.FfmpegAudioDevice, 12, true));
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 2, 0, 10) };
        start = Ui.Button("● Start", async () => await Start()); pause = Ui.Button("Pause", async () => await Pause()); stop = Ui.Button("Stop & save", async () => await Stop());
        restart = Ui.Button("Restart", async () => await Restart()); cancel = Ui.Button("Cancel", async () => await Cancel());
        recover = Ui.Button("Save recording as…", async () => await SaveRecovery());
        foreach (var button in new[] { start, pause, stop, restart, recover, cancel }) buttons.Children.Add(button);
        panel.Children.Add(buttons);
        status.TextWrapping = TextWrapping.Wrap;
        var statusMenu = new ContextMenu();
        var copyStatus = new MenuItem { Header = "Copy details" };
        copyStatus.Click += (_, _) => System.Windows.Clipboard.SetText(status.Text);
        statusMenu.Items.Add(copyStatus); status.ContextMenu = statusMenu;
        panel.Children.Add(new ScrollViewer { Content = status, MaxHeight = 65, VerticalScrollBarVisibility = ScrollBarVisibility.Auto });
        Content = panel;
        timer.Tick += (_, _) => UpdateTimer(); timer.Start();
        SourceInitialized += (_, _) => SetWindowDisplayAffinity(new WindowInteropHelper(this).Handle, 0x11);
        Closing += OnClosing; Closed += (_, _) => timer.Stop(); Refresh();
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
        if (closeWhenCancelled && snapshot.State == RecordingSessionState.Cancelled && !snapshot.IsBusy)
        {
            finished = true;
            Close();
            return;
        }
        var busy = snapshot.IsBusy || restarting;
        if (nativeAudio is not null) nativeAudio.IsEnabled = !busy && !snapshot.IsRecording;
        var recovery = snapshot.State == RecordingSessionState.Recoverable;
        start.Visibility = pause.Visibility = stop.Visibility = restart.Visibility = recovery ? Visibility.Collapsed : Visibility.Visible;
        recover.Visibility = recovery ? Visibility.Visible : Visibility.Collapsed;
        recover.IsEnabled = recovery && !busy;
        cancel.Content = recovery ? "Close" : "Cancel";
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
            OverwritePrompt = true
        };
        if (dialog.ShowDialog(this) == true) await session.SaveRecoveryAsync(dialog.FileName);
        Refresh();
    }
    private void UpdateTimer()
    {
        var elapsed = session.Snapshot.Elapsed;
        timerText.Text = elapsed.ToString(elapsed.TotalHours >= 1 ? @"h\:mm\:ss" : @"mm\:ss");
    }
    private readonly StackPanel? nativeAudio;
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
