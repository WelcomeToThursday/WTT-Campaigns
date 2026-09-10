"""Seed a previously committed protocol-1 receipt only in the stopped isolated server."""
import argparse
import hashlib
import json
from test_integration import PROJECT, SERVER, request, check, checks


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument('phase', choices=['prepare', 'verify'])
    phase = parser.parse_args().phase
    context = json.loads((PROJECT / 'Testing/story-v2-upgrade.json').read_text())
    path = SERVER / 'user/seasonal/profiles' / (context['child'] + '.json')
    saved = json.loads(path.read_text(encoding='utf-8-sig'))
    assert saved['info']['username'].startswith('story-v2-'), 'Synthetic accounts only'
    source = context['request']
    legacy = {key: source.get(key, default) for key, default in {
        'Version': 1, 'SeasonId': '', 'CharacterId': '', 'OperationId': '', 'ExpectedRevision': 0,
        'ConversationId': '', 'Target': '', 'Kind': '', 'RaidId': '', 'ItemIds': [], 'ItemId': ''}.items()}
    legacy['Version'] = 1
    key = 'wttCampaignsStory:' + context['season']
    pmc = saved['characters']['pmc']
    story = json.loads(pmc[key]) if isinstance(pmc[key], str) else pmc[key]
    if phase == 'prepare':
        assert not (SERVER / 'test-server.pid').exists(), 'Stop the isolated server first'
        fingerprint = hashlib.sha256(json.dumps({'operation': 'start', 'request': legacy}, separators=(',', ':')).encode()).hexdigest().upper()
        story['Receipts'][legacy['OperationId']]['RequestHash'] = fingerprint
        pmc[key] = json.dumps(story, separators=(',', ':'))
        path.write_text(json.dumps(saved))
        print('Prepared committed legacy receipt in synthetic profile.')
        return
    before = request('/client/game/profile/list', session=context['child'])[0]
    result = request('/wtt-campaigns/story/start', legacy, context['child'])
    check(not result.get('Error') and result['Replayed'], 'A committed protocol-1 request replays after upgrade')
    check(result['NativeRevision'] == story['Receipts'][legacy['OperationId']]['Revision'], 'Legacy replay retains original native update revision')
    check(not result['Lines'] and not result['Presentation'], 'Legacy replay never repeats presentation')
    after = request('/client/game/profile/list', session=context['child'])[0]
    check(before == after, 'Legacy replay never repeats consumption or rewards')
    (PROJECT / 'Research/story-upgrade-checks.json').write_text(json.dumps({'passed': len(checks), 'checks': checks}, indent=2))
    print('Story upgrade:', len(checks), 'checks passed.')


if __name__ == '__main__':
    main()
