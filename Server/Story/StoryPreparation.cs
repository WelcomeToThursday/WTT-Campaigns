using SeasonalPerks.Shared.Story;

namespace SeasonalPerks.Server.Story;

internal sealed class StoryPreparation
{
    internal string Id { get; } = Guid.NewGuid().ToString("N");
    internal required string Identity { get; init; }
    internal required string ProfileHash { get; init; }
    internal DateTime Expires { get; } = DateTime.UtcNow.AddMinutes(5);
    internal Dictionary<int, (int Maximum, int Value)> Draws { get; } = new();
    internal Dictionary<string, List<string>> Selections { get; } = new();
    internal StoryHandover? Pending { get; set; }
    internal bool Ready { get; set; }

    internal int Draw(int index, int maximum)
    {
        if (Draws.TryGetValue(index, out var draw))
        {
            if (draw.Maximum != maximum)
            {
                throw new InvalidOperationException("The prepared conversation changed. Start it again.");
            }

            return draw.Value;
        }
        var value = System.Security.Cryptography.RandomNumberGenerator.GetInt32(maximum);
        Draws[index] = (maximum, value);
        return value;
    }
}
