using Newtonsoft.Json;
using SPTarkov.DI.Annotations;
using SPTarkov.Server.Core.DI;
using SPTarkov.Server.Core.Models.Utils;
using SPTarkov.Server.Core.Utils;

namespace WTT.Campaigns.Server.Progression;

[Injectable]
public sealed class ProgressionRouter(JsonUtil json, ProgressionService progression)
    : StaticRouter(
        json,
        [
            new RouteAction<ProgressionRequest>(
                "/wtt-campaigns/progression",
                (_, _, _, _, _) => new ValueTask<string>(JsonConvert.SerializeObject(progression.Metadata))
            ),
        ]
    );

public record ProgressionRequest : IRequestData;
