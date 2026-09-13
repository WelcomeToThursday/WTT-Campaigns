using System.Text;
using Newtonsoft.Json;
using SPTarkov.DI.Annotations;
using WTT.Campaigns.Server.Seasons;
using WTT.Campaigns.Shared.Authoring;
using WTT.Campaigns.Shared.Native;
using WTT.Campaigns.Shared.Seasons;

namespace WTT.Campaigns.Server.Web.Authoring;

public sealed class PreviewState
{
    public string Key { get; set; } = "";
    public string Status { get; set; } = "Needs verification";
    public bool Verified { get; set; }
    public bool HasImage { get; set; }
    public List<string> Messages { get; set; } = [];
    public List<PreviewItemSize> ItemSizes { get; set; } = [];
}

public sealed record PreviewClient(string Id, string Character, bool Ready);

[Injectable(InjectionType.Singleton)]
public sealed class ItemPreviewService
{
    private sealed class Client
    {
        public string Owner = "",
            Character = "",
            Fingerprint = "";
        public DateTimeOffset Seen;
        public bool Ready;
    }

    private sealed class Work
    {
        public ItemPreviewJob Job = new();
        public string Client = "",
            BaseKey = "",
            Fingerprint = "",
            Destination = "";
        public DateTimeOffset Expires;
        public bool Sent;
    }

    public sealed class Receipt
    {
        public string BaseKey { get; set; } = "";
        public string Fingerprint { get; set; } = "";
        public PreviewState State { get; set; } = new();
        public DateTimeOffset Created { get; set; }
    }

    private readonly object _gate = new();
    private readonly Dictionary<string, Client> _clients = new();
    private readonly Dictionary<string, Work> _work = new();
    private readonly Dictionary<string, Receipt> _receipts = new();
    private readonly Dictionary<string, string> _selected = new();
    private readonly TraderOfferCatalogue _catalogue;
    private readonly string _cache;
    internal Func<DateTimeOffset> UtcNow = () => DateTimeOffset.UtcNow;

    public ItemPreviewService(SeasonRepository repository, TraderOfferCatalogue catalogue)
    {
        _catalogue = catalogue;
        _cache = Path.Combine(repository.ModDirectory, "user", "item-preview-cache");
        Directory.CreateDirectory(_cache);
        var providerFile = Path.Combine(_cache, "providers.json");
        try
        {
            if (File.Exists(providerFile))
                foreach (var pair in JsonConvert.DeserializeObject<Dictionary<string, string>>(File.ReadAllText(providerFile)) ?? [])
                    if (SeasonValidator.IsId(pair.Key) && SafeKey(pair.Value))
                        _selected[pair.Key] = pair.Value;
        }
        catch (Exception e) when (e is IOException or JsonException) { }
        foreach (var path in Directory.EnumerateFiles(_cache, "*.json").Take(4096))
        {
            try
            {
                var receipt = JsonConvert.DeserializeObject<Receipt>(File.ReadAllText(path));
                if (receipt != null && SafeKey(receipt.State.Key) && receipt.State.Key == Path.GetFileNameWithoutExtension(path))
                    _receipts[receipt.State.Key] = receipt;
            }
            catch (Exception e) when (e is IOException or JsonException) { }
        }
        repository.VerifyTraderOfferPublication = VerifyPublication;
    }

    private static bool SafeKey(string? key) => key != null && key.Length == 64 && key.All(c => "0123456789abcdef".Contains(c));

    private static string Hash(string value) => SeasonRepository.Hash(Encoding.UTF8.GetBytes(value));

    private string BaseKey(List<NativeItem> items) =>
        Hash(ItemPreviewExchange.Renderer + ":" + TraderOfferRules.AssemblyHash(items) + ":" + _catalogue.ContentHash(items));

    private void Expire()
    {
        foreach (var key in _clients.Where(c => UtcNow() - c.Value.Seen > TimeSpan.FromSeconds(20)).Select(c => c.Key).ToArray())
            _clients.Remove(key);
        foreach (
            var key in _work.Where(w => !_clients.ContainsKey(w.Value.Client) || UtcNow() > w.Value.Expires).Select(w => w.Key).ToArray()
        )
            _work.Remove(key);
    }

    public List<PreviewClient> Clients()
    {
        lock (_gate)
        {
            Expire();
            return _clients.Select(c => new PreviewClient(c.Key, c.Value.Character, c.Value.Ready)).ToList();
        }
    }

