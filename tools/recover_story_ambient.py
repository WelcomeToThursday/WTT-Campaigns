"""Preserve the ambient SH coefficients omitted from the initial room prefabs."""
import hashlib
import json
import re
from pathlib import Path

import UnityPy

ROOT = Path(__file__).resolve().parents[1]
LIVE = Path('E:/EscapeFromTarkov/EscapeFromTarkov_Data')
ROOMS = {
    638: '579dc571d53a0658a154fbec', 639: '5c0647fdd443bc2504c2d371',
    640: '5a7c2eca46aef81a7ca2145d', 642: '54cb50c76803fa8b248b4571',
    643: '5ac3b934156ae10c4430e83c', 644: '58330581ace78e27b8b10cee',
    645: '54cb57776803fa99248b456e',
}


def main():
    rows, audit = [], []
    for level, trader in ROOMS.items():
        path = LIVE / ('level' + str(level))
        env = UnityPy.load(str(path))
        settings = next(o for o in env.objects if o.type.name == 'RenderSettings').read_typetree()
        coefficients = settings['m_AmbientProbe']
        ordered = sorted(coefficients.items(), key=lambda pair: int(re.search(r'\d+', pair[0]).group()))
        if len(ordered) != 27:
            raise ValueError('Review changed ambient probe format: ' + str(path))
        rows.append(trader + ' ' + ' '.join(format(value, '.9g') for _, value in ordered))
        audit.append({'trader': trader, 'source': str(path),
                      'sha256': hashlib.sha256(path.read_bytes()).hexdigest(),
                      'property': 'RenderSettings.m_AmbientProbe'})
    output = ROOT / 'UI/Resources/Story'
    output.mkdir(parents=True, exist_ok=True)
    (output / 'ambient-probes.txt').write_text('\n'.join(rows) + '\n', encoding='utf-8')
    (output / 'ambient-probes.provenance.json').write_text(json.dumps(audit, indent=2), encoding='utf-8')


if __name__ == '__main__':
    main()
