using System.Security.Cryptography;
using System.Text;
using System.Threading;
using BepInEx;
using BepInEx.Configuration;
using Comfort.Common;
using Cysharp.Threading.Tasks;
using Diz.LanguageExtensions;
using EFT;
using EFT.InventoryLogic;
using EFT.UI.DragAndDrop;
using JsonType;
using Newtonsoft.Json;
using UnityEngine;
using WTT.Campaigns.Shared.Authoring;
using WTT.Campaigns.Shared.Seasons;
using ZLinq;

namespace WTT.Campaigns.Client.Authoring.Preview;

// A menu-only producer on the borrowed SPT notification socket. All native item
// and graphics work stays on Unity's thread and uses detached objects.
public sealed class ItemPreviewClient : MonoBehaviour
{
    private ConfigEntry<bool> _enabled = null!;
    private string _id = Guid.NewGuid().ToString("N"),
        _fingerprint = "",
        _session = "",
        _renderId = "";
    private float _nextPoll,
        _nextFingerprint;
    private bool _polling,
        _rendering,
        _hashing,
        _advertised;
    private int _generation;
    private ItemPreviewResult? _result;
    private readonly CancellationTokenSource _lifetime = new();
    private readonly Dictionary<string, (long Length, long Modified, string Hash)> _files = new();

    private void Awake()
    {
        _enabled = Plugin.Instance.Config.Bind(
            "Web item previews",
            "Enable item authoring",
            false,
            "Allow the administrator's campaign editor to render and verify detached item assemblies at the main menu. No inventory or profile is changed."
        );
    }

    private bool Ready =>
        _enabled.Value
        && !Plugin.InRaid
        && !Plugin.Busy
        && Plugin.App?.Session?.Profile != null
        && !Singleton<GameWorld>.Instantiated
        && Singleton<ItemFactory>.Instantiated
        && Singleton<ItemIconCreator>.Instantiated;

    private void Update()
    {
        var session = Plugin.SessionId ?? "";
        if (session != _session)
        {
            _session = session;
            _generation++;
            _id = Guid.NewGuid().ToString("N");
            _result = null;
            _fingerprint = "";
        }
        if (!_enabled.Value || session.Length == 0)
        {
            if (_advertised)
            {
                _advertised = false;
                _generation++;
                _result = null;
                _renderId = "";
                if (!_polling && _fingerprint.Length > 0 && session.Length > 0)
                    _ = Poll();
                _fingerprint = "";
                _nextFingerprint = 0;
            }
            return;
        }
        _advertised = true;
        if (Ready && !_hashing && Time.unscaledTime >= _nextFingerprint)
        {
            _nextFingerprint = Time.unscaledTime + 30;
            _ = Fingerprint();
        }
        if (!_polling && _fingerprint.Length > 0 && Time.unscaledTime >= _nextPoll)
        {
            _nextPoll = Time.unscaledTime + 2;
            _ = Poll();
        }
    }

