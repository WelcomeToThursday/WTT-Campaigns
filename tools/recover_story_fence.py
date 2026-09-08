"""Adapt Fence's audio-only greeting data to beta's base uLipSync format."""
import hashlib
import json
import re
from pathlib import Path
import UnityPy

ROOT = Path(__file__).resolve().parents[1]
SDK = ROOT.parent / 'CJ-SDK'
TARGET = SDK / 'Assets/Mods/SeasonalPerks.Assets/StoryRecovered'


def recover():
    source = Path('E:/EscapeFromTarkov/EscapeFromTarkov_Data/StreamingAssets/Windows/assets/scripts/dialogsystem/traderlipsyncscriptableobject.bundle')
    index = json.loads((ROOT / 'Research/Story/trader-export-index.json').read_text())['assets']
    scene = (TARGET / 'Content/Locations/Venders/Vendors_Fence.unity').read_text()
    guids = set(re.findall(r'bakedData: \{fileID: 11400000, guid: ([0-9a-f]{32})', scene))
    needed = {}
    for guid in guids:
        original = Path(index[guid])
        name = re.search(r'^  m_Name: (.+)$', original.read_text(), re.M).group(1)
        if not re.fullmatch(r'skupschik_(?:goodbye|greetings|greetings_while_work|start_trade)_\d+', name):
            raise ValueError('Fence recovery only accepts generic visit speech: ' + name)
        needed[name] = TARGET / 'MonoBehaviour' / original.name
    env = UnityPy.load(str(source))
    outputs = []
    for obj in env.objects:
        if obj.type.name != 'MonoBehaviour' or obj.read().m_Name not in needed:
            continue
        tree = obj.read_typetree()
        name = tree['m_Name']
        clip = obj.assets_file.objects[tree['audioClip']['m_PathID']].read()
        samples = clip.samples
        if len(samples) != 1 or not next(iter(samples)).endswith('.wav'):
            raise ValueError('Expected one decoded generic Fence recording.')
        audio = TARGET / 'AudioClip' / ('StoryFence_' + name + '.wav')
        audio.parent.mkdir(parents=True, exist_ok=True)
        audio.write_bytes(next(iter(samples.values())))
        audio_guid = hashlib.sha256(('Seasonal Fence audio:' + name).encode()).hexdigest()[:32]
        Path(str(audio) + '.meta').write_text('fileFormatVersion: 2\nguid: ' + audio_guid + '\nAudioImporter:\n  externalObjects: {}\n  serializedVersion: 7\n  defaultSettings:\n    serializedVersion: 2\n    loadType: 0\n    sampleRateSetting: 0\n    sampleRateOverride: 44100\n    compressionFormat: 1\n    quality: 1\n    conversionMode: 0\n  platformSettingOverrides: {}\n  forceToMono: 0\n  normalize: 1\n  preloadAudioData: 1\n  loadInBackground: 0\n  ambisonic: 0\n  3D: 0\n  userData:\n  assetBundleName:\n  assetBundleVariant:\n')
        data = '%YAML 1.1\n%TAG !u! tag:unity3d.com,2011:\n--- !u!114 &11400000\nMonoBehaviour:\n  m_ObjectHideFlags: 0\n  m_CorrespondingSourceObject: {fileID: 0}\n  m_PrefabInstance: {fileID: 0}\n  m_PrefabAsset: {fileID: 0}\n  m_GameObject: {fileID: 0}\n  m_Enabled: 1\n  m_EditorHideFlags: 0\n  m_Script: {fileID: 1483997362, guid: 3457008243cbb8a49b32b4ef95260ef1, type: 3}\n  m_Name: ' + name + '\n  m_EditorClassIdentifier:\n  profile: {fileID: 0}\n  bakedProfile: {fileID: 0}\n'
        for field in ('audioClip', 'bakedAudioClip'):
            data += '  ' + field + ': {fileID: 8300000, guid: ' + audio_guid + ', type: 3}\n'
        data += '  duration: ' + str(tree['duration']) + '\n  frames:\n'
        for frame in tree['frames']:
            if frame['phonemes']:
                raise ValueError('Review phoneme-bearing data before applying the audio-only adapter.')
            data += '  - volume: ' + str(frame['volume']) + '\n    phonemes: []\n'
        output = needed.pop(name)
        output.write_text(data)
        outputs.extend([output, audio])
    if needed:
        raise ValueError('Missing Fence speech data: ' + ', '.join(needed))
    manifest_path = ROOT / 'Research/Story/trader-import.json'
    manifest = json.loads(manifest_path.read_text())
    assets = {entry['asset']: entry for entry in manifest['assets']}
    for output in outputs:
        name = str(output.relative_to(SDK)).replace('\\', '/')
        assets[name] = {'asset': name, 'sha256': hashlib.sha256(output.read_bytes()).hexdigest()}
    manifest['assets'] = list(assets.values())
    manifest['fenceAudioAdapter'] = {'recordings': len(outputs) // 2, 'source': str(source)}
    manifest_path.write_text(json.dumps(manifest, indent=2))
    print('Recovered', len(outputs) // 2, 'generic Fence recordings for beta.', flush=True)


if __name__ == '__main__':
    recover()
