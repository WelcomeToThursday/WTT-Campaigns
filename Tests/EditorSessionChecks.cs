using System.Net.WebSockets;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using WTT.Campaigns.Client.Authoring;
using WTT.Campaigns.Shared.Authoring;
using WTT.Campaigns.Shared.Seasons;

namespace WTT.Campaigns.Tests;

internal static class EditorSessionChecks
{
    internal static async Task Run(Action<bool, string> check)
    {
        var now = DateTimeOffset.UtcNow;
        var serverSession = new WTT.Campaigns.Server.Editor.EditorSessionRegistry.Session
        {
            Owner = "owner",
            Ready = true,
            Location = "Interchange",
            Contact = now,
        };
        var request = new AuthoringRequest { EditorSessionId = serverSession.Id, Location = "Interchange" };
        foreach (var version in new[] { 2, 3, 4, 5 })
        {
            request.Version = version;
            check(serverSession.AcceptsMapRequest("owner", request, now), "Editor map connection accepts protocol " + version);
        }
        foreach (var version in new[] { 0, 1, 6 })
        {
            request.Version = version;
            check(!serverSession.AcceptsMapRequest("owner", request, now), "Editor map connection rejects protocol " + version);
        }
        request.Version = 5;
        check(!serverSession.AcceptsMapRequest("other", request, now), "Protocol 5 retains the owner check");
        check(!serverSession.AcceptsMapRequest("owner", request, now.AddMinutes(2)), "Protocol 5 retains session expiry");
        request.EditorSessionId = "wrong";
        check(!serverSession.AcceptsMapRequest("owner", request, now), "Protocol 5 retains the session token check");
        request.EditorSessionId = serverSession.Id;
        request.Location = "woods";
        check(!serverSession.AcceptsMapRequest("owner", request, now), "Protocol 5 retains the exact map check");
        serverSession.UnloadMap();
        request.Location = "";
        check(!serverSession.AcceptsMapRequest("owner", request, now), "A matching empty map cannot authorize an editor connection");
        var folder = Path.Combine(Path.GetTempPath(), "campaigns-session-" + Guid.NewGuid().ToString("N"));
        BepInEx.Paths.ConfigPath = folder;
        var socket = new ReplySocket();
        AuthoringSocket.Shared.Attach(socket, socket);
        try
        {
            var baseline = new SeasonDefinition
            {
                Id = "offline",
                Name = "Original",
                FormatVersion = 2,
            };
            for (var i = 0; i < 20; i++)
                baseline.Zones.Add(
                    new()
                    {
                        Id = "zone" + i,
                        Name = "Test zone " + i,
                        Location = "interchange",
                    }
                );
            var session = new RaidEditorSession("interchange") { Hold = true };
            var notifications = 0;
            session.Changed += () => notifications++;
            socket.Reply = _ => Response(baseline);
            await session.Tick();
            check(session.Contacted && !session.Dirty && notifications == 1, "Connecting initializes a clean draft and presents it.");

            socket.Reply = _ => Response();
            var previous = notifications;
            for (var i = 0; i < 20; i++)
                await session.Tick();
            check(notifications == previous, "Unchanged one-second polls do not rebuild editor UI or scene geometry.");
            session.Edit(d => d.Name = "Original");
            check(!session.Dirty && notifications == previous, "No-op edits preserve clean state without rebuilding presentation.");

            // Measure the removed operation against the same draft and exact legacy expression.
            _ = LegacyDirty(session);
            var start = GC.GetAllocatedBytesForCurrentThread();
            for (var i = 0; i < 60; i++)
                _ = LegacyDirty(session);
            var legacy = GC.GetAllocatedBytesForCurrentThread() - start;
            start = GC.GetAllocatedBytesForCurrentThread();
            var dirtyReads = 0;
            for (var i = 0; i < 36000; i++)
                if (session.Dirty)
                    dirtyReads++;
            var current = GC.GetAllocatedBytesForCurrentThread() - start;
            check(current == 0 && dirtyReads == 0, "Ten minutes of idle dirty-state reads at 60 FPS allocate zero bytes.");
            Console.WriteLine(
                $"Editor allocation regression: legacy 60 reads = {legacy:N0} bytes; cached 36,000 reads = {current} bytes (20-zone fixture)."
            );

            session.Edit(d => d.Name = "Changed");
            check(session.Dirty, "An edit marks the committed draft dirty.");
            session.Undo(false);
            check(!session.Dirty && session.Definition!.Name == "Original", "Undoing to the baseline clears dirty state.");
            session.Undo(true);
            check(session.Dirty && session.Definition!.Name == "Changed", "Redo restores dirty state.");
            try
            {
                session.Edit(d =>
                {
                    d.Name = "Partial";
                    throw new InvalidOperationException("rollback");
                });
            }
            catch (InvalidOperationException) { }
            check(session.Dirty && session.Definition!.Name == "Changed", "Failed edits restore content and preserve dirty state.");

            session.Hold = false;
            socket.Reply = message => message.Operation == "submit" ? Response(message.Request.Definition, 2) : Response();
            await session.Tick();
            check(session.Contacted && !session.Dirty && session.Revision == 2, "Acknowledged submissions advance the clean baseline.");
            session.Undo(false);
            check(session.Dirty, "Undo after a successful save differs from the new baseline.");
            session.Undo(true);
            check(!session.Dirty, "Redo back to the saved content is clean.");

            var remote = RaidEditorSession.Copy(session.Definition!);
            remote.Description = "Remote edit";
            socket.Reply = _ => Response(remote, 3);
            await session.Tick();
            check(!session.Dirty && session.Definition!.Description == "Remote edit", "Remote changes update content and clean state.");

            session.Edit(d => d.Name = "Local conflict");
            remote.Name = "Remote conflict";
            socket.Reply = _ => Response(remote, 4);
            await session.Tick();
            check(session.Conflict != null && session.Dirty, "Conflicting edits retain unsynchronized changes.");
            session.Resolve(true);
            check(session.Dirty && session.Definition!.Name == "Local conflict", "Keeping local conflict content remains dirty.");
            session.Conflict = new()
            {
                Definition = remote,
                Candidate = session.Definition,
                RemoteCandidate = remote,
                Revision = 4,
            };
            session.Resolve(false);
            check(!session.Dirty && session.Definition!.Name == "Remote conflict", "Keeping remote conflict content clears dirty state.");

            var task = new CaptureTask { Id = "capture" };
            socket.Reply = _ =>
            {
                var r = Response(null, 4);
                r.Tasks.Add(task);
                return r;
            };
            previous = notifications;
            await session.Tick();
            check(notifications > previous, "New capture tasks still refresh presentation.");
            previous = notifications;
            await session.Tick();
            check(notifications == previous, "Identical deserialized tasks do not trigger another refresh.");
            task.Status = "Completed";
            await session.Tick();
            check(notifications > previous, "Capture task status changes still refresh presentation.");

            var retired = new RaidEditorSession("interchange") { Hold = true };
            var sendGate = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var sendStarted = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            socket.SendGate = sendGate;
            socket.SendStarted = sendStarted;
            socket.Reply = _ => throw new IOException("late retired poll");
            var retiredStatus = retired.Status;
            var retiredTick = retired.Tick();
            await sendStarted.Task;
            retired.Retired = true;
            sendGate.SetResult(true);
            await retiredTick;
            check(!retired.Busy && retired.Status == retiredStatus, "A late retired poll completes quietly without exception spam.");
            socket.SendGate = null;
            socket.SendStarted = null;

            session.Edit(d => d.Name = "Recover me");
            var recovered = new RaidEditorSession("interchange") { Hold = true };
            socket.Reply = _ => Response(remote, 4);
            await recovered.Tick();
            check(recovered.Dirty && recovered.Definition!.Name == "Recover me", "Recovery files restore unsaved changes and dirty state.");
            socket.Reply = _ => new AuthoringResponse();
            await recovered.Tick();
            check(!recovered.Dirty && recovered.Definition == null, "Revoking the draft clears dirty state.");
        }
        finally
        {
            AuthoringSocket.Shared.Detach(socket, socket);
            if (Directory.Exists(folder))
                Directory.Delete(folder, true);
            BepInEx.Paths.ConfigPath = "";
        }
    }

