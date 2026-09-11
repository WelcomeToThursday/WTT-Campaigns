"""Prepare SDK previews of compact candidates using the existing reviewed script identities.

The candidate's complete object set and payloads are retained, except MonoScript
identities needed by the SDK. Also checks untouched audio/resource files against
the currently finalized source bundles before any promotion or installation.
"""
import hashlib
import json
from pathlib import Path

import UnityPy

ROOT = Path(__file__).resolve().parents[1]
CANDIDATES = ROOT / 'artifacts/compact-traders'


def main():
    manifest = json.loads((CANDIDATES / 'traders.json').read_text())
    for room in manifest['rooms']:
        name = Path(room['bundle']).name
        print('Preparing SDK preview', name, flush=True)
        original = UnityPy.load(str(ROOT / 'Client/Resources/StoryMedia' / room['bundle']))
        resources = {n: hashlib.sha256(f.bytes).hexdigest() for n, f in original.file.files.items()
                     if not hasattr(f, 'objects') and not n.endswith('.resS')}
        del original
        sdk = UnityPy.load(str(ROOT / 'Research/Story/PreviewBundles' / name))
        scripts = {o.path_id: o.read_typetree() for o in sdk.objects if o.type.name == 'MonoScript'}
        del sdk
        env = UnityPy.load(str(CANDIDATES / room['bundle']))
        for n, sha in resources.items():
            if hashlib.sha256(env.file.files[n].bytes).hexdigest() != sha:
                raise ValueError('Audio/resource payload changed: ' + n)
        for obj in env.objects:
            if obj.type.name == 'MonoScript':
                original = scripts[obj.path_id]
                tree = obj.read_typetree()
                if original['m_Namespace'] == 'SeasonalPerks.UI.Media' and original['m_ClassName'] == 'StoryRoomLighting':
                    original['m_Namespace'] = 'WTT.Campaigns.UI.Media'
                if (tree['m_Namespace'], tree['m_ClassName']) != (original['m_Namespace'], original['m_ClassName']):
                    raise ValueError('SDK script identity mismatch')
                obj.save_typetree(original)
        output = ROOT / 'artifacts/compact-preview/PreviewBundles' / name
        output.parent.mkdir(parents=True, exist_ok=True)
        output.write_bytes(env.file.save(packer='lz4'))
        del env


if __name__ == '__main__':
    main()
