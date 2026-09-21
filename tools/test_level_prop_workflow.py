"""File-only regressions for Scene catalog incremental build/verification/install."""
import json
import os
import shutil
import subprocess
import sys
import tempfile
import unittest
from pathlib import Path
from unittest.mock import Mock, patch

import level_prop_workflow as workflow


class WorkflowTests(unittest.TestCase):
    def setUp(self):
        self.temporary = tempfile.TemporaryDirectory()
        self.addCleanup(self.temporary.cleanup)
        self.base = Path(self.temporary.name)
        self.source = self.base / 'source'
        self.destination = self.base / 'installed'
        self.receipts = self.base / 'receipts'
        self.source.mkdir()
        self.destination.mkdir()
        self.original = b'original bundle'
        self.write_library()
        self.key = {'Source': 'game-v1', 'Verifier': 'v1'}
        self.identity = patch.object(workflow, 'identity', side_effect=lambda *args: dict(self.key))
        self.identity.start()
        self.addCleanup(self.identity.stop)
        self.exporter = Mock()
        self.exporter.verify.side_effect = self.check_library
        self.exporter.build.side_effect = self.build_library

    def write_library(self):
        (self.source / 'prop.bundle').write_bytes(self.original)
        (self.source / 'catalog.json').write_text(json.dumps({'Bundles': {
            'prop': {'File': 'prop.bundle', 'Sha256': workflow.sha(self.source / 'prop.bundle')}}}))

    def build_library(self, game, root, index, rebuild=False):
        if rebuild:
            self.write_library()

    def check_library(self, root, game):
        for name, expected in workflow.catalog_hashes(root).items():
            if workflow.sha(root / name) != expected:
                raise ValueError('bad hash')

    def verify(self, **kwargs):
        return workflow.verify_library(self.exporter, self.base, self.source, self.receipts, **kwargs)

    def build(self, **kwargs):
        workflow.build_library(self.exporter, self.base, self.source, self.base / 'index', self.receipts, **kwargs)

    def deploy(self, **kwargs):
        workflow.deployment(self.exporter, self.base, self.source, self.destination, self.receipts,
                            unchanged=self.base / 'unchanged.txt', **kwargs)
        return (self.base / 'unchanged.txt').read_text().splitlines()

    def install_files(self):
        for path in self.source.iterdir():
            shutil.copy2(path, self.destination / path.name)

    def test_unchanged_build_and_verification_do_no_content_io(self):
        self.build()
        self.verify()
        with patch.object(workflow, 'sha', side_effect=AssertionError('Unexpected content read')):
            self.build()
            self.verify()
        self.assertEqual(self.exporter.build.call_count, 1)
        self.assertEqual(self.exporter.verify.call_count, 1)

    def test_game_and_verifier_changes_and_force_revalidate(self):
        self.verify()
        self.key['Source'] = 'game-v2'
        self.verify()
        self.key['Verifier'] = 'v2'
        self.verify()
        self.verify(force=True)
        self.assertEqual(self.exporter.verify.call_count, 4)

    def test_missing_and_damaged_build_outputs_are_regenerated(self):
        self.build()
        (self.source / 'prop.bundle').unlink()
        self.build()
        self.assertTrue(self.exporter.build.call_args.kwargs['rebuild'])
        (self.source / 'prop.bundle').write_bytes(b'damaged bundle!')
        self.build()
        self.assertEqual((self.source / 'prop.bundle').read_bytes(), self.original)

    def test_rebuild_flag_bypasses_receipt(self):
        self.build()
        self.build(rebuild=True)
        self.assertTrue(self.exporter.build.call_args.kwargs['rebuild'])

    def test_same_size_edit_with_restored_mtime_invalidates_verification(self):
        self.verify()
        bundle = self.source / 'prop.bundle'
        previous = bundle.stat()
        bundle.write_bytes(b'x' * len(self.original))
        os.utime(bundle, ns=(previous.st_atime_ns, previous.st_mtime_ns))
        with self.assertRaisesRegex(ValueError, 'bad hash'):
            self.verify()
        self.assertEqual(self.exporter.verify.call_count, 2)

    def test_failed_verification_is_not_cached(self):
        self.exporter.verify.side_effect = ValueError('failure')
        for _ in range(2):
            with self.assertRaisesRegex(ValueError, 'failure'):
                self.verify()
        self.assertFalse(list(self.receipts.glob('verify-*.json')))

    def test_concurrent_mutation_does_not_publish_receipt(self):
        self.exporter.verify.side_effect = lambda *args: (self.source / 'prop.bundle').write_bytes(b'new')
        with self.assertRaisesRegex(ValueError, 'changed during verification'):
            self.verify()
        self.assertFalse(list(self.receipts.glob('verify-*.json')))

    def test_malformed_receipt_requires_full_verification(self):
        self.verify()
        next(self.receipts.glob('verify-*.json')).write_text('{broken')
        self.verify()
        self.assertEqual(self.exporter.verify.call_count, 2)

    def test_receipts_are_specific_to_output_directory(self):
        self.verify()
        other = self.base / 'other'
        shutil.copytree(self.source, other)
        workflow.verify_library(self.exporter, self.base, other, self.receipts)
        self.assertEqual(self.exporter.verify.call_count, 2)

    def test_warm_deployment_does_not_hash_catalog_or_bundles(self):
        self.install_files()
        self.assertEqual(len(self.deploy()), 2)
        with patch.object(workflow, 'sha', side_effect=AssertionError('Unexpected content read')):
            self.assertEqual(len(self.deploy()), 2)

    def test_only_changed_destinations_require_install_and_failed_install_retries(self):
        self.install_files()
        self.deploy()
        (self.destination / 'prop.bundle').write_bytes(b'broken')
        for _ in range(2):
            self.assertEqual(self.deploy(), [str(self.source / 'catalog.json')])
        with self.assertRaisesRegex(ValueError, 'hash mismatch'):
            self.deploy(complete=True)
        shutil.copy2(self.source / 'prop.bundle', self.destination / 'prop.bundle')
        self.assertEqual(len(self.deploy(complete=True)), 2)

    def test_missing_installed_files_and_new_destination(self):
        self.install_files()
        self.deploy()
        (self.destination / 'catalog.json').unlink()
        self.assertEqual(self.deploy(), [str(self.source / 'prop.bundle')])
        self.destination = self.base / 'another-install'
        self.assertEqual(self.deploy(), [])

    def test_force_deployment_hashes_unchanged_destinations(self):
        self.install_files()
        self.deploy()
        with patch.object(workflow, 'sha', wraps=workflow.sha) as sha:
            self.deploy(force=True)
            self.assertEqual({call.args[0] for call in sha.call_args_list},
                             {self.destination / 'catalog.json', self.destination / 'prop.bundle'})
        self.assertEqual(self.exporter.verify.call_count, 1)

    def test_touch_without_content_change_does_not_rebuild_or_reinstall(self):
        self.build()
        self.install_files()
        self.deploy()
        (self.source / 'prop.bundle').touch()
        self.build()
        self.assertFalse(self.exporter.build.call_args.kwargs['rebuild'])
        (self.destination / 'prop.bundle').touch()
        self.assertEqual(len(self.deploy()), 2)

    def test_catalog_paths_cannot_escape_library(self):
        (self.source / 'catalog.json').write_text(json.dumps({'Bundles': {
            'bad': {'File': '../outside.bundle', 'Sha256': 'bad'}}}))
        with self.assertRaisesRegex(ValueError, 'Invalid'):
            workflow.catalog_hashes(self.source)


