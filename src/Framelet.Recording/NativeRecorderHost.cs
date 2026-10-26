using System.Text.Json;

namespace Framelet.Recording;

/// <summary>Private stdio protocol entry point for the isolated Windows capture worker.</summary>
public static class NativeRecorderHost
{
    private static readonly object OutputLock = new();

    public static async Task<int> RunAsync()
    {
        NativeSession? session = null;
        try
        {
            while (await Console.In.ReadLineAsync().ConfigureAwait(false) is { } line)
            {
                if (line.Length > 65536) throw new InvalidDataException("Recorder command is too large.");
                var command = JsonSerializer.Deserialize<NativeHostCommand>(line)
                    ?? throw new InvalidDataException("Recorder command was empty.");
                try
                {
                    switch (command.Operation)
                    {
                        case "start":
                            if (session is not null) throw new InvalidOperationException("A recorder host handles one session.");
                            var request = command.Request ?? throw new ArgumentException("Recording settings are missing.");
                            if (!Guid.TryParseExact(request.SessionId, "N", out _)) throw new ArgumentException("Invalid session ID.");
                            var staging = Path.Combine(Path.GetDirectoryName(Path.GetFullPath(request.OutputPath))!,
                                $".framelet-{request.SessionId}.recording.mp4");
                            session = new NativeSession(staging);
                            session.StatusChanged += message => Send(new("status", Text: message));
                            session.Failed += error => Send(new("failed", Text: error.Message));
                            session.Completed += () => Send(new("completed"));
                            await session.StartAsync(request.ToRecordingRequest()).ConfigureAwait(false);
                            break;
                        case "pause":
                            await RequireSession(session).PauseAsync().ConfigureAwait(false);
                            break;
                        case "resume":
                            await RequireSession(session).ResumeAsync().ConfigureAwait(false);
                            break;
                        case "stop":
                            await RequireSession(session).StopAsync().ConfigureAwait(false);
                            break;
                        case "cancel":
                            if (session is not null) await session.CancelAsync().ConfigureAwait(false);
                            break;
                        default:
                            throw new ArgumentException("Unknown recorder command.");
                    }
                    Send(new("result", command.Id));
                }
                catch (Exception ex)
                {
                    Send(new("result", command.Id, ex.Message, ex.GetType().Name));
                }
                if (command.Operation is "stop" or "cancel") break;
            }
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(ex.Message);
            return 1;
        }
        finally
        {
            if (session is not null) await session.DisposeAsync().ConfigureAwait(false);
        }
    }

    private static NativeSession RequireSession(NativeSession? session) =>
        session ?? throw new InvalidOperationException("The recording has not started.");

    private static void Send(NativeHostMessage message)
    {
        lock (OutputLock)
        {
            Console.Out.WriteLine(JsonSerializer.Serialize(message));
            Console.Out.Flush();
        }
    }
}
