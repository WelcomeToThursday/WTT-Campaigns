using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using WTT.Campaigns.UI.Media;
using Object = UnityEngine.Object;

namespace WTT.Campaigns.Tools;

public static class CampaignsPeacekeeperBuilder
{
    public const string TraderId = "5935c25fb3acc3127c3d8cd9";
    private const string Assets = "Assets/Mods/WTT-Campaigns.Assets/";
    private const string Root = Assets + "StoryPeacekeeper/";

    public static GameObject Create()
    {
        var source = AssetDatabase.LoadAssetAtPath<GameObject>(Assets + "StoryTraders/54cb50c76803fa8b248b4571.prefab");
        var root = new GameObject("Campaign Peacekeeper supply office");
        root.SetActive(false);
        var original = source.GetComponentsInChildren<Transform>(true).Single(t => t.name == "Vendor_Prapor");
        var actor = Object.Instantiate(original.gameObject, root.transform);
        actor.name = "Peacekeeper custom actor";
        actor.transform.localPosition = Vector3.zero;
        actor.transform.localRotation = Quaternion.identity;
        foreach (var component in actor.GetComponentsInChildren<MonoBehaviour>(true))
        {
            if (component.GetType().Name is "uLipSyncBlendShape" or "LightKeeperEyesBlinking" or "LightKeeperEyeTargetFollower")
            {
                Object.DestroyImmediate(component);
                continue;
            }
            if (component.GetType().Name is "LipSyncDictionary" or "SecondaryAnimationDictionary")
            {
                var serialized = new SerializedObject(component);
                serialized.FindProperty("entries").ClearArray();
                serialized.ApplyModifiedPropertiesWithoutUndo();
            }
            if (component.GetType().Name == "AnimationDictionary")
            {
                var serialized = new SerializedObject(component);
                var entries = serialized.FindProperty("entries");
                for (var i = entries.arraySize - 1; i >= 0; i--)
                {
                    var entry = entries.GetArrayElementAtIndex(i);
                    var key = entry.FindPropertyRelative("key").stringValue;
                    if (key.StartsWith("Enterance", StringComparison.OrdinalIgnoreCase))
                    {
                        entries.DeleteArrayElementAtIndex(i);
                        continue;
                    }
                    entry.FindPropertyRelative("value").FindPropertyRelative("props").ClearArray();
                }
                serialized.ApplyModifiedPropertiesWithoutUndo();
            }
        }
        var head = actor.GetComponentsInChildren<SkinnedMeshRenderer>(true).Single(r => r.name == "Head_FacialAnim");
        head.sharedMesh = AssetDatabase.LoadAssetAtPath<Mesh>(Root + "Mesh/Head_BOSS_Glukhar_new_lod0.asset");
        head.sharedMaterials = new[] { AssetDatabase.LoadAssetAtPath<Material>(Root + "Material/Head_BOSS_Glukhar.mat") };
        head.localBounds = new Bounds(Vector3.zero, Vector3.one * 4);
        head.updateWhenOffscreen = true;
        if (!head.sharedMesh || head.bones.Length != head.sharedMesh.bindposes.Length)
        {
            throw new InvalidDataException("The custom head requires the three matching spine, neck and head bones.");
        }
        var body = actor.GetComponentsInChildren<SkinnedMeshRenderer>(true).Single(r => r.name == "Trader_Prapor_top_lod0");
        body.sharedMaterials = body.sharedMaterials.Select((m, i) => Tint(m, "Uniform" + i, new Color(.3f, .43f, .5f))).ToArray();
        foreach (
            var renderer in actor
                .GetComponentsInChildren<Renderer>(true)
                .Where(r => r != head && r != body && r.name != "Trader_Prapor_pants_lod0")
        )
        {
            var filter = renderer.GetComponent<MeshFilter>();
            if (filter)
            {
                Object.DestroyImmediate(filter);
            }
            Object.DestroyImmediate(renderer);
        }
        // Inventory equipment uses the same head pivot as the beta character rig.
        var glasses = new GameObject("Aviator glasses", typeof(MeshFilter), typeof(MeshRenderer));
        glasses.transform.SetParent(head.bones.Single(b => b.name == "Base HumanHead"), false);
        glasses.transform.localRotation = Quaternion.Euler(-90, 0, 0);
        glasses.GetComponent<MeshFilter>().sharedMesh = AssetDatabase.LoadAssetAtPath<Mesh>(
            Root + "Accessories/Mesh/item_equipment_glasses_aviator_LOD0_base.asset"
        );
        glasses.GetComponent<MeshRenderer>().sharedMaterials = new[]
        {
            AssetDatabase.LoadAssetAtPath<Material>(Root + "Accessories/Material/item_equipment_glasses_aviator.mat"),
            AssetDatabase.LoadAssetAtPath<Material>(Root + "Accessories/Material/item_equipment_glasses_aviator_glass.mat"),
        };
        var animator = actor.GetComponent<Animator>();
        animator.runtimeAnimatorController.animationClips.Single(c => c.name == "Prapor_idle").SampleAnimation(actor, 1);
        var bakedHead = new Mesh();
        head.BakeMesh(bakedHead);
        var vertices = bakedHead.vertices.Select(head.transform.TransformPoint).ToArray();
        var face = new Bounds(vertices[0], Vector3.zero);
        foreach (var vertex in vertices)
        {
            face.Encapsulate(vertex);
        }
        Object.DestroyImmediate(bakedHead);
        glasses.transform.rotation = Quaternion.Euler(0, 180, 0) * Quaternion.Euler(-90, 0, 0);
        var eyewear = glasses.GetComponent<MeshRenderer>();
        glasses.transform.localScale = Vector3.one * (face.size.x * .74f / eyewear.bounds.size.x);
        var glassMesh = glasses.GetComponent<MeshFilter>().sharedMesh;
        var lensVertices = glassMesh.GetTriangles(1).Distinct().Select(i => glassMesh.vertices[i]).ToArray();
        Vector3 LensCenter()
        {
            var bounds = new Bounds(glasses.transform.TransformPoint(lensVertices[0]), Vector3.zero);
            foreach (var vertex in lensVertices)
            {
                bounds.Encapsulate(glasses.transform.TransformPoint(vertex));
            }
            return bounds.center;
        }
        if (LensCenter().z < eyewear.bounds.center.z)
        {
            glasses.transform.rotation = Quaternion.Euler(0, 180, 0) * glasses.transform.rotation;
        }
        var fitted = LensCenter();
        glasses.transform.position += new Vector3(
            face.center.x - fitted.x,
            face.min.y + face.size.y * .52f - fitted.y,
            face.max.z + .008f - fitted.z
        );
        var concrete = Surface("Concrete", new Color(.32f, .35f, .34f));
        var blue = Surface("Office blue", new Color(.035f, .18f, .27f));
        var wood = Tint(
            source.GetComponentsInChildren<Renderer>(true).SelectMany(r => r.sharedMaterials).First(m => m && m.name == "steel_dye01"),
            "Desk steel",
            new Color(.35f, .38f, .37f)
        );
        Box(root, "Floor", new Vector3(0, -.08f, 0), new Vector3(7, .15f, 7), concrete);
        Box(root, "Rear wall", new Vector3(0, 1.6f, -2), new Vector3(7, 3.3f, .15f), concrete);
        Box(root, "Left wall", new Vector3(-3.4f, 1.6f, .8f), new Vector3(.15f, 3.3f, 5.5f), concrete);
        Box(root, "Right wall", new Vector3(3.4f, 1.6f, .8f), new Vector3(.15f, 3.3f, 5.5f), concrete);
        Box(root, "Ceiling", new Vector3(0, 3.2f, .8f), new Vector3(7, .15f, 5.5f), concrete);
        Box(root, "Desk", new Vector3(0, .73f, .65f), new Vector3(2.1f, .1f, .85f), wood);
        Box(root, "Desk front", new Vector3(0, .36f, 1.02f), new Vector3(2.1f, .7f, .08f), blue);
        Box(root, "Notice board", new Vector3(.25f, 1.8f, -1.9f), new Vector3(1.8f, .95f, .05f), blue);
        var sign = new GameObject("Supply office sign", typeof(TextMesh)).GetComponent<TextMesh>();
        sign.transform.SetParent(root.transform, false);
        sign.transform.localPosition = new Vector3(.25f, 2.03f, -1.86f);
        sign.transform.localRotation = Quaternion.Euler(0, 180, 0);
        sign.text = "UN\nLOGISTICS OFFICE";
        sign.anchor = TextAnchor.MiddleCenter;
        sign.alignment = TextAlignment.Center;
        sign.fontSize = 64;
        sign.characterSize = .015f;
        sign.color = new Color(.86f, .9f, .91f);
        Prop(source, root, "Office_Chair_02_LOD0", new Vector3(0, 0, -.3f), Quaternion.identity);
        for (var i = 0; i < 6; i++)
        {
            Prop(
                source,
                root,
                "Mllitary_box_wooden_01_LOD0",
                new Vector3(-2.2f + i % 2 * .75f, .18f + i / 2 * .38f, -1.2f),
                Quaternion.Euler(0, 4 * i, 0)
            );
        }
        Prop(source, root, "Table_08_LOD0", new Vector3(2.25f, 0, -1), Quaternion.Euler(0, 90, 0));
        var camera = new GameObject("StoryCamera", typeof(Camera)).GetComponent<Camera>();
        camera.transform.SetParent(root.transform, false);
        camera.transform.localPosition = new Vector3(0, 1.15f, 2.05f);
        camera.transform.localRotation = Quaternion.Euler(0, 180, 0);
        camera.fieldOfView = 50;
        camera.nearClipPlane = .03f;
        camera.farClipPlane = 25;
        camera.clearFlags = CameraClearFlags.SolidColor;
        camera.backgroundColor = new Color(.06f, .08f, .09f);
        camera.renderingPath = RenderingPath.DeferredShading;
        camera.enabled = false;
        Light(root, "Ceiling lamp", new Vector3(-1.4f, 2.7f, .6f), new Color(.9f, .95f, 1), 1.2f, 6);
        Light(root, "Desk lamp", new Vector3(1.2f, 1.7f, .8f), new Color(1, .81f, .6f), .8f, 4);
        var lighting = root.AddComponent<StoryRoomLighting>();
        lighting.Environment = new StoryEnvironmentState
        {
            AmbientMode = AmbientMode.Trilight,
            AmbientSky = new Color(.2f, .23f, .25f),
            AmbientEquator = new Color(.13f, .15f, .16f),
            AmbientGround = new Color(.06f, .07f, .08f),
            AmbientIntensity = 1,
        };
        foreach (var collider in root.GetComponentsInChildren<Collider>(true))
        {
            Object.DestroyImmediate(collider);
        }
        return root;
    }

