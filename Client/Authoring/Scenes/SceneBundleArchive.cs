using System.IO;
using System.Text;

namespace WTT.Campaigns.Client.Authoring.Scenes;

// Read only the UnityFS directory, never deserialize objects or load a scene into Unity.
// Scene archives contain a sharedAssets serialized file and separate scene serialized files.
// The native EasyBundle loader calls LoadAllAssetsAsync before exposing the bundle, so this
// check must run before Retain, including for dependencies and saved placement references.
internal static class SceneBundleArchive
{
    internal enum Contents
    {
        Assets,
        Scene,
    }

    private const int MaximumDirectoryBytes = 16 * 1024 * 1024;

    internal static Contents Read(string path)
    {
        using var file = File.OpenRead(path);
        return Read(file);
    }

    internal static Contents Read(Stream file)
    {
        using var reader = new BinaryReader(file, Encoding.UTF8, leaveOpen: true);
        if (Text(reader) != "UnityFS")
            throw Invalid("Unsupported asset archive format.");
        var version = UInt(reader);
        Text(reader); // Minimum engine version.
        var engine = Text(reader);
        var size = Long(reader);
        var compressed = UInt(reader);
        var expanded = UInt(reader);
        var flags = UInt(reader);
        if (version < 6 || version > 8 || size != file.Length || (flags & 0x40) == 0 || (flags & ~0x2ffu) != 0)
            throw Invalid("Unsupported asset archive header.");
        if (compressed == 0 || compressed > MaximumDirectoryBytes || expanded == 0 || expanded > MaximumDirectoryBytes)
            throw Invalid("Asset archive directory is too large or empty.");
        // Unity 2019.4.15 introduced alignment; newer bundles use format version 7+.
        if (version >= 7 || Aligned2019(engine))
            file.Position = (file.Position + 15) & ~15L;
        if ((flags & 0x80) != 0)
            file.Position = size - compressed;
        if (file.Position < 0 || compressed > file.Length - file.Position)
            throw Invalid("Asset archive directory is truncated.");
        var bytes = reader.ReadBytes((int)compressed);
        switch (flags & 0x3f)
        {
            case 0:
                if (compressed != expanded)
                    throw Invalid("Invalid uncompressed archive directory.");
                break;
            case 2:
            case 3:
                bytes = ExpandLz4(bytes, (int)expanded);
                break;
            default:
                throw Invalid("Unsupported asset archive directory compression.");
        }
        using var directory = new BinaryReader(new MemoryStream(bytes), Encoding.UTF8);
        directory.BaseStream.Position = 16; // Content hash.
        var blocks = UInt(directory);
        if (blocks > (directory.BaseStream.Length - directory.BaseStream.Position) / 10)
            throw Invalid("Invalid asset archive block count.");
        directory.BaseStream.Position += blocks * 10L;
        var count = UInt(directory);
        if (count == 0 || count > (directory.BaseStream.Length - directory.BaseStream.Position) / 21)
            throw Invalid("Invalid asset archive entry count.");
        var serialized = 0;
        var scene = false;
        for (var i = 0u; i < count; i++)
        {
            if (Long(directory) < 0 || Long(directory) < 0)
                throw Invalid("Invalid asset archive entry range.");
            var entryFlags = UInt(directory);
            var name = Text(directory);
            if ((entryFlags & 4) == 0)
                continue;
            serialized++;
            scene |= name.EndsWith(".sharedAssets", StringComparison.OrdinalIgnoreCase);
        }
        if (scene)
            return Contents.Scene;
        // Unknown multi-file layouts fail closed rather than enter the native prefab loader.
        if (serialized != 1)
            throw Invalid("Unsupported serialized asset archive layout.");
        return Contents.Assets;
    }

    private static bool Aligned2019(string version)
    {
        var parts = version.Split('.');
        if (parts.Length < 3 || parts[0] != "2019" || parts[1] != "4")
            return false;
        var count = 0;
        while (count < parts[2].Length && char.IsDigit(parts[2][count]))
            count++;
        return int.TryParse(parts[2].Substring(0, count), out var patch) && patch >= 15;
    }

    private static uint UInt(BinaryReader reader) =>
        (uint)(reader.ReadByte() << 24 | reader.ReadByte() << 16 | reader.ReadByte() << 8 | reader.ReadByte());

    private static long Long(BinaryReader reader) => unchecked((long)((ulong)UInt(reader) << 32 | UInt(reader)));

    private static string Text(BinaryReader reader)
    {
        var bytes = new List<byte>();
        for (var i = 0; i < 4096; i++)
        {
            var value = reader.ReadByte();
            if (value == 0)
                return Encoding.UTF8.GetString(bytes.ToArray());
            bytes.Add(value);
        }
        throw Invalid("Asset archive name is too long.");
    }

    // UnityFS directory compression uses raw LZ4 blocks (both LZ4 and LZ4HC).
    private static byte[] ExpandLz4(byte[] source, int size)
    {
        var result = new byte[size];
        var input = 0;
        var output = 0;
        int Length(int value)
        {
            if (value != 15)
                return value;
            byte extra;
            do
            {
                if (input >= source.Length || value > size)
                    throw Invalid("Invalid compressed archive length.");
                extra = source[input++];
                value += extra;
            } while (extra == 255);
            return value;
        }
        while (input < source.Length)
        {
            var token = source[input++];
            var literals = Length(token >> 4);
            if (literals > source.Length - input || literals > size - output)
                throw Invalid("Invalid compressed archive literals.");
            Array.Copy(source, input, result, output, literals);
            input += literals;
            output += literals;
            if (input == source.Length)
                break;
            if (source.Length - input < 2)
                throw Invalid("Invalid compressed archive offset.");
            var offset = source[input++] | source[input++] << 8;
            var match = Length(token & 15) + 4;
            if (offset == 0 || offset > output || match > size - output)
                throw Invalid("Invalid compressed archive match.");
            for (var i = 0; i < match; i++)
            {
                result[output] = result[output - offset];
                output++;
            }
        }
        if (output != size)
            throw Invalid("Truncated compressed archive directory.");
        return result;
    }

    private static InvalidDataException Invalid(string reason) => new(reason);
}
