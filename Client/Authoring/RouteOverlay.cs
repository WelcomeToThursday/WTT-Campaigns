using UnityEngine;
using UnityEngine.UI;
using WTT.Campaigns.Client.Spatial;
using WTT.Campaigns.Shared.Spatial;

namespace WTT.Campaigns.Client.Authoring;

// One reusable canvas mesh; labels are pooled children and never receive pointer events.
internal sealed class RouteOverlay : MaskableGraphic
{
    private readonly List<(SpatialCapture Point, RouteRole Role, int Number)> _points = new();
    private readonly List<(Vector2 A, Vector2 B)> _segments = new();
    private readonly List<(Vector2 Position, RouteRole Role, bool Selected)> _markers = new();
    private readonly List<Text> _labels = new();
    private readonly List<RouteOverlayBatch> _batches = new();
    private const int BatchSize = 400;
    private Font _font = null!;
    private static readonly Color Dark = new(.025f, .03f, .035f, .95f);

    internal void Initialize(Font font)
    {
        _font = font;
        raycastTarget = false;
        maskable = false;
    }

    internal static Color RoleColor(RouteRole role)
    {
        var rgb = RouteVisuals.Color(role);
        return new Color(((rgb >> 16) & 255) / 255f, ((rgb >> 8) & 255) / 255f, (rgb & 255) / 255f);
    }

    internal void Refresh(MapLayout layout, Camera camera, string selected)
    {
        _segments.Clear();
        _markers.Clear();
        RouteVisuals.Points(layout, _points);
        var near = camera.nearClipPlane + .001f;
        for (var i = 1; i < _points.Count; i++)
        {
            var a = ZoneRuntime.Vector(_points[i - 1].Point.Position);
            var b = ZoneRuntime.Vector(_points[i].Point.Position);
            var az = Vector3.Dot(a - camera.transform.position, camera.transform.forward);
            var bz = Vector3.Dot(b - camera.transform.position, camera.transform.forward);
            if (!RouteVisuals.ClipNear(az, bz, near, out var from, out var to))
                continue;
            var sa = camera.WorldToScreenPoint(Vector3.Lerp(a, b, from));
            var sb = camera.WorldToScreenPoint(Vector3.Lerp(a, b, to));
            if (!RouteVisuals.ClipScreen(ref sa.x, ref sa.y, ref sb.x, ref sb.y, Screen.width, Screen.height))
                continue;
            if (Local(sa, out var la) && Local(sb, out var lb))
                _segments.Add((la, lb));
        }
        var labelIndex = 0;
        foreach (var point in _points)
        {
            var screen = camera.WorldToScreenPoint(ZoneRuntime.Vector(point.Point.Position));
            if (!(screen.z >= near && screen.x >= 0 && screen.x <= Screen.width && screen.y >= 0 && screen.y <= Screen.height))
                continue;
            if (!Local(screen, out var local))
                continue;
            _markers.Add((local, point.Role, point.Point.Id == selected));
            var label = Label(labelIndex++);
            var caption = RouteVisuals.Label(point.Role, point.Number);
            if (label.text != caption)
                label.text = caption;
            label.color = RoleColor(point.Role);
            var rect = rectTransform.rect;
            // Keep captions inside the viewport, including markers at its right edge.
            label.rectTransform.anchoredPosition = new Vector2(
                Mathf.Clamp(local.x + 17, rect.xMin + 4, Mathf.Max(rect.xMin + 4, rect.xMax - 144)),
                Mathf.Clamp(local.y, rect.yMin + 12, Mathf.Max(rect.yMin + 12, rect.yMax - 12))
            );
            label.gameObject.SetActive(true);
        }
        for (var i = labelIndex; i < _labels.Count; i++)
            _labels[i].gameObject.SetActive(false);
        var extra = (_markers.Count + BatchSize - 1) / BatchSize;
        for (var i = 0; i < extra; i++)
        {
            if (i == _batches.Count)
            {
                var root = new GameObject("Route mesh batch", typeof(RectTransform), typeof(CanvasRenderer), typeof(RouteOverlayBatch));
                root.transform.SetParent(transform, false);
                root.transform.SetAsFirstSibling();
                var rect = (RectTransform)root.transform;
                rect.anchorMin = Vector2.zero;
                rect.anchorMax = Vector2.one;
                rect.offsetMin = rect.offsetMax = Vector2.zero;
                var batch = root.GetComponent<RouteOverlayBatch>();
                batch.Owner = this;
                batch.Index = i;
                batch.raycastTarget = false;
                _batches.Add(batch);
            }
            _batches[i].gameObject.SetActive(true);
            _batches[i].SetVerticesDirty();
        }
        for (var i = Math.Max(0, extra); i < _batches.Count; i++)
            _batches[i].gameObject.SetActive(false);
        SetVerticesDirty();
    }

