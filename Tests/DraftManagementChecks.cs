using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using SeasonalPerks.Server.Seasons;
using SeasonalPerks.Shared.Seasons;

namespace SeasonalPerks.Tests;

internal static class DraftManagementChecks
{
    public static void Run(SeasonRepository store, string directory, DraftEnvelope source, Action<bool, string> check)
    {
        var draft = store.Create(true, source.Definition);
        var id = draft.Id;
        var seasonId = draft.Definition.Id;
        var gameplay = SeasonRepository.GameplayHash(draft.Definition);
        var pack = store.Publish(draft, SeasonValidator.Validate(draft.Definition));
        var publishedName = store.Pack(pack).Name;
        var publishedPath = Path.Combine(directory, "creator", "packs", pack, "definition.json");
        var publishedBytes = File.ReadAllBytes(publishedPath);
        var old = SeasonCompiler.Copy(draft);
        draft = store.RenameDraft(id, draft.Revision, "  Managed draft  ");
        check(
            draft.Definition.Name == "Managed draft" && draft.Definition.Id == seasonId,
            "Quick rename trims the name and preserves season identity"
        );
        check(draft.LastEditedUtc != null && draft.LastEditedUtc >= old.LastEditedUtc, "Rename records its edit time");
        Reject(() => store.RenameDraft(id, old.Revision, "Stale rename"), "Stale rename cannot overwrite newer content");
        Reject(() => store.RenameDraft(id, draft.Revision, "  "), "Blank draft names are rejected");
        Reject(() => store.RenameDraft(id, draft.Revision, new string('a', 121)), "Overlong draft names are rejected");
        var edited = draft.LastEditedUtc;
        var beforeArchive = SeasonCompiler.Copy(draft);
        draft = store.SetDraftStatus(id, draft.Revision, DraftStatus.Archived);
        check(
            !store.Drafts().Any(d => d.Id == id) && store.Drafts(DraftStatus.Archived).Any(d => d.Id == id),
            "Archived drafts leave the editing list and appear in Archived"
        );
        check(new SeasonRepository(directory).Load(id).Status == DraftStatus.Archived, "Archive state survives a repository restart");
        Reject(() => store.Save(beforeArchive), "An already-open editor cannot save over an archived draft");
        Reject(
            () => store.Publish(draft, SeasonValidator.Validate(draft.Definition)),
            "Archived drafts cannot be published without restoring"
        );
        var bypass = SeasonCompiler.Copy(draft);
        bypass.Status = DraftStatus.Active;
        Reject(() => store.Save(bypass), "A save cannot bypass archive state with a matching revision");
        Reject(() => store.SetDraftStatus(id, old.Revision, DraftStatus.Trashed), "Stale removal cannot hide a newer draft");
        draft = store.SetDraftStatus(id, draft.Revision, DraftStatus.Trashed);
        var trashedJson = JsonConvert.SerializeObject(draft.Definition);
        check(
            store.Drafts(DraftStatus.Trashed).Any(d => d.Id == id) && !store.Drafts(DraftStatus.Archived).Any(d => d.Id == id),
            "Trash removes a draft from Archived and keeps it recoverable"
        );
        Reject(() => store.RenameDraft(id, draft.Revision, "Hidden rename"), "Trashed drafts must be restored before editing");
        draft = new SeasonRepository(directory).SetDraftStatus(id, draft.Revision, DraftStatus.Active);
        check(
            JsonConvert.SerializeObject(draft.Definition) == trashedJson && draft.LastEditedUtc == edited,
            "Restore preserves complete content and last edit time across restart"
        );
        check(
            store.Drafts().Any(d => d.Id == id) && SeasonRepository.GameplayHash(draft.Definition) == gameplay,
            "Restored draft returns to editing with unchanged gameplay"
        );
        check(
            store.Pack(pack).Name == publishedName && File.ReadAllBytes(publishedPath).SequenceEqual(publishedBytes),
            "Rename, archive and trash leave published content byte-for-byte unchanged"
        );
        Reject(() => store.SetDraftStatus(id, draft.Revision, (DraftStatus)999), "Unknown organization state is rejected");
        Reject(() => store.SetDraftStatus("../selection", 1, DraftStatus.Trashed), "Organization rejects path traversal");

        // Existing draft files have neither field. Reading the new library must not rewrite them.
        var legacy = store.Create(false);
        var path = Path.Combine(directory, "creator", "drafts", legacy.Id + ".json");
        var json = JObject.Parse(File.ReadAllText(path));
        json.Remove("Status");
        json.Remove("LastEditedUtc");
        File.WriteAllText(path, json.ToString());
        File.SetLastWriteTimeUtc(path, new DateTime(2025, 1, 2, 3, 4, 5, DateTimeKind.Utc));
        var bytes = File.ReadAllBytes(path);
        var loaded = store.Drafts().Single(d => d.Id == legacy.Id);
        check(
            loaded.Status == DraftStatus.Active && loaded.LastEditedUtc?.Year == 2025,
            "Older drafts default to Drafts and use their file date for sorting"
        );
        check(File.ReadAllBytes(path).SequenceEqual(bytes), "Listing old drafts does not migrate or rewrite files");
        loaded.Definition.Description = "Saved with draft management";
        loaded = store.Save(loaded);
        check(
            loaded.LastEditedUtc?.Year >= 2026 && store.Load(legacy.Id).Definition.Description == loaded.Definition.Description,
            "Editing an old draft persists its new metadata and content"
        );

        void Reject(Action action, string message)
        {
            try
            {
                action();
            }
            catch (Exception e) when (e is InvalidOperationException or InvalidDataException or IOException)
            {
                check(true, message);
                return;
            }
            check(false, message);
        }
    }
}
