using System.Net.WebSockets;
using System.Text;
using Newtonsoft.Json;
using WTT.Campaigns.Client.Authoring;
using WTT.Campaigns.Shared.Authoring;

namespace WTT.Campaigns.Tests;

internal static class AuthoringSocketChecks
{
    internal static async Task Run(Action<bool, string> check)
    {
        var bridge = new AuthoringSocket(TimeSpan.FromMilliseconds(150));
        var owner = new object();
        var socket = new FakeSocket();
        check(await Fails(bridge.Send("poll", new())), "Editor waits for the native socket instead of opening another connection");
        bridge.Attach(owner, socket);
        var first = bridge.Send("poll", new());
        var second = bridge.Send("submit", new() { OperationId = "receipt" });
        check(socket.Sent.Count == 2, "Editor sends over the borrowed native socket");
        var a = socket.Sent[0];
        var b = socket.Sent[1];
        check(
            a.RequestId != b.RequestId && b.Request.OperationId == "receipt",
            "Transport correlation is separate from retry receipt identity"
        );
        check(!bridge.Receive(owner, Encoding.UTF8.GetBytes("{\"type\":\"ping\"}")), "Native notifications pass through unchanged");
        check(
            !bridge.Receive(owner, Encoding.UTF8.GetBytes("{\"text\":\"wttCampaignsAuthoring\"}")),
            "Ordinary text mentioning the editor is not intercepted"
        );
        check(bridge.Receive(owner, Reply(b.RequestId, 2)), "Editor replies are consumed before native notification parsing");
        check((await second).Revision == 2 && !first.IsCompleted, "Out-of-order replies resolve only their matching request");
        bridge.Receive(owner, Reply(a.RequestId, 1));
        check((await first).Revision == 1, "First request receives its own reply");
        check(bridge.Receive(owner, Reply(Guid.NewGuid().ToString("N"), 99)), "Replies for other connections are safely consumed");
        check(
            bridge.Receive(owner, Encoding.UTF8.GetBytes("{\"wttCampaignsAuthoring\":\"broken\"}")),
            "Malformed editor payload cannot reach native notification parsing"
        );

        var timeout = bridge.Send("submit", new() { OperationId = "retry" });
        var oldId = socket.Sent.Last().RequestId;
        check(await Fails(timeout), "Missing editor replies time out");
        check(
            !socket.Managed && !socket.CancellableSend,
            "Timeout never aborts, disposes, receives from or cancels a send on the native socket"
        );
        var retry = bridge.Send("submit", new() { OperationId = "retry" });
        bridge.Receive(owner, Reply(oldId, 90));
        check(
            !retry.IsCompleted && socket.Sent.Last().Request.OperationId == "retry",
            "Late reply cannot satisfy a retry; operation receipt remains stable"
        );
        bridge.Receive(owner, Reply(socket.Sent.Last().RequestId, 3));
        await retry;

        var interrupted = bridge.Send("poll", new());
        var replacement = new FakeSocket();
        var newOwner = new object();
        bridge.Attach(newOwner, replacement);
        check(await Fails(interrupted), "Native reconnect fails outstanding requests for retry");
        bridge.Detach(owner, socket);
        var resumed = bridge.Send("poll", new());
        bridge.Receive(owner, Reply(replacement.Sent[0].RequestId, 88));
        check(!resumed.IsCompleted, "Old native receive loop cannot satisfy requests on its replacement");
        bridge.Receive(newOwner, Reply(replacement.Sent[0].RequestId, 4));
        check((await resumed).Revision == 4, "Late disconnect cannot detach the replacement native socket");
        using var retirement = new CancellationTokenSource();
        var retiring = bridge.Send("poll", new(), retirement.Token);
        retirement.Cancel();
        check(await Fails(retiring) && !replacement.Managed, "Raid retirement cancels only editor work");
        bridge.Detach(newOwner, replacement);
        check(await Fails(bridge.Send("poll", new())), "Disconnected native socket is no longer used");

        var stalledBridge = new AuthoringSocket(TimeSpan.FromMilliseconds(50));
        var stalled = new FakeSocket { SendHold = new(TaskCreationOptions.RunContinuationsAsynchronously) };
        stalledBridge.Attach(owner, stalled);
        var stalledRequest = stalledBridge.Send("poll", new());
        var queuedRequest = stalledBridge.Send("poll", new());
        check(
            await Fails(stalledRequest) && await Fails(queuedRequest),
            "Stalled native sends do not leave the editor waiting indefinitely"
        );
        check(
            stalled.Sent.Count == 1 && !stalled.CancellableSend && !stalled.Managed,
            "Send gate stays held while a timed-out native send remains active"
        );
        stalled.SendHold.SetResult(true);
    }

    private static byte[] Reply(string id, long revision)
    {
        return Encoding.UTF8.GetBytes(
            JsonConvert.SerializeObject(
                new Dictionary<string, string>
                {
                    [AuthoringSocketMessage.ChannelName] = JsonConvert.SerializeObject(
                        new AuthoringSocketReply
                        {
                            RequestId = id,
                            Response = new() { Revision = revision },
                        }
                    ),
                }
            )
        );
    }

    private static async Task<bool> Fails(Task task)
    {
        try
        {
            await task;
            return false;
        }
        catch (IOException)
        {
            return true;
        }
    }

    private sealed class FakeSocket : WebSocket
    {
        internal readonly List<AuthoringSocketMessage> Sent = new();
        internal bool Managed,
            CancellableSend;
        internal TaskCompletionSource<bool>? SendHold;
        public override WebSocketCloseStatus? CloseStatus
        {
            get { return null; }
        }

        public override string? CloseStatusDescription
        {
            get { return null; }
        }

        public override string? SubProtocol
        {
            get { return null; }
        }

        public override WebSocketState State
        {
            get { return WebSocketState.Open; }
        }

        public override void Abort()
        {
            Managed = true;
        }

        public override void Dispose()
        {
            Managed = true;
        }

        public override Task CloseAsync(WebSocketCloseStatus status, string? description, CancellationToken token)
        {
            Managed = true;
            return Task.CompletedTask;
        }

        public override Task CloseOutputAsync(WebSocketCloseStatus status, string? description, CancellationToken token)
        {
            Managed = true;
            return Task.CompletedTask;
        }

        public override Task<WebSocketReceiveResult> ReceiveAsync(ArraySegment<byte> buffer, CancellationToken token)
        {
            Managed = true;
            throw new Exception("Editor must not receive from native socket");
        }

        public override Task SendAsync(ArraySegment<byte> buffer, WebSocketMessageType type, bool end, CancellationToken token)
        {
            CancellableSend |= token.CanBeCanceled;
            Sent.Add(
                JsonConvert.DeserializeObject<AuthoringSocketMessage>(Encoding.UTF8.GetString(buffer.Array!, buffer.Offset, buffer.Count))!
            );
            return SendHold?.Task ?? Task.CompletedTask;
        }
    }
}
