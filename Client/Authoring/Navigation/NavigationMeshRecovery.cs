using System.Diagnostics;
using System.Runtime.ExceptionServices;
using Cysharp.Threading.Tasks;
using UnityEngine;
using UnityEngine.Rendering;

namespace WTT.Campaigns.Client.Authoring.Navigation;

// Owns only temporary readable copies. Never changes a native mesh or collider.
internal sealed class NavigationMeshRecovery : IDisposable
{
    internal int MeshLimit { get; }
    internal long ByteLimit { get; }
    internal bool Full { get; }
    private const int BufferLimit = 8 * 1024 * 1024;
    internal const int BatchSize = 4;
    private readonly Dictionary<Mesh, Mesh> _copies = new();
    private readonly Dictionary<Mesh, string> _unresolved = new();
    internal readonly List<object> Results = new();
    internal long ReservedBytes { get; private set; }
    internal int Count => _copies.Count;
    internal int UnattemptedMeshes { get; private set; }

    internal NavigationMeshRecovery(bool full = false)
    {
        Full = full;
        MeshLimit = full ? 4096 : 128;
        ByteLimit = (full ? 256L : 64L) * 1024 * 1024;
    }

    internal Mesh? Find(Mesh original) => original && _copies.TryGetValue(original, out var copy) && copy ? copy : null;

    internal string UnresolvedReason(Mesh original) =>
        _unresolved.TryGetValue(original, out var reason) ? reason : "No recovery attempt recorded for this source";

    internal async UniTask Recover(NavigationSurvey survey, Action<string> progress, CancellationToken token)
    {
        if (!SystemInfo.supportsAsyncGPUReadback)
            throw new InvalidOperationException("This graphics device does not support asynchronous GPU readback.");
        var attempted = new HashSet<Mesh>();
        var work = new List<NavigationSurvey.Issue>();
        var scanned = 0;
        var slice = Stopwatch.StartNew();
        foreach (var issue in survey.Issues)
        {
            if (++scanned % 128 == 0)
            {
                progress($"Selecting unique recovery meshes: {scanned}/{survey.Issues.Count} sources…");
                if (slice.ElapsedMilliseconds >= 2 || scanned % 4096 == 0)
                {
                    await UniTask.NextFrame(cancellationToken: token);
                    slice.Restart();
                }
            }
            token.ThrowIfCancellationRequested();
            var original = issue.UnreadableMesh;
            if (!original || original!.isReadable || !attempted.Add(original))
                continue;
            if (attempted.Count > MeshLimit)
            {
                UnattemptedMeshes++;
                _unresolved[original] = $"Not attempted: recovery limit of {MeshLimit} unique meshes reached";
                continue;
            }
            work.Add(issue);
        }
        for (var offset = 0; offset < work.Count; offset += BatchSize)
        {
            token.ThrowIfCancellationRequested();
            var count = Math.Min(BatchSize, work.Count - offset);
            progress(
                $"Recovering {(Full ? "full-map" : "local")} meshes {offset + 1}–{offset + count}/{work.Count} · up to {BatchSize} concurrent readbacks…"
            );
            var batch = new UniTask[count];
            // Each method runs on Unity's thread until its first readback awaits.
            // Reservations are made before that await, so concurrent jobs share the
            // same hard memory limit without racing native objects or counters.
            for (var i = 0; i < count; i++)
                batch[i] = RecoverOne(work[offset + i], token);
            await Drain(batch);
            await UniTask.NextFrame(cancellationToken: token);
        }
    }

