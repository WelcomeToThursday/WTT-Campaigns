using System;
using System.Globalization;
using System.IO;
using System.Linq;
using UnityEngine;
using UnityEngine.Rendering;

namespace SeasonalPerks.UI.Media;

public static class StoryRoomAmbient
{
    public static void Configure(GameObject room, Camera camera, string traderId)
    {
        var lighting = room.GetComponent<StoryRoomLighting>();
        if (!lighting)
            return;
#if UNITY_EDITOR
        var text = File.ReadAllText(
            Path.GetFullPath(Path.Combine(Application.dataPath, "../../SeasonalPerks/UI/Resources/Story/ambient-probes.txt"))
        );
#else
        using var stream =
            typeof(StoryRoomAmbient).Assembly.GetManifestResourceStream("SeasonalPerks.Story.ambient-probes.txt")
            ?? throw new InvalidDataException("Missing trader ambient probes.");
        using var reader = new StreamReader(stream);
        var text = reader.ReadToEnd();
#endif
        var row = text.Split('\n').Select(line => line.Trim().Split(' ')).FirstOrDefault(values => values[0] == traderId);
        if (row != null)
        {
            if (row.Length != 28)
                throw new InvalidDataException("Invalid trader ambient probe.");
            lighting.Environment.AmbientProbe = row.Skip(1).Select(value => float.Parse(value, CultureInfo.InvariantCulture)).ToArray();
        }
        else if (lighting.Environment.AmbientMode == AmbientMode.Flat)
        {
            // The authored Peacekeeper room uses flat ambient rather than a recovered sky.
            var probe = new SphericalHarmonicsL2();
            probe.AddAmbientLight(lighting.Environment.AmbientSky);
            lighting.Environment.AmbientProbe = new float[27];
            for (var c = 0; c < 3; c++)
            for (var i = 0; i < 9; i++)
                lighting.Environment.AmbientProbe[c * 9 + i] = probe[c, i];
        }
        var scope = camera.GetComponent<StoryRoomRenderState>() ?? camera.gameObject.AddComponent<StoryRoomRenderState>();
        scope.Environment = lighting.Environment;
    }
}