class MSBuildWorkflowTests(unittest.TestCase):
    def test_install_filter_repair_backups_and_complete_package(self):
        # A file-only installation fixture: no game/server executable or profile exists.
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            scripts = root / 'tools'
            scripts.mkdir()
            for name in ('Deployment.targets', 'CampaignsMigration.targets', 'level_prop_workflow.py'):
                shutil.copy2(Path(__file__).with_name(name), scripts / name)
            for name in ('level_prop_index.py', 'export_container_library.py'):
                (scripts / name).touch()
            (scripts / 'export_level_props.py').write_text('''
import json
from level_prop_workflow import sha
def source_stamp(game): return 'fixture-game'
def tool_stamp(): return 'fixture-exporter'
def verify(root, game):
    for info in json.loads((root / 'catalog.json').read_text())['Bundles'].values():
        assert sha(root / info['File']) == info['Sha256']
''')
            source = root / 'source/LevelPropLibrary'
            destination = root / 'client/LevelPropLibrary'
            source.mkdir(parents=True)
            (source / 'prop.bundle').write_bytes(b'validated synthetic asset')
            (source / 'catalog.json').write_text(json.dumps({'Bundles': {'prop': {
                'File': 'prop.bundle', 'Sha256': workflow.sha(source / 'prop.bundle')}}}))
            shutil.copytree(source, destination)
            (root / 'source/WTT-Campaigns.Client.dll').write_bytes(b'synthetic runtime payload')
            shutil.copy2(root / 'source/WTT-Campaigns.Client.dll', root / 'client/WTT-Campaigns.Client.dll')
            (root / 'fixture.proj').write_text('''<Project>
  <PropertyGroup>
    <DeploymentScope>Client</DeploymentScope>
    <CampaignsClientDir>$(MSBuildProjectDirectory)/client/</CampaignsClientDir>
    <CampaignsServerDir>$(MSBuildProjectDirectory)/server/</CampaignsServerDir>
    <CampaignsBackupDir>$(MSBuildProjectDirectory)/backups/</CampaignsBackupDir>
    <TarkovDir>$(MSBuildProjectDirectory)/game/</TarkovDir>
    <LevelPropReceiptsDir>$(MSBuildProjectDirectory)/receipts</LevelPropReceiptsDir>
  </PropertyGroup>
  <ItemGroup>
    <CampaignsDeployFile Include="$(MSBuildProjectDirectory)/source/LevelPropLibrary/catalog.json">
      <InstallPath>client\\LevelPropLibrary\\catalog.json</InstallPath>
    </CampaignsDeployFile>
    <CampaignsDeployFile Include="$(MSBuildProjectDirectory)/source/LevelPropLibrary/prop.bundle">
      <InstallPath>client\\LevelPropLibrary\\prop.bundle</InstallPath>
    </CampaignsDeployFile>
    <CampaignsDeployFile Include="$(MSBuildProjectDirectory)/source/WTT-Campaigns.Client.dll">
      <InstallPath>client\\WTT-Campaigns.Client.dll</InstallPath>
    </CampaignsDeployFile>
  </ItemGroup>
  <Import Project="tools/Deployment.targets" />
  <Target Name="Record" AfterTargets="PrepareCampaignsFiles">
    <WriteLinesToFile File="planned.txt" Lines="@(_PlannedFiles->'%(Filename)%(Extension)')" Overwrite="true" />
  </Target>
  <Target Name="Stage" DependsOnTargets="PrepareCampaignsFiles">
    <Copy SourceFiles="@(_PlannedFiles)" DestinationFiles="@(_PlannedFiles->'$(PackageDir)/%(PackagePath)')" />
    <VerifyFileHash File="$(PackageDir)/%(_PlannedFiles.PackagePath)" Hash="%(_PlannedFiles.FileHash)" />
  </Target>
</Project>''')

            def run(target, *arguments):
                result = subprocess.run(['dotnet', 'msbuild', 'fixture.proj', '-nologo', '-t:' + target,
                                         '-p:CampaignsPython=' + sys.executable, *arguments], cwd=root,
                                        capture_output=True, text=True, timeout=60)
                self.assertEqual(result.returncode, 0, result.stdout + result.stderr)
                return (root / 'planned.txt').read_text(encoding='utf-8-sig').splitlines()

            self.assertEqual(run('DeployCampaignsFiles'), ['WTT-Campaigns.Client.dll'])
            self.assertEqual(run('DeployCampaignsFiles'), ['WTT-Campaigns.Client.dll'])
            (destination / 'prop.bundle').write_bytes(b'old damaged version')
            self.assertEqual(run('DeployCampaignsFiles'), ['prop.bundle', 'WTT-Campaigns.Client.dll'])
            self.assertEqual((destination / 'prop.bundle').read_bytes(), (source / 'prop.bundle').read_bytes())
            backups = list((root / 'backups').glob('*/client/LevelPropLibrary/prop.bundle'))
            self.assertEqual(len(backups), 1)
            self.assertEqual(backups[0].read_bytes(), b'old damaged version')
            self.assertEqual(set(run('Stage', '-p:PackageDir=' + str(root / 'package'))),
                             {'catalog.json', 'prop.bundle', 'WTT-Campaigns.Client.dll'})
            for name in ('catalog.json', 'prop.bundle'):
                self.assertEqual((root / 'package/BepInEx/plugins/WTT-Campaigns/LevelPropLibrary' / name).read_bytes(),
                                 (source / name).read_bytes())


if __name__ == '__main__':
    unittest.main()
