using Newtonsoft.Json;

namespace SeasonalPerks.Shared.Story;

// These values are our protocol, never live or beta enum ordinals. JSON always uses names.
[JsonConverter(typeof(StoryEnumConverter))]
public enum StoryActionType
{
    SetVariable,
    DiaryNote,
    SwitchDialog,
    QuitAction,
    TradingScreenAction,
    QuestsScreenAction,
    EmbedQuestDialog,
    SelectQuest,
    AcceptQuest,
    HandoverItem,
    FinishQuest,
    PlayerReward,
    SelectSubService,
    PurchaseService,
    CompleteItem,
    StartCinematic,
}
