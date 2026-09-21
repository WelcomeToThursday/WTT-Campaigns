"""Incremental build, verification and deployment of the exported Scene catalog.

Receipts are local optimizations, never package contents. Only successful checks are
recorded. File identity/change time detects replacement and edits with restored mtimes.
Changed deployment files still use MSBuild's normal backup, hash and rollback path.
"""
import argparse
import ctypes
import hashlib
import importlib.metadata
import json
import os
import sys
import tempfile
from pathlib import Path


def sha(path):
    with path.open('rb') as stream:
        return hashlib.file_digest(stream, 'sha256').hexdigest().upper()


def digest(value):
    return hashlib.sha256(json.dumps(value, sort_keys=True).encode()).hexdigest()


if os.name == 'nt':
    from ctypes import wintypes

    class FileBasicInfo(ctypes.Structure):
        _fields_ = [(name, ctypes.c_longlong) for name in
                    ('CreationTime', 'LastAccessTime', 'LastWriteTime', 'ChangeTime')]
        _fields_ += [('FileAttributes', wintypes.DWORD)]

    kernel = ctypes.WinDLL('kernel32', use_last_error=True)
    kernel.CreateFileW.argtypes = (wintypes.LPCWSTR, wintypes.DWORD, wintypes.DWORD,
                                  ctypes.c_void_p, wintypes.DWORD, wintypes.DWORD, wintypes.HANDLE)
    kernel.CreateFileW.restype = wintypes.HANDLE
    kernel.GetFileInformationByHandleEx.argtypes = (wintypes.HANDLE, ctypes.c_int,
                                                   ctypes.c_void_p, wintypes.DWORD)
    kernel.GetFileInformationByHandleEx.restype = wintypes.BOOL
    kernel.CloseHandle.argtypes = (wintypes.HANDLE,)
    kernel.CloseHandle.restype = wintypes.BOOL


def signature(path):
    try:
        stat = path.stat()
        changed = stat.st_ctime_ns
        if os.name == 'nt':
            handle = kernel.CreateFileW(str(path), 0x80, 7, None, 3, 0, None)
            if handle == wintypes.HANDLE(-1).value:
                raise ctypes.WinError(ctypes.get_last_error())
            try:
                info = FileBasicInfo()
                if not kernel.GetFileInformationByHandleEx(handle, 0, ctypes.byref(info), ctypes.sizeof(info)):
                    raise ctypes.WinError(ctypes.get_last_error())
                changed = info.ChangeTime
            finally:
                kernel.CloseHandle(handle)
        return [stat.st_size, stat.st_mtime_ns, changed, stat.st_dev, stat.st_ino]
    except FileNotFoundError:
        return None


def inventory(root):
    return {path.name: signature(path) for path in sorted(root.iterdir())
            if path.name == 'catalog.json' or path.suffix == '.bundle'} if root.exists() else {}


def read(path):
    try:
        value = json.loads(path.read_text(encoding='utf-8'))
        return value if isinstance(value, dict) else {}
    except (OSError, ValueError):
        return {}


def write(path, value):
    path.parent.mkdir(parents=True, exist_ok=True)
    descriptor, temporary = tempfile.mkstemp(dir=path.parent, suffix='.tmp')
    try:
        with os.fdopen(descriptor, 'w', encoding='utf-8') as stream:
            json.dump(value, stream, separators=(',', ':'))
        os.replace(temporary, path)
    finally:
        Path(temporary).unlink(missing_ok=True)


def receipt_path(receipts, stage, *roots):
    return receipts / (stage + '-' + digest([str(p.resolve()).casefold() for p in roots]) + '.json')


def identity(exporter, game, stage):
    scripts = ('level_prop_workflow.py', 'export_level_props.py', 'level_prop_index.py',
               'export_container_library.py')
    return {'Schema': 1, 'Stage': stage, 'Game': str(game.resolve()),
            'Source': exporter.source_stamp(game), 'Exporter': exporter.tool_stamp(),
            'Checks': [sha(Path(__file__).with_name(name)) for name in scripts],
            'Runtime': [sys.version, importlib.metadata.version('UnityPy')]}


def catalog_hashes(root):
    catalog = json.loads((root / 'catalog.json').read_text(encoding='utf-8'))
    hashes = {'catalog.json': sha(root / 'catalog.json')}
    for info in catalog['Bundles'].values():
        name = info['File']
        if Path(name).name != name or '/' in name or '\\' in name or not name.endswith('.bundle') or name in hashes:
            raise ValueError('Invalid or duplicate level bundle filename: ' + name)
        hashes[name] = info['Sha256'].upper()
    return hashes


def intact(root, files, hashes, previous):
    # Previous hashes only bypass IO when both the expected hash and file identity match.
    for name, expected in hashes.items():
        current = files.get(name)
        if current is None:
            return False
        if (previous.get('Files', {}).get(name) == current
                and previous.get('Hashes', {}).get(name) == expected):
            continue
        if sha(root / name) != expected:
            return False
    return True


