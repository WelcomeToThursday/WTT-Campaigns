"""Finalize the synthetic cinematic using the same beta shader and script identities as trader rooms."""
from pathlib import Path
import hashlib
import json
import UnityPy
from story_compiled_shaders import CompiledShaders

ROOT = Path(__file__).resolve().parents[1]
source = ROOT / 'Research/Story/ExampleBundles/examples/story-test.bundle'
output = ROOT / 'Client/Resources/StoryMedia/examples/story-test.bundle'
env = UnityPy.load(str(source))
shaders = CompiledShaders().restore(env)
native = UnityPy.load(str(ROOT.parent.parent / 'EscapeFromTarkov_Data/globalgamemanagers.assets'))
scripts = {}
for obj in native.objects:
    if obj.type.name == 'MonoScript':
        tree = obj.read_typetree()
        scripts[(tree['m_Namespace'], tree['m_ClassName'])] = tree
mapped = []
for obj in env.objects:
    if obj.type.name == 'MonoScript':
        tree = obj.read_typetree()
        key = (tree['m_Namespace'], tree['m_ClassName'])
        if key not in scripts:
            raise ValueError('Synthetic cinematic uses a non-native script: ' + str(key))
        tree['m_AssemblyName'] = scripts[key]['m_AssemblyName']
        tree['m_PropertiesHash'] = scripts[key]['m_PropertiesHash']
        obj.save_typetree(tree)
        mapped.append('.'.join(key))
output.parent.mkdir(parents=True, exist_ok=True)
output.write_bytes(next(iter(env.files.values())).save(packer='lz4'))
record = {'bundle': 'examples/story-test.bundle', 'asset': 'assets/story-test.prefab', 'sha256': hashlib.sha256(output.read_bytes()).hexdigest(), 'shaders': shaders, 'scripts': mapped, 'synthetic': True, 'duration': 5}
(output.parent / 'story-test.json').write_text(json.dumps(record, indent=2) + '\n')
print(json.dumps(record))
