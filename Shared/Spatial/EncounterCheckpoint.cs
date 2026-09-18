namespace WTT.Campaigns.Shared.Spatial;

public sealed class EncounterCheckpoint
{
    public string EncounterId { get; set; } = "";
    public bool Activated { get; set; }
    public string ActivationKey { get; set; } = "";
    public double ActivationOffset { get; set; }
    public List<EncounterWaveCheckpoint> Waves { get; set; } = new();
}

public sealed class EncounterWaveCheckpoint
{
    public string WaveId { get; set; } = "";
    public EncounterWaveStatus Status { get; set; }
    public double? ActivatedOffset { get; set; }
    public double? CompletedOffset { get; set; }
    public List<string> LivingProfileIds { get; set; } = new();
}
