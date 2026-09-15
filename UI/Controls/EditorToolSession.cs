using System;
using System.Collections.Generic;

namespace WTT.Campaigns.UI.Controls;

// Session-only record state. View-specific search, expansion and scroll state
// stay with each retained browser; this data never enters campaign/profile JSON.
public sealed class EditorToolSession<TPicked>
    where TPicked : class
{
    public string Selection = "",
        LibraryKey = "";
    public int Page;
    public TPicked? Picked;
    public readonly List<(string Id, string Label)> Rows = new();

    public void Reconcile(Func<string, bool> exists)
    {
        if (Selection.Length > 0 && !exists(Selection))
        {
            Selection = "";
            Picked = null;
        }
    }
}
