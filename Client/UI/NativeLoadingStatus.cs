using EFT;
using EFT.UI.Matchmaker;
using HarmonyLib;

namespace WTT.Campaigns.Client.UI;

/// <summary>Scoped context for EFT's existing loading caption; never owns the screen or its progress.</summary>
internal sealed class NativeLoadingStatus : IDisposable
{
    private static readonly List<NativeLoadingStatus> Stages = new();
    private static MatchmakerTimeHasCome? _screen;
    private static string _nativeStatus = "";
    private static float? _nativeProgress;
    private static bool _refreshing;
    private readonly string _text;

    private NativeLoadingStatus(string text) => _text = text;

    internal static void Enable()
    {
        var harmony = new Harmony("com.wtt.campaigns.loading-status");
        harmony.Patch(
            AccessTools.DeclaredMethod(typeof(MatchmakerTimeHasCome), nameof(MatchmakerTimeHasCome.ChangeStatus)),
            prefix: new HarmonyMethod(typeof(NativeLoadingStatus), nameof(ChangeStatus))
        );
        harmony.Patch(
            AccessTools.DeclaredMethod(typeof(MatchmakerTimeHasCome), nameof(MatchmakerTimeHasCome.OnDestroy)),
            prefix: new HarmonyMethod(typeof(NativeLoadingStatus), nameof(Close))
        );
    }

    internal static NativeLoadingStatus Begin(string text)
    {
        var stage = new NativeLoadingStatus(text);
        Stages.Add(stage);
        Refresh();
        return stage;
    }

    public void Dispose()
    {
        if (Stages.Remove(this))
            Refresh();
    }

    private static void ChangeStatus(MatchmakerTimeHasCome __instance, ref string status, float? progress)
    {
        _screen = __instance;
        if (!_refreshing)
        {
            _nativeStatus = status;
            _nativeProgress = progress;
        }
        if (Stages.Count > 0)
            status = Stages[Stages.Count - 1]._text + "\n" + status.Localized();
    }

    private static void Refresh()
    {
        if (!_screen || !_screen!.isActiveAndEnabled || !_screen.gameObject.activeInHierarchy)
            return;
        _refreshing = true;
        try
        {
            _screen.ChangeStatus(_nativeStatus, _nativeProgress);
        }
        finally
        {
            _refreshing = false;
        }
    }

    private static void Close(MatchmakerTimeHasCome __instance)
    {
        if (_screen != __instance)
            return;
        _screen = null;
        _nativeStatus = "";
        _nativeProgress = null;
    }
}
