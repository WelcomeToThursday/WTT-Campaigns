using WTT.Campaigns.Client.Authoring.Navigation;

namespace WTT.Campaigns.Tests;

internal static class NavigationMeshDecoderChecks
{
    internal static void Run(Action<bool, string> check)
    {
        byte[] FloatBytes(params float[] values) => values.SelectMany(BitConverter.GetBytes).ToArray();
        void Reject(Action action, string reason)
        {
            var rejected = false;
            try
            {
                action();
            }
            catch (InvalidOperationException)
            {
                rejected = true;
            }
            check(rejected, reason);
        }
        var interleaved = FloatBytes(99, 1, 2, 3, 98, 4, 5, 6);
        check(
            NavigationMeshDecoder.Positions(interleaved, 2, 16, 4, false).SequenceEqual(new float[] { 1, 2, 3, 4, 5, 6 }),
            "Recovery decodes interleaved positions with nonzero offset and stride"
        );
        check(
            NavigationMeshDecoder
                .Positions(new byte[] { 0, 0x3c, 0, 0xc0, 1, 0 }, 1, 6, 0, true)
                .SequenceEqual(new float[] { 1, -2, MathF.Pow(2, -24) }),
            "Recovery decodes half precision and subnormal coordinates"
        );
        Reject(() => NavigationMeshDecoder.Positions(new byte[11], 1, 12, 0, false), "Recovery rejects truncated positions");
        Reject(() => NavigationMeshDecoder.Positions(interleaved, 2, 16, 8, false), "Recovery rejects attributes crossing stride");
        Reject(
            () => NavigationMeshDecoder.Positions(interleaved, 1, 16, int.MaxValue, false),
            "Recovery rejects overflowing attribute offsets"
        );
        Reject(() => NavigationMeshDecoder.Positions(interleaved, 0, 16, 4, false), "Recovery rejects empty vertex arrays");
        Reject(() => NavigationMeshDecoder.Positions(FloatBytes(1, float.NaN, 3), 1, 12, 0, false), "Recovery rejects NaN coordinates");
        Reject(() => NavigationMeshDecoder.Positions(new byte[] { 0, 0x7c, 0, 0, 0, 0 }, 1, 6, 0, true), "Recovery rejects half infinity");
        var parts = new[] { new NavigationMeshDecoder.Part(1, 3, 2), new NavigationMeshDecoder.Part(4, 3, 0) };
        var shortIndices = new byte[] { 99, 0, 0, 0, 1, 0, 2, 0, 4, 0, 3, 0, 2, 0 };
        check(
            NavigationMeshDecoder.Triangles(shortIndices, false, 5, parts).SequenceEqual(new[] { 2, 3, 4, 4, 3, 2 }),
            "Recovery preserves submesh ranges, winding and base vertex offsets"
        );
        var wide = new uint[] { 65536, 65537, 65538 }
            .SelectMany(BitConverter.GetBytes)
            .ToArray();
        check(
            NavigationMeshDecoder
                .Triangles(wide, true, 65539, new[] { new NavigationMeshDecoder.Part(0, 3, 0) })
                .SequenceEqual(new[] { 65536, 65537, 65538 }),
            "Recovery preserves 32-bit indices"
        );
        Reject(
            () => NavigationMeshDecoder.Triangles(wide, true, 3, new[] { new NavigationMeshDecoder.Part(0, 3, 0) }),
            "Recovery rejects out-of-range indices"
        );
        Reject(
            () => NavigationMeshDecoder.Triangles(shortIndices, false, 5, new[] { new NavigationMeshDecoder.Part(1, 3, -1) }),
            "Recovery rejects negative adjusted indices"
        );
        Reject(
            () => NavigationMeshDecoder.Triangles(shortIndices, false, 5, new[] { new NavigationMeshDecoder.Part(4, 6, 0) }),
            "Recovery rejects truncated submesh ranges"
        );
        Reject(
            () => NavigationMeshDecoder.Triangles(shortIndices, false, 5, new[] { new NavigationMeshDecoder.Part(1, 2, 0) }),
            "Recovery rejects incomplete triangles"
        );
        Reject(
            () => NavigationMeshDecoder.Triangles(shortIndices, false, 5, new[] { new NavigationMeshDecoder.Part(int.MaxValue, 3, 0) }),
            "Recovery rejects overflowing submesh ranges before allocation"
        );
        Reject(
            () =>
                NavigationMeshDecoder.Triangles(
                    new byte[] { 255, 255, 255, 255, 0, 0, 0, 0, 1, 0, 0, 0 },
                    true,
                    3,
                    new[] { new NavigationMeshDecoder.Part(0, 3, 1) }
                ),
            "Recovery does not wrap unsigned 32-bit indices"
        );
    }
}
