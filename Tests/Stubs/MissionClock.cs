// Native-shaped clock doubles for the source-linked production checkpoint snapshot.
// The installed assembly contract is independently inspected by EditorEnvironmentChecks.
namespace UnityEngine
{
    internal static class Time
    {
        public static float realtimeSinceStartup;
    }
}

namespace EFT
{
    internal sealed class GameDateTime
    {
        private DateTime _date;
        private float _started;
        public float TimeFactor { get; set; } = 7;
        public float TimeFactorMod = 1;
        public bool Locked = true;

        public DateTime Calculate() => _date.AddSeconds((UnityEngine.Time.realtimeSinceStartup - _started) * TimeFactor * TimeFactorMod);

        public void ResetForce(DateTime date)
        {
            _date = date;
            _started = UnityEngine.Time.realtimeSinceStartup;
        }
    }
}

internal sealed class TOD_Time : UnityEngine.Object
{
    public EFT.GameDateTime? GameDateTime = new();
    public float DayLengthInMinutes = 30;
    public bool LockCurrentTime;
}

internal sealed class TOD_CycleParameters
{
    public DateTime DateTime;
}

internal sealed class TODSkyProvider
{
    public static bool IsAvailable = true;
    public static TODSkyProvider Instance = new();
    public TOD_Time CurrentTime = new();
    public TOD_CycleParameters Cycle = new();
}
