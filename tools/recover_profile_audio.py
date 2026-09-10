"""Recover live profile and perk feedback clips with their serialized bindings."""
import hashlib
import json
from pathlib import Path

import UnityPy
from recover_ui import Generator

DEV = Path(__file__).resolve().parents[2]
LIVE = Path('E:/EscapeFromTarkov')
OUT = DEV / 'CJ-SDK/Assets/Mods/WTT-Campaigns.Assets/Audio'


def main():
    source = LIVE / 'EscapeFromTarkov_Data/resources.assets'
    generator = Generator('2022.3.43f2')
    generator.load_il2cpp((LIVE / 'GameAssembly.dll').read_bytes(),
                         (DEV / '1.0 Metadata/global-metadata.dat').read_bytes())
    env = UnityPy.load(str(source))
    objects = next(f for f in env.files.values() if hasattr(f, 'objects')).objects
    wrapper = objects[46348].read_typetree(generator.get_nodes_up('Assembly-CSharp', 'UISoundsWrapper'))
    OUT.mkdir(parents=True, exist_ok=True)
    records = []
    source_hash = hashlib.sha256(source.read_bytes()).hexdigest()
    for sound_type, object_id, name in [(70, 4016, 'profile-hover-normal'), (82, 4241, 'profile-hover-seasonal'),
                                         (61, 4354, 'perk-off'), (62, 4004, 'perk-on'), (63, 3941, 'perk-reset'),
                                         (74, None, 'hub-click'), (75, None, 'hub-hover')]:
        binding = next(entry for entry in wrapper['_UIAudioClips'] if entry['_soundType'] == sound_type)
        if object_id is None:
            object_id = binding['_sound']['m_PathID']
        assert binding['_sound'] == {'m_FileID': 0, 'm_PathID': object_id}
        clip = objects[object_id].read()
        samples = clip.samples
        assert len(samples) == 1
        data = next(iter(samples.values()))
        assert data[:4] == b'RIFF' and data[8:12] == b'WAVE'
        output = OUT / (name + '.wav')
        output.write_bytes(data)
        records.append({'file': output.name, 'source': source.name, 'sourceSha256': source_hash,
                        'wrapperId': 46348, 'soundType': sound_type, 'objectId': object_id,
                        'name': clip.m_Name, 'duration': clip.m_Length,
                        'sha256': hashlib.sha256(data).hexdigest()})
        print(name, clip.m_Length, len(data))
    source = LIVE / 'EscapeFromTarkov_Data/sharedassets44.assets'
    env = UnityPy.load(str(source))
    objects = next(f for f in env.files.values() if hasattr(f, 'objects')).objects
    clip = objects[452].read()
    raw = next(iter(clip.samples.values()))
    (OUT / 'hub-hover-loop.wav').write_bytes(raw)
    records.append({'file': 'hub-hover-loop.wav', 'source': source.name, 'objectId': 452,
                    'name': clip.m_Name, 'duration': clip.m_Length, 'sourceSha256': hashlib.sha256(source.read_bytes()).hexdigest(),
                    'sha256': hashlib.sha256(raw).hexdigest(), 'volume': .3, 'fadeIn': .1, 'fadeOut': .3})
    (OUT / 'provenance.json').write_text(json.dumps(records, indent=2), encoding='utf-8')


if __name__ == '__main__':
    main()