    private bool Local(Vector2 screen, out Vector2 local) =>
        RectTransformUtility.ScreenPointToLocalPointInRectangle(rectTransform, screen, null, out local);

    private Text Label(int index)
    {
        if (index == _labels.Count)
        {
            var root = new GameObject("Route waypoint label", typeof(RectTransform), typeof(CanvasRenderer), typeof(Text), typeof(Outline));
            root.transform.SetParent(transform, false);
            var label = root.GetComponent<Text>();
            label.font = _font;
            label.fontSize = 14;
            label.fontStyle = FontStyle.Bold;
            label.alignment = TextAnchor.MiddleLeft;
            label.horizontalOverflow = HorizontalWrapMode.Overflow;
            label.verticalOverflow = VerticalWrapMode.Overflow;
            label.supportRichText = false;
            label.raycastTarget = false;
            label.rectTransform.anchorMin = label.rectTransform.anchorMax = new Vector2(.5f, .5f);
            label.rectTransform.pivot = new Vector2(0, .5f);
            label.rectTransform.sizeDelta = new Vector2(140, 24);
            var outline = root.GetComponent<Outline>();
            outline.effectColor = Color.black;
            outline.effectDistance = new Vector2(1, -1);
            _labels.Add(label);
        }
        return _labels[index];
    }

    // The parent draws all connections; its children draw only markers, so even
    // connections crossing a batch boundary remain beneath every waypoint.
    protected override void OnPopulateMesh(VertexHelper mesh) => Populate(mesh, -1);

    internal void Populate(VertexHelper mesh, int batch)
    {
        mesh.Clear();
        var pixel = 1 / Mathf.Max(.001f, canvas ? canvas.scaleFactor : 1);
        if (batch < 0)
        {
            // A valid layout has at most 2,000 points: both connection passes
            // together stay below 16,000 vertices in this parent mesh.
            foreach (var segment in _segments)
                Stroke(mesh, segment.A, segment.B, 8 * pixel, Dark);
            foreach (var segment in _segments)
                Stroke(mesh, segment.A, segment.B, 4 * pixel, new Color(.94f, .96f, 1));
            return;
        }
        var first = batch * BatchSize;
        for (var i = first; i < Math.Min(first + BatchSize, _markers.Count); i++)
        {
            var marker = _markers[i];
            Glyph(mesh, marker.Position, marker.Role, 9, Dark);
            if (marker.Selected)
                Glyph(mesh, marker.Position, marker.Role, 7, Color.white);
            Glyph(mesh, marker.Position, marker.Role, 3, RoleColor(marker.Role));
        }
    }

    private static void Glyph(VertexHelper mesh, Vector2 p, RouteRole role, float width, Color color)
    {
        void Edge(float ax, float ay, float bx, float by) => Stroke(mesh, p + new Vector2(ax, ay), p + new Vector2(bx, by), width, color);
        if (role == RouteRole.Start)
        {
            Edge(-7, -11, -7, 11);
            Edge(-7, 11, 10, 5);
            Edge(10, 5, -7, 0);
        }
        else if (role == RouteRole.Checkpoint)
        {
            Edge(0, 11, 10, 0);
            Edge(10, 0, 0, -11);
            Edge(0, -11, -10, 0);
            Edge(-10, 0, 0, 11);
        }
        else
        {
            Edge(-9, -9, -9, 9);
            Edge(-9, 9, 9, 9);
            Edge(9, 9, 9, -9);
            Edge(9, -9, -9, -9);
        }
    }

    private static void Stroke(VertexHelper mesh, Vector2 a, Vector2 b, float width, Color color)
    {
        var delta = b - a;
        if (delta.sqrMagnitude < .001f)
            return;
        var normal = new Vector2(-delta.y, delta.x).normalized * (width * .5f);
        var index = mesh.currentVertCount;
        mesh.AddVert(a - normal, color, Vector2.zero);
        mesh.AddVert(a + normal, color, Vector2.zero);
        mesh.AddVert(b + normal, color, Vector2.zero);
        mesh.AddVert(b - normal, color, Vector2.zero);
        mesh.AddTriangle(index, index + 1, index + 2);
        mesh.AddTriangle(index, index + 2, index + 3);
    }
}

// Keep each mesh below Unity UI's 65k vertex limit even for the largest valid layout.
internal sealed class RouteOverlayBatch : MaskableGraphic
{
    internal RouteOverlay Owner = null!;
    internal int Index;
    protected override void OnPopulateMesh(VertexHelper mesh)
    {
        if (Owner)
            Owner.Populate(mesh, Index);
        else
            mesh.Clear();
    }
}