    public PreviewState Get(List<NativeItem> items, string campaign = "")
    {
        lock (_gate)
        {
            Expire();
            if (items.Count == 0)
                return new();
            if (
                items.Count > TraderOfferRules.MaxItems
                || items.Any(i => i == null || string.IsNullOrEmpty(i.Id))
                || items.Select(i => i.Id).Distinct().Count() != items.Count
            )
                return new()
                {
                    Status = "Unavailable",
                    Messages = ["Repair duplicate or missing item identities, and keep the assembly within 256 items."],
                };
            var invalid = items.FirstOrDefault(i => !_catalogue.IsInventoryItem(i.Template));
            if (invalid != null)
                return new()
                {
                    Status = "Unavailable",
                    Messages = ["This template is not a previewable inventory item: " + invalid.Template],
                };
            var key = BaseKey(items);
            var fingerprint = _selected.GetValueOrDefault(campaign);
            var receipt = _receipts
                .Values.Where(r => r.BaseKey == key && (fingerprint == null || r.Fingerprint == fingerprint))
                .OrderByDescending(r => r.Created)
                .FirstOrDefault();
            var work = _work.Values.FirstOrDefault(w => w.BaseKey == key && (fingerprint == null || w.Fingerprint == fingerprint));
            if (work != null)
                return new() { Key = work.Job.Key, Status = work.Sent ? "Generating" : "Queued" };
            if (receipt == null)
                return new() { Status = "Needs verification" };
            var state = SeasonCompiler.Copy(receipt.State);
            state.HasImage = File.Exists(Path.Combine(_cache, state.Key + ".png"));
            state.Status = state.Verified
                ? (_clients.Values.Any(c => c.Fingerprint == receipt.Fingerprint && c.Ready) ? "Verified" : "Cached · verified")
                : "Unavailable";
            return state;
        }
    }

