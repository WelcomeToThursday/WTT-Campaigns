using System.Collections.Concurrent;
using System.Security.Cryptography;
using Newtonsoft.Json;
using SeasonalPerks.Shared;
using SPTarkov.DI.Annotations;
using SPTarkov.Server.Core.Models.Common;
using SPTarkov.Server.Core.Models.Eft.Common;
using SPTarkov.Server.Core.Models.Eft.Profile;
using SPTarkov.Server.Core.Models.Enums;
using SPTarkov.Server.Core.Models.Spt.Tables;
using SPTarkov.Server.Core.Servers;
using SPTarkov.Server.Core.Services.Modding;
using SPTarkov.Server.Core.Services.Profile;

namespace SeasonalPerks.Server;

public sealed class AccountLink
{
    public HashSet<string> ActiveRaidProfiles { get; set; } = [];
    public string? SeasonalId { get; set; }
    public bool Created { get; set; }
    public string Mode { get; set; } = "normal";
}
