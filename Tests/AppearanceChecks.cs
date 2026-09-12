using System.Text.Json;
using SPTarkov.Server.Core.Models.Common;
using SPTarkov.Server.Core.Models.Eft.Common.Tables;
using SPTarkov.Server.Core.Utils.Json.Converters;
using WTT.Campaigns.Server.Profiles;
using Path = System.IO.Path;

namespace WTT.Campaigns.Tests;

internal static class AppearanceChecks
{
    internal static async Task Run(string database)
    {
        var templates = JsonSerializer.Deserialize<Dictionary<MongoId, CustomizationItem>>(
            File.ReadAllText(Path.Combine(database, "templates", "customization.json")),
            new JsonSerializerOptions { Converters = { new StringToMongoIdConverter() } }
        )!;
        var count = 0;
        void Check(bool value, string message)
        {
            if (!value)
                throw new InvalidOperationException(message);
            count++;
        }
        foreach (var side in new[] { "Usec", "Bear" })
        {
            var heads = templates
                .Values.Where(x =>
                    x.Parent == AppearanceSelection.HeadParent && x.Properties.AvailableAsDefault && x.Properties.Side.Contains(side)
                )
                .ToArray();
            var voices = templates
                .Values.Where(x =>
                    x.Parent == AppearanceSelection.VoiceParent && x.Properties.AvailableAsDefault && x.Properties.Side.Contains(side)
                )
                .ToArray();
            Check(heads.Length == 8 && voices.Length > 0, side + " has all eight authentic heads and voice choices");
            foreach (var head in heads)
            foreach (var voice in voices)
            {
                var appearance = new Customization
                {
                    Head = heads[0].Id,
                    Voice = voices[0].Id,
                    Body = new MongoId("111111111111111111111111"),
                };
                var writes = 0;
                await AppearanceSelection.Apply(
                    templates,
                    side,
                    appearance,
                    head.Id.ToString(),
                    voice.Id.ToString(),
                    () =>
                    {
                        Check(appearance.Head == head.Id && appearance.Voice == voice.Id, "Both choices reach the same save");
                        writes++;
                        return Task.CompletedTask;
                    }
                );
                Check(writes == 1 && appearance.Body.ToString() == "111111111111111111111111", "One save preserves clothing");
            }
            var original = new Customization { Head = heads[0].Id, Voice = voices[0].Id };
            var attempted = false;
            var failed = false;
            try
            {
                await AppearanceSelection.Apply(
                    templates,
                    side,
                    original,
                    heads[1].Id.ToString(),
                    voices[0].Id.ToString(),
                    () =>
                    {
                        if (!attempted)
                        {
                            attempted = true;
                            throw new IOException("simulated disk failure");
                        }
                        Check(
                            original.Head == heads[0].Id && original.Voice == voices[0].Id,
                            "Rollback reaches save cache after disk failure"
                        );
                        return Task.CompletedTask;
                    }
                );
            }
            catch (IOException)
            {
                failed = true;
            }
            Check(failed && original.Head == heads[0].Id && original.Voice == voices[0].Id, "Failed save restores both confirmed choices");
            var other = templates.Values.First(x =>
                x.Parent == AppearanceSelection.HeadParent && x.Properties.AvailableAsDefault && !x.Properties.Side.Contains(side)
            );
            foreach (var invalid in new[] { "", "not-an-id", "000000000000000000000001", other.Id.ToString(), voices[0].Id.ToString() })
            {
                failed = false;
                try
                {
                    await AppearanceSelection.Apply(
                        templates,
                        side,
                        original,
                        invalid,
                        voices[0].Id.ToString(),
                        () => throw new Exception("Invalid choice reached persistence")
                    );
                }
                catch (InvalidOperationException)
                {
                    failed = true;
                }
                Check(failed && original.Head == heads[0].Id, "Invalid, wrong-kind, and wrong-faction heads cannot mutate profile");
            }
            failed = false;
            try
            {
                await AppearanceSelection.Apply(
                    templates,
                    side,
                    original,
                    heads[1].Id.ToString(),
                    heads[0].Id.ToString(),
                    () => throw new Exception("Invalid voice reached persistence")
                );
            }
            catch (InvalidOperationException)
            {
                failed = true;
            }
            Check(failed && original.Head == heads[0].Id, "Invalid voice cannot partially commit a valid head");
        }
        Console.WriteLine($"Appearance persistence: {count} offline checks passed.");
    }
}
