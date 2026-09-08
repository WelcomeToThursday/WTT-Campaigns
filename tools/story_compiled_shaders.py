"""Preserve native shader programs when Unity's YAML importer strips editor-only metadata."""
import copy
from pathlib import Path
import UnityPy

ROOT = Path(__file__).resolve().parents[1]
LIVE = Path('E:/EscapeFromTarkov/EscapeFromTarkov_Data')
LIVE_SHADERS = [
    ('sharedassets13.assets', 2740, 'Custom/Billboard_FogSheet_Simple'),
    ('sharedassets15.assets', 708, 'ANGRYMESH/PBR Rocks/PBR BlendTopDetail (Legacy)'),
    ('sharedassets5.assets', 4486, 'Cloth/ClothShader'),
    ('sharedassets639.assets', 94, 'Particles/VolumetricSmoke'),
    ('sharedassets161.assets', 2771, 'Characters/TraiderHair'),
]


class CompiledShaders:
    def __init__(self):
        self.environments = []
        self.sources = {}
        beta = UnityPy.load(str(ROOT.parent.parent / 'EscapeFromTarkov_Data/StreamingAssets/Windows/shaders'))
        self.environments.append(beta)
        for obj in beta.objects:
            if obj.type.name == 'Shader':
                self.sources[obj.read().m_ParsedForm.m_Name] = obj
        builtin = UnityPy.load(str(ROOT.parent.parent / 'EscapeFromTarkov_Data/Resources/unity_builtin_extra'))
        self.environments.append(builtin)
        for obj in builtin.objects:
            if obj.type.name == 'Shader':
                self.sources.setdefault(obj.read().m_ParsedForm.m_Name, obj)
        for filename, path_id, name in LIVE_SHADERS:
            env = UnityPy.load(str(LIVE / filename))
            self.environments.append(env)
            obj = next(o for o in env.objects if o.path_id == path_id)
            if obj.type.name != 'Shader' or obj.read().m_ParsedForm.m_Name != name:
                raise ValueError('Live shader identity changed: ' + name)
            if obj.assets_file.unity_version != '2022.3.43f2':
                raise ValueError('Review the shader layout before using another donor Unity version.')
            self.sources[name] = obj

    def restore(self, env):
        existing = {o.read().m_ParsedForm.m_Name: o for o in env.objects if o.type.name == 'Shader'}
        if not existing:
            raise ValueError('No shader objects survived the room build.')
        template = next(iter(existing.values()))
        pending = list(existing)
        restored = []
        for name in pending:
            source = self.sources.get(name)
            if source is None:
                if name in {'Skybox/Procedural', 'Unlit/Color'} and existing[name].read().compressedBlob:
                    # This built-in is compiled by the matching Unity 2022 editor.
                    restored.append(name)
                    continue
                raise ValueError('Unreviewed compiled shader: ' + name)
            tree = copy.deepcopy(source.read_typetree())
            # Runtime shaders have only these object references. New reference-bearing
            # fields must be reviewed before adding another Unity version.
            for pair in tree.get('m_NonModifiableTextures', []):
                if pair[1]['m_PathID']:
                    raise ValueError('Shader texture dependency needs packaging: ' + name)
            dependencies = []
            for pointer in source.read().m_Dependencies:
                if not pointer.m_PathID:
                    dependencies.append({'m_FileID': 0, 'm_PathID': 0})
                    continue
                dependency = pointer.deref()
                dependency_name = dependency.read().m_ParsedForm.m_Name
                self.sources.setdefault(dependency_name, dependency)
                if dependency_name not in existing:
                    clone = copy.copy(template)
                    clone.path_id = min(template.assets_file.objects) - 1
                    template.assets_file.objects[clone.path_id] = clone
                    existing[dependency_name] = clone
                    pending.append(dependency_name)
                dependencies.append({'m_FileID': 0, 'm_PathID': existing[dependency_name].path_id})
            tree['m_Dependencies'] = dependencies
            existing[name].save_typetree(tree)
            restored.append(name)
        return restored