def build_library(exporter, game, root, index, receipts, rebuild=False):
    key = identity(exporter, game, 'build')
    path = receipt_path(receipts, 'build', root)
    previous = read(path)
    files = inventory(root)
    if not rebuild and previous.get('Identity') == key and previous.get('Files') == files and files:
        print('Scene catalog unchanged: build skipped.', flush=True)
        return
    exporter.build(game, root, index, rebuild=rebuild)
    files = inventory(root)
    hashes = catalog_hashes(root)
    if not intact(root, files, hashes, previous):
        print('Scene catalog output is missing or damaged; rebuilding.', flush=True)
        exporter.build(game, root, index, rebuild=True)
        files = inventory(root)
        hashes = catalog_hashes(root)
        if not intact(root, files, hashes, {}):
            raise ValueError('Rebuilt scene catalog failed its content hashes.')
    if inventory(root) != files or identity(exporter, game, 'build') != key:
        raise ValueError('Scene catalog inputs or outputs changed during build; retry.')
    write(path, {'Identity': key, 'Files': files, 'Hashes': hashes})
    print('Scene catalog build receipt saved.', flush=True)


def verify_library(exporter, game, root, receipts, force=False):
    key = identity(exporter, game, 'verify')
    path = receipt_path(receipts, 'verify', root)
    previous = read(path)
    files = inventory(root)
    if (not force and previous.get('Identity') == key and previous.get('Files') == files
            and files and previous.get('Hashes')):
        print('Scene catalog unchanged: reusing successful verification.', flush=True)
        return previous
    print('Scene catalog changed or has no verification receipt: running full verification.', flush=True)
    exporter.verify(root, game)
    hashes = catalog_hashes(root)
    if inventory(root) != files or identity(exporter, game, 'verify') != key:
        raise ValueError('Scene catalog inputs or outputs changed during verification; retry.')
    result = {'Identity': key, 'Files': files, 'Hashes': hashes}
    write(path, result)
    return result


def deployment(exporter, game, root, destination, receipts, unchanged=None, force=False, complete=False):
    # Validate owns forced structural verification; deployment's force flag only
    # bypasses destination receipts, avoiding a second full Unity object traversal.
    verified = verify_library(exporter, game, root, receipts)
    path = receipt_path(receipts, 'installed', root, destination)
    previous = read(path)
    old = previous.get('Files', {}) if previous.get('Identity') == verified['Identity'] else {}
    checked, skipped, changed = {}, [], []
    for name, expected in verified['Hashes'].items():
        target = destination / name
        before = signature(target)
        saved = old.get(name, {})
        valid = (not force and before is not None and saved.get('Signature') == before
                 and saved.get('Hash') == expected)
        if before is not None and not valid:
            valid = sha(target) == expected
        if signature(target) != before:
            raise ValueError('Installed scene catalog changed during inspection: ' + str(target))
        if valid:
            checked[name] = {'Signature': before, 'Hash': expected}
            skipped.append(str((root / name).resolve()))
        else:
            changed.append(name)
    if inventory(root) != verified['Files']:
        raise ValueError('Scene catalog source changed during deployment inspection; retry.')
    if complete and changed:
        raise ValueError('Installed scene catalog hash mismatch: ' + ', '.join(changed[:5]))
    # Recording already-verified unchanged files is safe even if the later install fails.
    # Missing/incorrect files are never recorded as successfully installed.
    write(path, {'Identity': verified['Identity'], 'Files': checked})
    if unchanged:
        unchanged.parent.mkdir(parents=True, exist_ok=True)
        unchanged.write_text('\n'.join(skipped), encoding='utf-8')
    print(f'Scene catalog: {len(skipped)} unchanged files; {len(changed)} files require installation.', flush=True)


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('stage', choices=('build', 'verify', 'plan', 'complete'))
    parser.add_argument('--game', type=Path, required=True)
    parser.add_argument('--output', type=Path, required=True)
    parser.add_argument('--receipts', type=Path, required=True)
    parser.add_argument('--index', type=Path)
    parser.add_argument('--destination', type=Path)
    parser.add_argument('--unchanged', type=Path)
    parser.add_argument('--rebuild', action='store_true')
    parser.add_argument('--force', action='store_true')
    args = parser.parse_args()
    import export_level_props as exporter
    if args.stage == 'build':
        if not args.index:
            parser.error('build requires --index')
        build_library(exporter, args.game, args.output, args.index, args.receipts, args.rebuild)
    elif args.stage == 'verify':
        verify_library(exporter, args.game, args.output, args.receipts, args.force)
    else:
        if not args.destination or (args.stage == 'plan' and not args.unchanged):
            parser.error('plan/complete require --destination; plan also requires --unchanged')
        deployment(exporter, args.game, args.output, args.destination, args.receipts,
                   args.unchanged, force=args.force, complete=args.stage == 'complete')


if __name__ == '__main__':
    main()
