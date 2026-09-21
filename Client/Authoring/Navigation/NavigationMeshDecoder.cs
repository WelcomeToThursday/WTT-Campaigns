namespace WTT.Campaigns.Client.Authoring.Navigation;

// Kept independent of Unity so buffer layouts and malformed inputs can be checked offline.
internal static class NavigationMeshDecoder
{
    internal readonly struct Part
    {
        internal readonly int Start,
            Count,
            BaseVertex;

        internal Part(int start, int count, int baseVertex)
        {
            Start = start;
            Count = count;
            BaseVertex = baseVertex;
        }
    }

    internal static float[] Positions(byte[] bytes, int count, int stride, int offset, bool half)
    {
        var width = half ? 2 : 4;
        if (count <= 0 || count > 1000000 || offset < 0 || stride < (long)offset + width * 3 || (long)count * stride > bytes.Length)
            throw new InvalidOperationException("Invalid or truncated position buffer.");
        var result = new float[checked(count * 3)];
        for (var i = 0; i < count; i++)
        for (var axis = 0; axis < 3; axis++)
        {
            var at = i * stride + offset + axis * width;
            var value = half ? Half(Read(bytes, at, 2)) : BitConverter.Int32BitsToSingle(unchecked((int)Read(bytes, at, 4)));
            if (float.IsNaN(value) || float.IsInfinity(value))
                throw new InvalidOperationException("Position buffer contains non-finite coordinates.");
            result[i * 3 + axis] = value;
        }
        return result;
    }

    internal static int[] Triangles(byte[] bytes, bool wide, int vertexCount, Part[] parts)
    {
        var width = wide ? 4 : 2;
        long count = 0;
        foreach (var part in parts)
        {
            if (part.Start < 0 || part.Count < 0 || part.Count % 3 != 0 || ((long)part.Start + part.Count) * width > bytes.Length)
                throw new InvalidOperationException("Invalid or truncated triangle range.");
            count += part.Count;
        }
        if (count <= 0 || count > 3000000 || vertexCount <= 0)
            throw new InvalidOperationException("Empty or oversized triangle buffer.");
        var result = new int[(int)count];
        var next = 0;
        foreach (var part in parts)
            for (var i = 0; i < part.Count; i++)
            {
                var index = (long)Read(bytes, (part.Start + i) * width, width) + part.BaseVertex;
                if (index < 0 || index >= vertexCount)
                    throw new InvalidOperationException("Triangle index is outside the vertex buffer.");
                result[next++] = (int)index;
            }
        return result;
    }

    private static uint Read(byte[] bytes, int at, int width)
    {
        uint value = 0;
        for (var i = 0; i < width; i++)
            value |= (uint)bytes[at + i] << (8 * i);
        return value;
    }

    private static float Half(uint value)
    {
        var sign = (value & 0x8000) != 0 ? -1f : 1f;
        var exponent = (value >> 10) & 31;
        var fraction = value & 1023;
        if (exponent == 31)
            return fraction == 0 ? sign * float.PositiveInfinity : float.NaN;
        return sign * (float)(exponent == 0 ? fraction * Math.Pow(2, -24) : (1024 + fraction) * Math.Pow(2, (int)exponent - 25));
    }
}
