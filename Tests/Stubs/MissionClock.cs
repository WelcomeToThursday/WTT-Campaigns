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
    internal sealed class AbstractGame
    {
        public GameTimer GameTimer = new();
    }

    internal sealed class GameTimer
    {
        public TimeSpan? SessionTime = TimeSpan.FromMinutes(45);
        public DateTime? _escapeDateTime = DateTimeExtensions.UtcNow.AddMinutes(45);
        public DateTime? EscapeDateTime => _escapeDateTime;
        private readonly DateTime _started = DateTimeExtensions.UtcNow;
        public TimeSpan PastTime => DateTimeExtensions.UtcNow - _started;

        public void ChangeSessionTime(TimeSpan sessionTime)
        {
            SessionTime = sessionTime;
            _escapeDateTime = _started + sessionTime;
        }
    }

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

namespace Comfort.Common
{
    internal static class Singleton<T>
        where T : class
    {
        public static T Instance = null!;
        public static bool Instantiated => Instance != null;
    }
}

internal static class DateTimeExtensions
{
    public static DateTime UtcNow => new DateTime(2026, 9, 21, 0, 0, 0, DateTimeKind.Utc).AddSeconds(UnityEngine.Time.realtimeSinceStartup);
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