    private async Task Fingerprint()
    {
        _hashing = true;
        try
        {
            var generation = _generation;
            var templateData = ItemTemplateFingerprint.Capture(Singleton<ItemFactory>.Instance.ItemTemplates.Values);
            var value = await Task.Run(() =>
            {
                var roots = new[]
                {
                    BepInEx.Paths.PluginPath,
                    BepInEx.Paths.CachePath,
                    Path.Combine(BepInEx.Paths.GameRootPath, "user", "cache"),
                    Path.Combine(BepInEx.Paths.GameRootPath, "Bundles"),
                };
                var files = roots
                    .AsValueEnumerable()
                    .Where(Directory.Exists)
                    .SelectMany(r => Directory.EnumerateFiles(r, "*", SearchOption.AllDirectories))
                    .Where(p =>
                        p.EndsWith(".dll", StringComparison.OrdinalIgnoreCase)
                        || p.EndsWith(".bundle", StringComparison.OrdinalIgnoreCase)
                        || p.EndsWith(".manifest", StringComparison.OrdinalIgnoreCase)
                    )
                    .OrderBy(p => p)
                    .ToArray();
                var identity = new StringBuilder(ItemPreviewExchange.Renderer).Append(templateData);
                using var sha = SHA256.Create();
                foreach (var path in files)
                {
                    var info = new FileInfo(path);
                    if (
                        !_files.TryGetValue(path, out var cached)
                        || cached.Length != info.Length
                        || cached.Modified != info.LastWriteTimeUtc.Ticks
                    )
                    {
                        using var input = File.OpenRead(path);
                        cached = (info.Length, info.LastWriteTimeUtc.Ticks, BitConverter.ToString(sha.ComputeHash(input)));
                        _files[path] = cached;
                    }
                    identity.Append(Path.GetRelativePath(BepInEx.Paths.GameRootPath, path)).Append(cached.Hash);
                }
                return BitConverter
                    .ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(identity.ToString())))
                    .Replace("-", "")
                    .ToLowerInvariant();
            });
            if (this && generation == _generation)
            {
                if (_fingerprint.Length > 0 && _fingerprint != value)
                {
                    _generation++;
                    _result = null;
                }
                _fingerprint = value;
            }
        }
        catch (Exception e)
        {
            Plugin.LogInfo("Item preview fingerprint unavailable: " + e.Message);
        }
        finally
        {
            _hashing = false;
        }
    }

    private async Task Poll()
    {
        _polling = true;
        var generation = _generation;
        var result = _result;
        try
        {
            var response = await AuthoringSocket.Shared.Send(
                "preview",
                new()
                {
                    ClientId = _id,
                    Enabled = _enabled.Value,
                    Preview = new()
                    {
                        Fingerprint = _fingerprint,
                        Ready = Ready,
                        Busy = _rendering,
                        Result = result,
                    },
                },
                _lifetime.Token
            );
            if (!this || generation != _generation)
                return;
            if (response.Error != null)
                throw new InvalidOperationException(response.Error);
            if (ReferenceEquals(_result, result))
                _result = null;
            if (response.Preview?.Version != ItemPreviewExchange.Protocol)
                throw new InvalidOperationException("Update both item authoring components together.");
            if (_rendering && response.Preview.ActiveJobId != _renderId)
                _renderId = "";
            if (response.Preview.Job is { } job && !_rendering)
                _ = Render(job, generation);
        }
        catch (Exception e) when (e is not OutOfMemoryException)
        {
            _nextPoll = Time.unscaledTime + 5;
        }
        finally
        {
            _polling = false;
        }
    }

    private async Task Render(ItemPreviewJob job, int generation)
    {
        _rendering = true;
        _renderId = job.Id;
        var result = new ItemPreviewResult { Id = job.Id, Key = job.Key };
        try
        {
            if (!Ready || job.Items.Count is 0 or > TraderOfferRules.MaxItems)
                throw new InvalidOperationException("Return to the main menu to verify this assembly.");
            var root = Build(job, result.Warnings, out var sizes);
            result.ItemSizes = sizes;
            result.Verified = true;
            var sprite = await TraderSprite(root, job.Id, generation);
            if (!this || generation != _generation || !Ready || _renderId != job.Id)
                return;
            if (!sprite || !sprite.texture)
                throw new InvalidOperationException("The installed item did not produce an image.");
            result.Png = Convert.ToBase64String(Encode(sprite));
        }
        catch (Exception e) when (e is not OutOfMemoryException)
        {
            result.Verified = false;
            result.Errors.Add(e.Message.Length > 2048 ? e.Message.Substring(0, 2048) : e.Message);
        }
        finally
        {
            if (this && generation == _generation && Ready && _renderId == job.Id)
            {
                _result = result;
                // Submit immediately and receive the next queued job in the same
                // exchange. The two-second cadence is only an idle heartbeat.
                _nextPoll = 0;
            }
            _renderId = "";
            _rendering = false;
        }
    }

    private async Task<Sprite> TraderSprite(Item item, string jobId, int generation)
    {
        // Exactly the trader ItemView.RefreshIcon path: reuse native memory/disk
        // icons and its shared camera queue, including other UI icon requests.
        var icon = ItemViewFactory.LoadItemIcon(item);
        var deadline = Time.realtimeSinceStartup + 15;
        while (!icon.Sprite)
        {
            if (!this || !Ready || generation != _generation || _renderId != jobId)
                throw new OperationCanceledException("The preview was superseded.");
            if (Time.realtimeSinceStartup >= deadline)
                throw new InvalidOperationException("The item image timed out. Check its installed bundles and refresh the preview.");
            await UniTask.NextFrame(cancellationToken: _lifetime.Token);
        }
        // The sprite belongs to EFT's icon cache. Do not destroy it or its texture.
        return icon.Sprite;
    }

    internal static Item Build(ItemPreviewJob job, List<string> warnings, out List<PreviewItemSize> sizes)
    {
        var validation = new SeasonValidationResult();
        SeasonValidator.ItemTree(job.Items, "Assembly", validation);
        if (!validation.CanPublish)
            throw new InvalidOperationException(validation.Issues[0].Message);
        var factory = Singleton<ItemFactory>.Instance;
        var native = JsonConvert.DeserializeObject<FlatItem[]>(JsonConvert.SerializeObject(job.Items), EftJsonConverters.Converters)!;
        var items = native.AsValueEnumerable().ToDictionary(i => i._id, i => factory.CreateItem(i._id, i._tpl, i.upd));
        if (items.Count != native.Length)
            throw new InvalidOperationException("An item could not be constructed.");
        var rootRecord = job.Items.AsValueEnumerable().Single(i => i.ParentId == null || !items.ContainsKey(i.ParentId));
        var remaining = job.Items.AsValueEnumerable().Where(i => i.Id != rootRecord.Id).ToList();
        var stacks = new Dictionary<StackSlot, int>();
        var folded = new HashSet<string>();
        foreach (var item in items.Values)
            if (item.GetItemComponent<FoldableComponent>() is { Folded: true } component)
            {
                folded.Add(item.Id);
                component.SetFolded(false);
            }
        void FinishItem(Item item)
        {
            if (item is ContainerCollection collection)
                foreach (var stack in collection.Containers.AsValueEnumerable().OfType<StackSlot>())
                    if (stacks.TryGetValue(stack, out var expected))
                    {
                        var finish = stack.FinalizeDeserialization();
                        if (finish.Failed || stack.Count != expected)
                            throw new InvalidOperationException("Invalid ammunition capacity or positions in " + stack.ID);
                        stacks.Remove(stack);
                    }
            if (folded.Contains(item.Id))
            {
                var component = item.GetItemComponent<FoldableComponent>();
                if (!component.CanBeFolded)
                    throw new InvalidOperationException("This configured item cannot be folded.");
                component.SetFolded(true);
            }
        }
        while (remaining.Count > 0)
        {
            // Assemble from leaves upward, so native filters and grid sizes see
            // complete child subtrees when they are inserted into a parent.
            var pending = remaining
                .AsValueEnumerable()
                .Where(i => !remaining.AsValueEnumerable().Any(child => child.ParentId == i.Id))
                .ToArray();
            if (pending.Length == 0)
                throw new InvalidOperationException("Assembly contains detached or cyclic items.");
            foreach (var record in pending)
            {
                var item = items[record.Id];
                FinishItem(item);
                if (item.StackObjectsCount < 1 || item.StackObjectsCount > item.StackMaxSize)
                    throw new InvalidOperationException("Invalid item stack quantity: " + record.Template);
                var parent =
                    items[record.ParentId!] as ContainerCollection
                    ?? throw new InvalidOperationException("This item cannot contain children.");
                var container =
                    parent.Containers.AsValueEnumerable().SingleOrDefault(c => c.ID == record.SlotId)
                    ?? throw new InvalidOperationException("Unknown item slot: " + record.SlotId);
                if (container is Slot slot)
                {
                    // These are newly constructed, detached items, not an inventory
                    // move. Built-in armor parts occupy immutable native slots.
                    var added = slot.Locked ? RestoreLockedPart(slot, item) : slot.Add(item, false);
                    if (!added.Succeeded)
                        throw new InvalidOperationException(added.Error.ToString());
                }
                else if (container is Grid grid)
                {
                    if (record.Location?.Grid == null)
                        throw new InvalidOperationException("Choose a grid position.");
                    var location = JsonConvert.DeserializeObject<LocationInGrid>(
                        JsonConvert.SerializeObject(record.Location),
                        EftJsonConverters.Converters
                    )!;
                    var added = grid.Add(item, location, false);
                    if (!added.Succeeded)
                        throw new InvalidOperationException(added.Error.ToString());
                }
                else if (container is StackSlot stack)
                {
                    if (!stack.CheckCompatibility(item))
                        throw new InvalidOperationException("Ammunition is incompatible with its container.");
                    stack.AddAtPosition(item, ItemStackPosition.Require(record.Location, parent is AmmoBox));
                    stacks[stack] = checked(stacks.GetValueOrDefault(stack) + item.StackObjectsCount);
                }
                else
                    throw new InvalidOperationException("Unsupported item container: " + record.SlotId);
                remaining.Remove(record);
            }
        }
        FinishItem(items[rootRecord.Id]);
        foreach (var item in items.Values.AsValueEnumerable().OfType<ContainerCollection>())
        foreach (var slot in item.Containers.AsValueEnumerable().OfType<Slot>().Where(s => s.Required && s.ContainedItem == null))
            if (warnings.Count < 64)
                warnings.Add("Incomplete assembly: " + slot.ID + " is empty.");
        sizes = job
            .Items.AsValueEnumerable()
            .Select(i =>
            {
                var size = items[i.Id].CalculateCellSize();
                return new PreviewItemSize { Width = size.X, Height = size.Y };
            })
            .ToList();
        return items[rootRecord.Id];
    }

    private static OperationResult<ContainerAddResult> RestoreLockedPart(Slot slot, Item item)
    {
        if (
            !slot.Locked
            || slot.ContainedItem != null
            || !slot.CheckCompatibility(item)
            || slot.BlockerSlots.Count > 0
            || (item.IsSpecialSlotOnly && !slot.IsSpecial)
            || slot.GetConflictingSlot(item).AsValueEnumerable().Any(s => s.ContainedItem != null)
        )
            throw new InvalidOperationException("Incompatible built-in item in slot " + slot.ID);
        var conflicts = slot.CheckConflictingItems(item);
        if (conflicts.Failed)
            throw new InvalidOperationException(conflicts.Error.ToString());
        // Keep the slot locked. Only this detached reconstruction skips the
        // gameplay move restriction, after validating its contents above.
        return slot.AddWithoutRestrictions(item);
    }

    private static byte[] Encode(Sprite sprite)
    {
        var rect = sprite.textureRect;
        var texture = sprite.texture;
        // Native generated/cached icons are readable ARGB32 textures. Export
        // those exact pixels, like EFT's SaveIconAsync, without another GPU blit.
        if (texture.isReadable && rect.width <= 1024 && rect.height <= 1024)
        {
            if (
                rect.x == 0
                && rect.y == 0
                && rect.width == texture.width
                && rect.height == texture.height
                && texture.format is TextureFormat.ARGB32 or TextureFormat.RGBA32 or TextureFormat.RGB24
            )
                return CheckedPng(texture.EncodeToPNG());
            var crop = new Texture2D((int)rect.width, (int)rect.height, TextureFormat.RGBA32, false);
            try
            {
                crop.SetPixels(texture.GetPixels((int)rect.x, (int)rect.y, (int)rect.width, (int)rect.height));
                return CheckedPng(crop.EncodeToPNG());
            }
            finally
            {
                Destroy(crop);
            }
        }
        var scale = Math.Min(1f, 1024f / Math.Max(rect.width, rect.height));
        int width = Math.Max(1, Mathf.RoundToInt(rect.width * scale)),
            height = Math.Max(1, Mathf.RoundToInt(rect.height * scale));
        var target = RenderTexture.GetTemporary(width, height, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
        var previous = RenderTexture.active;
        var previousSrgbWrite = GL.sRGBWrite;
        Texture2D? copy = null;
        try
        {
            GL.sRGBWrite = QualitySettings.activeColorSpace == ColorSpace.Linear;
            Graphics.Blit(
                sprite.texture,
                target,
                new Vector2(rect.width / sprite.texture.width, rect.height / sprite.texture.height),
                new Vector2(rect.x / sprite.texture.width, rect.y / sprite.texture.height)
            );
            RenderTexture.active = target;
            copy = new Texture2D(width, height, TextureFormat.RGBA32, false);
            copy.ReadPixels(new Rect(0, 0, width, height), 0, 0);
            copy.Apply();
            return CheckedPng(copy.EncodeToPNG());
        }
        finally
        {
            RenderTexture.active = previous;
            GL.sRGBWrite = previousSrgbWrite;
            if (copy)
                Destroy(copy);
            RenderTexture.ReleaseTemporary(target);
        }
    }

    private static byte[] CheckedPng(byte[] bytes)
    {
        if (bytes.Length > 1024 * 1024)
            throw new InvalidOperationException("The generated image exceeds 1 MiB.");
        return bytes;
    }

    private void OnDestroy()
    {
        _generation++;
        _lifetime.Cancel();
        _lifetime.Dispose();
    }
}
