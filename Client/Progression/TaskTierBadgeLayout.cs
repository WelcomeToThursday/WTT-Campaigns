using TMPro;
using UnityEngine;

namespace WTT.Campaigns.Client.Progression;

/// <summary>Reserves title space for the badge while a tiered quest is selected.</summary>
internal sealed class TaskTierBadgeLayout : MonoBehaviour
{
    internal const float BadgeSize = 26;
    private const float Gap = 8;
    private TMP_Text? _title;
    private Vector4 _margin;
    private TextOverflowModes _overflow;

    internal void Apply(TMP_Text title, RectTransform badge, bool visible)
    {
        if (!visible)
        {
            Restore();
            return;
        }
        // Capture once per active selection sequence, not once per Show call.
        if (_title == null)
        {
            _title = title;
            _margin = title.margin;
            _overflow = title.overflowMode;
        }
        var margin = _margin;
        margin.z += BadgeSize + Gap;
        title.margin = margin;
        title.overflowMode = TextOverflowModes.Ellipsis;
        badge.anchoredPosition = new Vector2(-_margin.z, 0);
    }

    private void Restore()
    {
        if (_title == null)
            return;
        _title.margin = _margin;
        _title.overflowMode = _overflow;
        _title = null;
    }

    private void OnDisable() => Restore();

    private void OnDestroy() => Restore();
}
