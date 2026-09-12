using System.Net.WebSockets;
using System.Text;
using System.Threading;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using WTT.Campaigns.Shared.Authoring;

namespace WTT.Campaigns.Client.Authoring;

// Borrows the native notification socket. Never receives from, closes or aborts it.
internal sealed class AuthoringSocket
{
    internal static readonly AuthoringSocket Shared = new();
    private readonly object _sync = new();
    private readonly SemaphoreSlim _sendGate = new(1, 1);
    private readonly Dictionary<string, TaskCompletionSource<AuthoringResponse>> _pending = new();
    private readonly TimeSpan _timeout;
    private object? _owner;
    private WebSocket? _socket;

    internal AuthoringSocket(TimeSpan? timeout = null) => _timeout = timeout ?? TimeSpan.FromSeconds(10);

    internal void Attach(object owner, WebSocket socket)
    {
        lock (_sync)
        {
            if (ReferenceEquals(_socket, socket))
            {
                return;
            }

            FailPending();
            _owner = owner;
            _socket = socket;
        }
    }

    internal void Detach(object owner, WebSocket socket)
    {
        lock (_sync)
        {
            if (!ReferenceEquals(_owner, owner) || !ReferenceEquals(_socket, socket))
            {
                return;
            }

            _owner = null;
            _socket = null;
            FailPending();
        }
    }

    private void FailPending()
    {
        foreach (var pending in _pending.Values)
        {
            pending.TrySetException(new IOException("SPT notification connection changed; the editor will retry."));
        }

        _pending.Clear();
    }

    internal bool Receive(object owner, byte[] data)
    {
        var json = Encoding.UTF8.GetString(data);
        if (!json.Contains(AuthoringSocketMessage.ChannelName))
        {
            return false;
        }

        JObject envelope;
        try
        {
            envelope = JObject.Parse(json);
        }
        catch (JsonException)
        {
            return false;
        }
        if (!envelope.TryGetValue(AuthoringSocketMessage.ChannelName, out var payload))
        {
            return false;
        }
        // Consume our notifications even when late, malformed or addressed to another client.
        // They must never reach EFT's native notification type deserializer.
        try
        {
            var reply = JsonConvert.DeserializeObject<AuthoringSocketReply>(payload.Value<string>() ?? "");
            if (reply?.RequestId == null || reply.Response == null)
            {
                return true;
            }

            lock (_sync)
            {
                if (ReferenceEquals(owner, _owner) && _pending.TryGetValue(reply.RequestId, out var pending))
                {
                    pending.TrySetResult(reply.Response);
                }
            }
        }
        catch (Exception e) when (e is JsonException or ArgumentException or InvalidCastException) { }
        return true;
    }

    internal async Task<AuthoringResponse> Send(string operation, AuthoringRequest request, CancellationToken cancellation = default)
    {
        var id = Guid.NewGuid().ToString("N");
        var payload = Encoding.UTF8.GetBytes(
            JsonConvert.SerializeObject(
                new AuthoringSocketMessage
                {
                    RequestId = id,
                    Operation = operation,
                    Request = request,
                }
            )
        );
        if (payload.Length > AuthoringSocketMessage.MaxBytes)
        {
            throw new InvalidOperationException("The editor update exceeds the WebSocket 4 MiB limit.");
        }

        var reply = new TaskCompletionSource<AuthoringResponse>(TaskCreationOptions.RunContinuationsAsynchronously);
        WebSocket socket;
        lock (_sync)
        {
            socket = _socket ?? throw new IOException("Waiting for SPT's notification WebSocket connection.");
            if (socket.State != WebSocketState.Open)
            {
                throw new IOException("Waiting for SPT to reconnect its notification WebSocket.");
            }

            _pending.Add(id, reply);
        }
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellation);
        deadline.CancelAfter(_timeout);
        using var registration = deadline.Token.Register(() =>
            reply.TrySetException(new IOException("Editor reply timed out or the raid ended; the shared connection remains open."))
        );
        // Native Mono aborts a WebSocket when SendAsync is cancelled. Bound the caller's
        // wait instead, and hold the send gate until the uncancelled send actually finishes.
        _ = SendFrame(socket, payload, reply, deadline.Token);
        try
        {
            return await reply.Task.ConfigureAwait(false);
        }
        finally
        {
            lock (_sync)
            {
                _pending.Remove(id);
            }
        }
    }

    private async Task SendFrame(WebSocket socket, byte[] payload, TaskCompletionSource<AuthoringResponse> reply, CancellationToken token)
    {
        try
        {
            await _sendGate.WaitAsync(token).ConfigureAwait(false);
            try
            {
                token.ThrowIfCancellationRequested();
                lock (_sync)
                {
                    if (!ReferenceEquals(socket, _socket))
                    {
                        throw new IOException("SPT notification connection changed before sending.");
                    }
                }
                await socket
                    .SendAsync(new ArraySegment<byte>(payload), WebSocketMessageType.Text, true, CancellationToken.None)
                    .ConfigureAwait(false);
            }
            finally
            {
                _sendGate.Release();
            }
        }
        catch (Exception e)
        {
            reply.TrySetException(new IOException("Unable to send the editor update on SPT's notification connection.", e));
        }
    }
}
