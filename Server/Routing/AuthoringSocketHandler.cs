using System.Net.WebSockets;
using System.Text;
using Microsoft.Extensions.DependencyInjection;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using SPTarkov.DI.Annotations;
using SPTarkov.Server.Core.Models.Eft.Ws;
using SPTarkov.Server.Core.Servers.Ws;
using SPTarkov.Server.Core.Servers.Ws.Message;
using WTT.Campaigns.Server.Profiles;
using WTT.Campaigns.Server.Web.Authoring;
using WTT.Campaigns.Shared.Authoring;

namespace WTT.Campaigns.Server.Routing;

[Injectable(InjectionType.Singleton)]
public sealed class AuthoringSocketHandler(IServiceProvider services) : ISptWebSocketMessageHandler
{
    public async Task OnSptMessageAsync(string sessionID, WebSocket client, byte[] rawData)
    {
        JObject envelope;
        try
        {
            envelope = JObject.Parse(Encoding.UTF8.GetString(rawData));
        }
        catch (JsonException)
        {
            return;
        }
        if (envelope["Channel"]?.Type != JTokenType.String || (string?)envelope["Channel"] != AuthoringSocketMessage.ChannelName)
        {
            return;
        }

        var requestId = envelope["RequestId"]?.Type == JTokenType.String ? (string?)envelope["RequestId"] : null;
        if (!Guid.TryParseExact(requestId, "N", out _))
        {
            return;
        }

        AuthoringResponse response;
        try
        {
            if (rawData.Length > AuthoringSocketMessage.MaxBytes)
            {
                throw new InvalidOperationException("Editor message is too large.");
            }

            var message = envelope.ToObject<AuthoringSocketMessage>();
            if (message?.Request == null || message.Operation is not ("poll" or "submit" or "preview"))
            {
                throw new InvalidOperationException("Unsupported editor WebSocket operation.");
            }
            // Identity comes from the existing SPT connection, never from message data.
            var seasons = services.GetRequiredService<SeasonService>();
            var authoring = services.GetRequiredService<RaidAuthoringService>();
            var root = seasons.ResolveRoot(sessionID);
            using var lease = seasons.Enter(root);
            var character = seasons.EffectiveId(root);
            response =
                message.Operation == "preview"
                    ? services.GetRequiredService<ItemPreviewService>().Exchange(root, character, message.Request)
                : message.Operation == "poll" ? authoring.Poll(root, character, message.Request)
                : authoring.Submit(root, character, message.Request);
        }
        catch (Exception e) when (e is InvalidOperationException or ArgumentException or IOException or JsonException or FormatException)
        {
            response = new AuthoringResponse { Error = e.Message };
        }
        var reply = JsonConvert.SerializeObject(new AuthoringSocketReply { RequestId = requestId!, Response = response });
        if (Encoding.UTF8.GetByteCount(JsonConvert.SerializeObject(reply)) > AuthoringSocketMessage.MaxBytes - 512)
        {
            reply = JsonConvert.SerializeObject(
                new AuthoringSocketReply
                {
                    RequestId = requestId!,
                    Response = new() { Error = "The editor response exceeds the WebSocket 4 MiB limit." },
                }
            );
        }
        // Use SPT's sender and its per-socket lock, so replies cannot overlap native notifications.
        // SPT sends to the session's sockets; only the matching request ID accepts this reply.
        // Resolve after construction: SPT's connection handler itself owns this message handler.
        await services
            .GetRequiredService<SptWebSocketConnectionHandler>()
            .SendMessageAsync(
                sessionID,
                new WsNotificationEvent { ExtensionData = new() { [AuthoringSocketMessage.ChannelName] = reply } }
            );
    }
}
