// Synthetic accounts only, on the dedicated BattlePassServer (6989).
const https = require('https'), zlib = require('zlib'), fs = require('fs'), path = require('path'), crypto = require('crypto'), assert = require('assert');
const server = path.resolve('Testing/BattlePassServer');
const read = file => JSON.parse(fs.readFileSync(path.join(server, file), 'utf8').replace(/^\uFEFF/, ''));
const op = () => crypto.randomBytes(16).toString('hex');
async function request(route, payload = {}, session) {
    const body = zlib.deflateSync(Buffer.from(JSON.stringify({ ProtocolVersion: 2, ...payload })));
    return new Promise((resolve, reject) => {
        const req = https.request({ hostname: '127.0.0.1', port: 6989, path: route, method: 'POST', rejectUnauthorized: false,
            headers: { 'Content-Type': 'application/json', 'Content-Length': body.length, ...(session ? { Cookie: 'PHPSESSID=' + session } : {}) } }, response => {
            const chunks = [];
            response.on('data', data => chunks.push(data));
            response.on('end', () => {
                try {
                    let data = Buffer.concat(chunks);
                    try { data = zlib.inflateSync(data); } catch {}
                    const result = JSON.parse(data);
                    assert(!result.err, result.errmsg);
                    resolve('err' in result ? result.data : result);
                } catch (error) { reject(error); }
            });
        });
        req.on('error', reject);
        req.end(body);
    });
}
let checks = 0;
function check(value, label) { assert(value, label); checks++; }
const rewards = hub => hub.Pages.map(page => page.Rewards.map(reward => reward.Id));
async function main() {
    let state;
    const stateFile = path.join(server, 'battlepass-fixture.json');
    if (process.argv[2] === 'restart') {
        state = JSON.parse(fs.readFileSync(stateFile));
    } else {
        const name = 'battlepass-test-' + op().slice(0, 8);
        const registered = await request('/launcher/v2/register', { username: name, edition: 'Standard' });
        const root = registered.Profiles.find(profile => profile.username === name).profileId;
        const custom = read('SPT_Data/database/templates/customization.json');
        const cosmetic = parent => Object.entries(custom).find(([, value]) => value._parent === parent && value._props.AvailableAsDefault && value._props.Side.includes('Usec'))[0];
        await request('/client/game/profile/create', { side: 'Usec', nickname: 'PassNormal', headId: cosmetic('5cc085e214c02e000c6bea67'), voiceId: cosmetic('5fc100cf95572123ae738483') }, root);
        const snapshot = await request('/wtt-seasonal/snapshot', {}, root);
        const legacy = snapshot.Seasons.find(season => season.Id === '69e232a764dfe95549003f0f');
        const story = snapshot.Seasons.find(season => season.Name === 'Story Sandbox');
        check(legacy && story, 'Both Season One and Story Sandbox are playable');
        state = { root, characters: [] };
        for (const [index, season] of [legacy, story, legacy].entries()) {
            const result = await request('/wtt-seasonal/create', { SeasonId: season.Id, OperationId: op(), Nickname: 'Pass' + index, Side: 'Usec', PerkIds: [] }, root);
            check(!result.Error, 'Character creation succeeds: ' + result.Error);
            state.characters.push({ id: result.SelectedCharacterId, season: season.Id });
        }
        fs.writeFileSync(stateFile, JSON.stringify(state));
    }
    const { root, characters } = state;
    const definitions = [read('user/mods/SeasonalPerks/creator/legacy.json')];
    for (const folder of fs.readdirSync(path.join(server, 'user/mods/SeasonalPerks/creator/packs'))) {
        definitions.push(read('user/mods/SeasonalPerks/creator/packs/' + folder + '/definition.json'));
    }
    for (const character of [...characters, ...characters].reverse()) {
        const switched = await request('/wtt-seasonal/switch', { Mode: 'seasonal', CharacterId: character.id }, root);
        check(!switched.Error && switched.EffectiveProfileId === character.id && switched.SeasonId === character.season, 'Switch selects the exact character and season');
        const expected = definitions.find(definition => definition.Id === character.season);
        for (const session of [root, character.id]) {
            const hub = await request('/wtt-seasonal/hub', { CharacterId: character.id, SeasonId: character.season }, session);
            check(!hub.Error && hub.SeasonId === character.season && hub.Id === expected.BattlePassId, 'Root and character requests return their own battlepass');
            assert.deepStrictEqual(rewards(hub), rewards(expected)); checks++;
            check(hub.SeasonName === expected.Name && hub.LegacyBranding === expected.Legacy, 'Battlepass title and branding match the character');
            const wrongSeason = characters.find(other => other.season !== character.season);
            check((await request('/wtt-seasonal/hub', { SeasonId: wrongSeason.season }, session)).Error, 'Mismatched season is rejected');
            const wrongCharacter = characters.find(other => other.id !== character.id);
            check((await request('/wtt-seasonal/hub', { SeasonId: character.season, CharacterId: wrongCharacter.id }, session)).Error, 'Mismatched character is rejected, including another character in the same season');
        }
        for (const previous of characters.filter(other => other.id !== character.id)) {
            check((await request('/wtt-seasonal/hub', {}, previous.id)).Error, 'Previous character session cannot load the new character battlepass');
        }
        const compatible = await request('/wtt-seasonal/hub', {}, root);
        check(!compatible.Error && compatible.SeasonId === character.season, 'Older root requests retain correct active-season behavior');
    }
    await request('/wtt-seasonal/switch', { Mode: 'normal' }, root);
    check((await request('/wtt-seasonal/hub', {}, root)).Error, 'Normal character cannot open a seasonal battlepass');
    console.log('PASS ' + checks + ' battlepass selection checks (' + (process.argv[2] || 'create') + ')');
}
main().catch(error => { console.error(error); process.exitCode = 1; });