    private static Material Surface(string name, Color color)
    {
        var source = AssetDatabase.LoadAssetAtPath<Material>(Assets + "StoryRecovered/Material/City_wall_plaster_white_01.mat");
        return Tint(source, name, color);
    }

    private static Material Tint(Material source, string name, Color color)
    {
        return Save(new Material(source) { name = name, color = color }, name);
    }

    private static Material Save(Material material, string name)
    {
        var path = Root + "Material/" + name + ".mat";
        var existing = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (existing)
        {
            EditorUtility.CopySerialized(material, existing);
            Object.DestroyImmediate(material);
            return existing;
        }
        AssetDatabase.CreateAsset(material, path);
        return material;
    }

    private static void Box(GameObject root, string name, Vector3 position, Vector3 size, Material material)
    {
        var box = GameObject.CreatePrimitive(PrimitiveType.Cube);
        box.name = name;
        box.transform.SetParent(root.transform, false);
        box.transform.localPosition = position;
        box.transform.localScale = size;
        box.GetComponent<Renderer>().sharedMaterial = material;
    }

    private static void Prop(GameObject source, GameObject root, string name, Vector3 position, Quaternion rotation)
    {
        var original = source.GetComponentsInChildren<MeshRenderer>(true).FirstOrDefault(r => r.name == name);
        if (!original)
        {
            throw new InvalidDataException("Missing reviewed office prop: " + name);
        }
        var prop = new GameObject(name, typeof(MeshFilter), typeof(MeshRenderer));
        prop.transform.SetParent(root.transform, false);
        prop.transform.localPosition = position;
        prop.transform.localRotation = rotation * original.transform.rotation;
        prop.transform.localScale = original.transform.lossyScale;
        prop.GetComponent<MeshFilter>().sharedMesh = original.GetComponent<MeshFilter>().sharedMesh;
        prop.GetComponent<MeshRenderer>().sharedMaterials = original.sharedMaterials;
    }

    private static void Light(GameObject root, string name, Vector3 position, Color color, float intensity, float range)
    {
        var light = new GameObject(name, typeof(Light)).GetComponent<Light>();
        light.transform.SetParent(root.transform, false);
        light.transform.localPosition = position;
        light.type = LightType.Point;
        light.color = color;
        light.intensity = intensity;
        light.range = range;
        light.shadows = LightShadows.Soft;
    }
}