    private async UniTask RecoverOne(NavigationSurvey.Issue issue, CancellationToken token)
    {
        var original = issue.UnreadableMesh!;
        token.ThrowIfCancellationRequested();
        var watch = Stopwatch.StartNew();
        var name = original.name;
        var id = original.GetInstanceID();
        var vertices = original.vertexCount;
        var parts = original.subMeshCount;
        var result = "";
        object? metadata = null;
        try
        {
            var copy = await ReadMesh(original, value => metadata = value, token);
            _copies.Add(original, copy);
            result = "Recovered readable collision-source copy";
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception error)
        {
            result = error.Message;
            _unresolved[original] = "Recovery failed: " + result;
        }
        Results.Add(
            new
            {
                issue.Path,
                Mesh = name,
                InstanceId = id,
                Vertices = vertices,
                SubMeshes = parts,
                Recovered = _copies.ContainsKey(original),
                Result = result,
                Metadata = metadata,
                Milliseconds = watch.ElapsedMilliseconds,
            }
        );
    }

    private static async UniTask Drain(UniTask[] batch)
    {
        // WhenAll may fault before siblings release borrowed GPU buffers. Always
        // drain every started job before cancellation/failure can dispose the cache.
        Exception? failure = null;
        foreach (var work in batch)
            try
            {
                await work;
            }
            catch (Exception error)
            {
                failure ??= error;
            }
        if (failure != null)
            ExceptionDispatchInfo.Capture(failure).Throw();
    }

