using Microsoft.AspNetCore.Components;
using WTT.Campaigns.Shared.Authoring;

namespace WTT.Campaigns.Server.Web.Pages;

public partial class Creator
{
    [Parameter]
    public bool LevelEditor { get; set; }

    [SupplyParameterFromQuery(Name = "layout")]
    public string? LayoutQuery { get; set; }
    private string _levelLayout = "";

    private void OpenLevel(EditorLayoutChoice choice) =>
        Run(() =>
        {
            var draft = Repository.Load(choice.DraftId);
            if (
                !draft.Definition.MapLayouts.Any(l =>
                    l.Id == choice.Id && EditorContentRules.Mode(draft.Definition, l.Id) == EditorContentMode.Level
                )
            )
                throw new InvalidOperationException("Choose an ordinary level. Campaign missions require Mission Editor.");
            Open(draft);
            _levelLayout = choice.Id;
        });

    private void CheckLevelEdit()
    {
        if (!LevelEditor)
            return;
        if (!S.MapLayouts.Any(l => l.Id == _levelLayout && EditorContentRules.Mode(S, l.Id) == EditorContentMode.Level))
            throw new InvalidOperationException("Select an ordinary level from the level library.");
        EditorContentRules.ValidateEdit(
            Newtonsoft.Json.JsonConvert.DeserializeObject<WTT.Campaigns.Shared.Seasons.SeasonDefinition>(_baseline)!,
            S
        );
    }
}