    public void Request(string clientId, string destination, string campaign, List<NativeItem> items, bool refresh = false)
    {
        lock (_gate)
        {
            Expire();
            if (!_clients.TryGetValue(clientId, out var client) || !client.Ready)
                throw new InvalidOperationException("Choose an enabled client at the main menu.");
            if (items.Count is 0 or > TraderOfferRules.MaxItems)
                throw new InvalidOperationException("Preview supports 1–256 items.");
            var validation = new SeasonValidationResult();
            SeasonValidator.ItemTree(items, "Preview", validation);
            if (!validation.CanPublish)
                throw new InvalidOperationException(validation.Issues[0].Message);
            foreach (var item in items)
                if (!_catalogue.IsInventoryItem(item.Template))
                    throw new InvalidOperationException("This template is not a previewable inventory item: " + item.Template);
            if (_selected.GetValueOrDefault(campaign) != client.Fingerprint)
            {
                _selected[campaign] = client.Fingerprint;
                Atomic("providers.json", Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(_selected)));
            }
            var basis = BaseKey(items);
            var key = Hash(basis + ":" + client.Fingerprint);
            foreach (
                var old in _work
                    .Where(w => (w.Value.Destination == destination && w.Value.Job.Key != key) || (refresh && w.Value.Job.Key == key))
                    .Select(w => w.Key)
                    .ToArray()
            )
                _work.Remove(old);
            if (!refresh && (_receipts.ContainsKey(key) || _work.Values.Any(w => w.Job.Key == key)))
                return;
            if (_work.Count >= 64)
                throw new InvalidOperationException("The image queue is full. Wait for existing previews to finish.");
            _receipts.Remove(key);
            if (refresh)
                File.Delete(Path.Combine(_cache, key + ".json"));
            var job = new ItemPreviewJob
            {
                Id = Guid.NewGuid().ToString("N"),
                Key = key,
                Items = SeasonCompiler.Copy(items),
            };
            if (Encoding.UTF8.GetByteCount(JsonConvert.SerializeObject(job)) > 512 * 1024)
                throw new InvalidOperationException("This assembly is too large to preview.");
            _work[job.Id] = new()
            {
                Job = job,
                Client = clientId,
                BaseKey = basis,
                Fingerprint = client.Fingerprint,
                Destination = destination,
                Expires = UtcNow().AddMinutes(2),
            };
        }
    }

    public AuthoringResponse Exchange(string owner, string character, AuthoringRequest request)
    {
        lock (_gate)
        {
            Expire();
            var payload = request.Preview ?? throw new InvalidOperationException("Missing preview payload.");
            if (
                payload.Version != ItemPreviewExchange.Protocol
                || !Guid.TryParseExact(request.ClientId, "N", out _)
                || !SafeKey(payload.Fingerprint)
            )
                throw new InvalidOperationException("Unsupported item preview protocol or identity.");
            if (_clients.TryGetValue(request.ClientId, out var prior) && prior.Owner != owner)
                throw new InvalidOperationException("Preview client belongs to another account.");
            if (!request.Enabled)
            {
                _clients.Remove(request.ClientId);
                Expire();
                return new() { Preview = new() };
            }
            if (_clients.Count >= 32 && prior == null)
                throw new InvalidOperationException("Too many preview clients.");
            if (prior != null && (prior.Fingerprint != payload.Fingerprint || prior.Character != character))
            {
                foreach (var id in _work.Where(w => w.Value.Client == request.ClientId).Select(w => w.Key).ToArray())
                    _work.Remove(id);
                foreach (var campaign in _selected.Where(p => p.Value == prior.Fingerprint).Select(p => p.Key).ToArray())
                    _selected[campaign] = payload.Fingerprint;
                Atomic("providers.json", Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(_selected)));
            }
            _clients[request.ClientId] = new()
            {
                Owner = owner,
                Character = character,
                Fingerprint = payload.Fingerprint,
                Seen = UtcNow(),
                Ready = payload.Ready,
            };
            if (payload.Result is { } result)
            {
                if (_work.TryGetValue(result.Id, out var work))
                {
                    if (
                        !work.Sent
                        || work.Client != request.ClientId
                        || work.Fingerprint != payload.Fingerprint
                        || work.Job.Key != result.Key
                    )
                        throw new InvalidOperationException("Preview result does not match its assigned job.");
                    if (
                        result.Errors == null
                        || result.Warnings == null
                        || result.Png == null
                        || result.ItemSizes == null
                        || (result.ItemSizes.Count != 0 && result.ItemSizes.Count != work.Job.Items.Count)
                        || result.ItemSizes.Any(s => s == null || s.Width is < 1 or > 128 || s.Height is < 1 or > 128)
                        || result.Errors.Count > 64
                        || result.Warnings.Count > 64
                        || result.Errors.Concat(result.Warnings).Any(m => m == null || m.Length > 2048)
                    )
                        throw new InvalidOperationException("Preview diagnostics exceed limits.");
                    byte[]? png = null;
                    if (result.Png.Length > 0)
                    {
                        if (result.Png.Length > 1400000)
                            throw new InvalidOperationException("Preview PNG exceeds 1 MiB.");
                        png = Convert.FromBase64String(result.Png);
                        SeasonRepository.ValidateImage(png);
                        if (
                            System.Buffers.Binary.BinaryPrimitives.ReadInt32BigEndian(png.AsSpan(16, 4)) > 1024
                            || System.Buffers.Binary.BinaryPrimitives.ReadInt32BigEndian(png.AsSpan(20, 4)) > 1024
                        )
                            throw new InvalidOperationException("Preview dimensions exceed 1024 pixels.");
                    }
                    var receipt = new Receipt
                    {
                        BaseKey = work.BaseKey,
                        Fingerprint = work.Fingerprint,
                        Created = UtcNow(),
                        State = new()
                        {
                            Key = result.Key,
                            Verified = result.Verified && result.Errors.Count == 0,
                            HasImage = png != null,
                            Messages = result.Errors.Concat(result.Warnings).ToList(),
                            ItemSizes = SeasonCompiler.Copy(result.ItemSizes),
                        },
                    };
                    if (png != null)
                        Atomic(result.Key + ".png", png);
                    Atomic(result.Key + ".json", Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(receipt)));
                    _receipts[result.Key] = receipt;
                    _work.Remove(result.Id);
                    Trim();
                }
                // Late and duplicate completed jobs are acknowledged, never reinstated.
            }
            var next =
                payload.Ready && !payload.Busy
                    ? _work
                        .Values.Where(w => w.Client == request.ClientId && !w.Sent)
                        .OrderBy(w => w.Destination.StartsWith("thumbnail:", StringComparison.Ordinal) ? 1 : 0)
                        .FirstOrDefault()
                    : null;
            if (next != null && !_work.Values.Any(w => w.Client == request.ClientId && w.Sent))
            {
                next.Sent = true;
                next.Expires = UtcNow().AddMinutes(1);
            }
            else
                next = null;
            return new()
            {
                Preview = new()
                {
                    Job = next == null ? null : SeasonCompiler.Copy(next.Job),
                    ActiveJobId = _work.Values.FirstOrDefault(w => w.Client == request.ClientId && w.Sent)?.Job.Id ?? "",
                },
            };
        }
    }

    private void Atomic(string name, byte[] bytes)
    {
        var target = Path.Combine(_cache, name);
        File.WriteAllBytes(target + ".tmp", bytes);
        File.Move(target + ".tmp", target, true);
    }

    private void Trim()
    {
        var files = new DirectoryInfo(_cache).GetFiles("*.png");
        long size = files.Sum(f => f.Length);
        foreach (var receipt in _receipts.Values.OrderBy(r => r.Created).ToArray())
        {
            if (size <= 256L * 1024 * 1024 && _receipts.Count <= 4096)
                break;
            var png = Path.Combine(_cache, receipt.State.Key + ".png");
            if (File.Exists(png))
            {
                size -= new FileInfo(png).Length;
                File.Delete(png);
            }
            File.Delete(Path.Combine(_cache, receipt.State.Key + ".json"));
            _receipts.Remove(receipt.State.Key);
        }
    }

    public string? ImagePath(string key)
    {
        if (!SafeKey(key))
            return null;
        var path = Path.Combine(_cache, key + ".png");
        return File.Exists(path) ? path : null;
    }

    public void VerifyPublication(SeasonDefinition definition)
    {
        foreach (var offer in definition.TraderOffers)
            if (!Get(offer.Items, definition.Id).Verified)
                throw new InvalidOperationException("Verify the assembly with a connected client before publishing: " + offer.Name);
    }
}