    private static bool LegacyDirty(RaidEditorSession session) =>
        session.Definition != null
        && !JToken.DeepEquals(
            JObject.FromObject(session.Definition),
            session.Baseline == null ? null : JObject.FromObject(session.Baseline)
        );

    private static AuthoringResponse Response(SeasonDefinition? definition = null, long revision = 1) =>
        new()
        {
            Grant = "offline-grant",
            DraftId = "offline",
            Definition = definition,
            Revision = revision,
        };

    private sealed class ReplySocket : WebSocket
    {
        internal Func<AuthoringSocketMessage, AuthoringResponse> Reply = null!;
        internal TaskCompletionSource<bool>? SendGate;
        internal TaskCompletionSource<bool>? SendStarted;
        public override WebSocketCloseStatus? CloseStatus => null;
        public override string? CloseStatusDescription => null;
        public override WebSocketState State => WebSocketState.Open;
        public override string? SubProtocol => null;

        public override void Abort() => throw new NotSupportedException();

        public override void Dispose() { }

        public override Task CloseAsync(WebSocketCloseStatus status, string? description, CancellationToken token) =>
            throw new NotSupportedException();

        public override Task CloseOutputAsync(WebSocketCloseStatus status, string? description, CancellationToken token) =>
            throw new NotSupportedException();

        public override Task<WebSocketReceiveResult> ReceiveAsync(ArraySegment<byte> buffer, CancellationToken token) =>
            throw new NotSupportedException();

        public override Task SendAsync(ArraySegment<byte> buffer, WebSocketMessageType type, bool end, CancellationToken token)
        {
            var message = JsonConvert.DeserializeObject<AuthoringSocketMessage>(
                Encoding.UTF8.GetString(buffer.Array!, buffer.Offset, buffer.Count)
            )!;
            SendStarted?.TrySetResult(true);
            var gate = SendGate;
            if (gate != null)
                return CompleteSend(message, gate.Task);
            return CompleteSend(message, Task.CompletedTask);
        }

        private async Task CompleteSend(AuthoringSocketMessage message, Task gate)
        {
            await gate;
            var response = new AuthoringSocketReply { RequestId = message.RequestId, Response = Reply(message) };
            AuthoringSocket.Shared.Receive(
                this,
                Encoding.UTF8.GetBytes(
                    JsonConvert.SerializeObject(
                        new Dictionary<string, string> { [AuthoringSocketMessage.ChannelName] = JsonConvert.SerializeObject(response) }
                    )
                )
            );
            return;
        }
    }
}
