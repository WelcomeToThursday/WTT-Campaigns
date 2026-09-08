using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using SeasonalPerks.Server.Seasons;

namespace SeasonalPerks.Server.Web.Pages;

public partial class Creator
{
    private string _librarySearch = "";
    private DraftStatus _draftView;
    private string _draftSort = "recent";
    private DraftEnvelope? _renamingDraft,
        _trashDraft;
    private string _draftName = "";
    private string? _draftMenu;
    private ElementReference _draftActionFocus;
    private bool _focusDraftAction;

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (_focusDraftAction && _draft == null)
        {
            _focusDraftAction = false;
            await _draftActionFocus.FocusAsync();
        }
    }

    private IEnumerable<DraftEnvelope> LibraryDrafts(IEnumerable<DraftEnvelope> drafts)
    {
        var matches = drafts.Where(d => d.Status == _draftView && LibraryMatches(d.Definition.Name, d.Definition.Description));
        return _draftSort switch
        {
            "name" => matches
                .OrderBy(d => d.Definition.Name, StringComparer.OrdinalIgnoreCase)
                .ThenByDescending(d => d.LastEditedUtc)
                .ThenBy(d => d.Id),
            "oldest" => matches.OrderBy(d => d.LastEditedUtc).ThenBy(d => d.Id),
            _ => matches.OrderByDescending(d => d.LastEditedUtc).ThenBy(d => d.Id),
        };
    }

    private static string DraftViewName(DraftStatus status)
    {
        return status switch
        {
            DraftStatus.Archived => "Archived",
            DraftStatus.Trashed => "Trash",
            _ => "Drafts",
        };
    }

    private void SelectDraftView(DraftStatus status)
    {
        _draftView = status;
        RefreshDraftLibrary();
    }

    private void RefreshDraftLibrary()
    {
        _renamingDraft = null;
        _trashDraft = null;
        _draftMenu = null;
        _focusDraftAction = false;
        _message = "";
    }

    private void BeginDraftRename(DraftEnvelope draft)
    {
        RefreshDraftLibrary();
        _renamingDraft = draft;
        _draftName = draft.Definition.Name;
        _focusDraftAction = true;
    }

    private void RenameLibraryDraft()
    {
        if (_renamingDraft == null)
        {
            return;
        }
        Run(() =>
        {
            var renamed = Repository.RenameDraft(_renamingDraft.Id, _renamingDraft.Revision, _draftName);
            _renamingDraft = null;
            _message = $"Draft renamed to {renamed.Definition.Name}. Published packs are unchanged.";
        });
    }

    private void RenameDraftKey(KeyboardEventArgs args)
    {
        if (args.Key == "Enter")
        {
            RenameLibraryDraft();
        }
        else if (args.Key == "Escape")
        {
            RefreshDraftLibrary();
        }
    }

    private void BeginDraftTrash(DraftEnvelope draft)
    {
        RefreshDraftLibrary();
        _trashDraft = draft;
        _focusDraftAction = true;
    }

    private void MoveLibraryDraft(DraftEnvelope draft, DraftStatus status)
    {
        Run(() =>
        {
            Repository.SetDraftStatus(draft.Id, draft.Revision, status);
            _trashDraft = null;
            _renamingDraft = null;
            _draftMenu = null;
            _message = status switch
            {
                DraftStatus.Active => $"{draft.Definition.Name} restored to Drafts.",
                DraftStatus.Archived => $"{draft.Definition.Name} archived. Restore it from Archived whenever you need it.",
                _ =>
                    $"{draft.Definition.Name} moved to Trash. You can restore it at any time; published packs and characters are unchanged.",
            };
        });
    }

    private void OpenLibraryDraft(DraftEnvelope draft)
    {
        Run(() =>
        {
            var current = Repository.Load(draft.Id);
            if (current.Status != DraftStatus.Active)
            {
                throw new InvalidOperationException(
                    "This draft was moved in another tab. Refresh the library and restore it to Drafts before editing."
                );
            }
            RefreshDraftLibrary();
            Open(current);
        });
    }

    private static string LibraryQuantity(int count, string noun)
    {
        return $"{count} {noun}{(count == 1 ? "" : "s")}";
    }

    private bool LibraryMatches(params string[] values)
    {
        var query = _librarySearch.Trim();
        return query.Length == 0 || values.Any(value => value.Contains(query, StringComparison.OrdinalIgnoreCase));
    }
}
