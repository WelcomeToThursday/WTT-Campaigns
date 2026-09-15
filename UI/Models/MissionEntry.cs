using System;

namespace WTT.Campaigns.UI.Models;

/// <summary>A client-facing mission card. The client maps server summaries into this small UI model.</summary>
public sealed class MissionEntry
{
    public string Id = "";
    public string Name = "";
    public string Briefing = "";
    public string Location = "";
    public string Status = "Locked";
    public string FailureReason = "";
    public string[] Objectives = Array.Empty<string>();
    public bool Unlocked;
    public bool Completed;
    public bool Active;
    public bool CanDeploy;
    public bool CanReplay;
    public bool CanResume;
    public bool CanCancel;
}