    private async UniTask<Mesh> ReadMesh(Mesh original, Action<object> metadata, CancellationToken token)
    {
        if (!original.HasVertexAttribute(VertexAttribute.Position) || original.GetVertexAttributeDimension(VertexAttribute.Position) != 3)
            throw new InvalidOperationException("Expected a three-component position attribute.");
        var format = original.GetVertexAttributeFormat(VertexAttribute.Position);
        if (format != VertexAttributeFormat.Float32 && format != VertexAttributeFormat.Float16)
            throw new InvalidOperationException("Unsupported position format: " + format);
        var stream = original.GetVertexAttributeStream(VertexAttribute.Position);
        var stride = original.GetVertexBufferStride(stream);
        var offset = original.GetVertexAttributeOffset(VertexAttribute.Position);
        var vertexCount = original.vertexCount;
        var bounds = original.bounds;
        if (vertexCount <= 0 || vertexCount > 1000000 || original.subMeshCount <= 0 || original.subMeshCount > 256)
            throw new InvalidOperationException("Empty or oversized mesh metadata.");
        var parts = new NavigationMeshDecoder.Part[original.subMeshCount];
        long indexEnd = 0,
            triangleIndices = 0;
        for (var i = 0; i < parts.Length; i++)
        {
            var part = original.GetSubMesh(i);
            if (part.topology != MeshTopology.Triangles || part.indexStart < 0 || part.indexCount < 0 || part.indexCount % 3 != 0)
                throw new InvalidOperationException("Only valid triangle submeshes are supported by this probe.");
            parts[i] = new(part.indexStart, part.indexCount, part.baseVertex);
            indexEnd = Math.Max(indexEnd, (long)part.indexStart + part.indexCount);
            triangleIndices += part.indexCount;
        }
        var wide = original.indexFormat == IndexFormat.UInt32;
        var vertexBytes = (long)vertexCount * stride;
        var indexBytes = indexEnd * (wide ? 4 : 2);
        // Include transient arrays and both CPU/GPU copies in a conservative reservation, not a measured peak.
        var reserve = vertexBytes + indexBytes + vertexCount * 48L + triangleIndices * 12L;
        metadata(
            new
            {
                PositionFormat = format.ToString(),
                Stream = stream,
                Stride = stride,
                Offset = offset,
                IndexFormat = original.indexFormat.ToString(),
                VertexBytes = vertexBytes,
                IndexBytes = indexBytes,
                TriangleIndices = triangleIndices,
                Bounds = bounds.ToString("R"),
                ReservedBytes = reserve,
            }
        );
        if (
            vertexBytes <= 0
            || indexBytes <= 0
            || vertexBytes > BufferLimit
            || indexBytes > BufferLimit
            || reserve > ByteLimit - ReservedBytes
        )
            throw new InvalidOperationException($"Recovery budget exceeded (8 MiB per buffer, {ByteLimit / 1048576} MiB per probe).");
        ReservedBytes += reserve;
        // Do not change buffer targets: that could recreate buffers on the native unreadable mesh.
        using var vertexBuffer = original.GetVertexBuffer(stream);
        using var indexBuffer = original.GetIndexBuffer();
        if (vertexBuffer == null || indexBuffer == null || !vertexBuffer.IsValid() || !indexBuffer.IsValid())
            throw new InvalidOperationException("GPU buffers unavailable; this collision mesh needs another geometry adapter.");
        if (vertexBytes > (long)vertexBuffer.count * vertexBuffer.stride || indexBytes > (long)indexBuffer.count * indexBuffer.stride)
            throw new InvalidOperationException("GPU buffer size does not match mesh metadata.");
        var positions = NavigationMeshDecoder.Positions(
            await ReadBuffer(vertexBuffer, (int)vertexBytes, token),
            vertexCount,
            stride,
            offset,
            format == VertexAttributeFormat.Float16
        );
        var indices = NavigationMeshDecoder.Triangles(await ReadBuffer(indexBuffer, (int)indexBytes, token), wide, vertexCount, parts);
        token.ThrowIfCancellationRequested();
        if (!original || original.vertexCount != vertexCount || original.bounds != bounds || original.subMeshCount != parts.Length)
            throw new InvalidOperationException("Source mesh changed during readback.");
        var vectors = new Vector3[vertexCount];
        var expanded = bounds;
        expanded.Expand(Math.Max(.002f, bounds.size.magnitude * .0001f));
        for (var i = 0; i < vectors.Length; i++)
        {
            vectors[i] = new(positions[i * 3], positions[i * 3 + 1], positions[i * 3 + 2]);
            if (!expanded.Contains(vectors[i]))
                throw new InvalidOperationException("Recovered positions fall outside the original mesh bounds.");
        }
        var nondegenerate = false;
        for (var i = 0; i < indices.Length; i += 3)
            if (
                Vector3.Cross(vectors[indices[i + 1]] - vectors[indices[i]], vectors[indices[i + 2]] - vectors[indices[i]]).sqrMagnitude
                > 1e-16f
            )
            {
                nondegenerate = true;
                break;
            }
        if (!nondegenerate)
            throw new InvalidOperationException("Recovered mesh has no non-degenerate triangles.");
        Mesh? copy = new Mesh { name = "CampaignEditor recovered " + original.name, indexFormat = IndexFormat.UInt32 };
        try
        {
            copy.vertices = vectors;
            copy.triangles = indices;
            copy.RecalculateBounds();
            var result = copy;
            copy = null;
            return result;
        }
        finally
        {
            if (copy)
                UnityEngine.Object.Destroy(copy);
        }
    }

    private static async UniTask<byte[]> ReadBuffer(GraphicsBuffer buffer, int size, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        var completion = new UniTaskCompletionSource<byte[]>();
        _ = AsyncGPUReadback.Request(
            buffer,
            size,
            0,
            request =>
            {
                try
                {
                    if (request.hasError)
                        completion.TrySetException(new InvalidOperationException("GPU readback failed; no readable copy was created."));
                    else
                        completion.TrySetResult(request.GetData<byte>().ToArray());
                }
                catch (Exception error)
                {
                    completion.TrySetException(error);
                }
            }
        );
        // A request cannot be cancelled. Drain it before disposing its borrowed buffer wrapper.
        // Copy in the callback because request data is only valid briefly after completion.
        byte[] bytes;
        try
        {
            bytes = await completion.Task;
        }
        finally
        {
            await UniTask.SwitchToMainThread();
        }
        token.ThrowIfCancellationRequested();
        return bytes;
    }

    public void Dispose()
    {
        foreach (var copy in _copies.Values)
            if (copy)
                UnityEngine.Object.Destroy(copy);
        _copies.Clear();
    }
}
