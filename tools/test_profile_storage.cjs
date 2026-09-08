const https = require('https'), zlib = require('zlib'), fs = require('fs'), path = require('path'), crypto = require('crypto'), assert = require('assert');
const server = path.resolve('Testing/TypedProfilesServer');
const op = () => crypto.randomBytes(16).toString('hex');
const read = file => JSON.parse(fs.readFileSync(path.join(server, file), 'utf8').replace(/^\uFEFF/,''));
const stateFile = 'Testing/typed-storage-state.json';
async function request(url, payload={}, session) {
 const body=zlib.deflateSync(Buffer.from(JSON.stringify(payload)));
 return new Promise((resolve,reject)=>{const req=https.request({hostname:'127.0.0.1',port:6988,path:url,method:'POST',rejectUnauthorized:false,headers:{'Content-Type':'application/json','Content-Length':body.length,...(session?{Cookie:'PHPSESSID='+session}:{})}},r=>{const chunks=[];r.on('data',b=>chunks.push(b));r.on('end',()=>{try{let data=Buffer.concat(chunks);try{data=zlib.inflateSync(data)}catch{} const value=JSON.parse(data);assert(!value.err,value.errmsg);resolve('err' in value?value.data:value)}catch(e){reject(e)}})});req.on('error',reject);req.end(body)});
}
let checks=0;
function check(ok,label){assert(ok,label);checks++;console.log('PASS '+label)}
async function main(){
 const mode=process.argv[2]||'create';
 let state;
 if(mode==='create'){
  const name='typed-storage-'+op().slice(0,10);
  const registration=await request('/launcher/v2/register',{username:name,edition:'Standard'});
  const root=registration.Profiles.find(p=>p.username===name).profileId;
  const custom=read('SPT_Data/database/templates/customization.json');
  const cosmetic=parent=>Object.entries(custom).find(([k,v])=>v._parent===parent&&v._props.AvailableAsDefault&&v._props.Side.includes('Usec'))[0];
  await request('/client/game/profile/create',{side:'Usec',nickname:'TypedNormal',headId:cosmetic('5cc085e214c02e000c6bea67'),voiceId:cosmetic('5fc100cf95572123ae738483')},root);
  const initial=await request('/wtt-seasonal/snapshot',{ProtocolVersion:2},root);
  const season=initial.Seasons.find(s=>s.Id==='69e232a764dfe95549003f0f')||initial.Seasons[0];
  const payload={ProtocolVersion:2,SeasonId:season.Id,OperationId:op(),Nickname:'TypedSeason',Side:'Usec',PerkIds:[]};
  const created=await request('/wtt-seasonal/create',payload,root);
  check(!created.Error,'Create: '+(created.Error||'seasonal character'));
  const child=created.SelectedCharacterId;
  check(child!==root,'Separate character identity');
  const replay=await request('/wtt-seasonal/create',payload,root);
  check(replay.SelectedCharacterId===child,'Creation retry preserves identity');
  state={root,child,season:season.Id,name};
  fs.writeFileSync(stateFile,JSON.stringify(state));
 } else state=JSON.parse(fs.readFileSync(stateFile));
 const {root,child}=state;
 const call=async(action,data={})=>{const r=await request('/wtt-seasonal/'+action,{ProtocolVersion:2,...data},root);check(!r.Error,action+': '+(r.Error||'succeeded'));return r;};
 const profile=path.join(server,'user/seasonal/profiles',child+'.json');
 check(fs.existsSync(profile),'Character file in separate seasonal folder');
 check(!fs.existsSync(path.join(server,'user/profiles',child+'.json')),'Character absent from launcher profile folder');
 check(fs.existsSync(path.join(server,'user/profiles',root+'.json')),'Account retained in original folder');
 const list=await request('/launcher/profiles');
 check(Array.isArray(list)&&list.some(p=>p.profileId===root)&&!list.some(p=>p.profileId===child),'Launcher lists account once and hides character');
 const selected=await call('switch',{Mode:'seasonal',CharacterId:child});
 check(selected.EffectiveProfileId===child,'Custom profile is loaded and switchable');
 const hub=await request('/wtt-seasonal/hub',{ProtocolVersion:2},child);
 check(!hub.Error&&hub.SeasonId===state.season,'Typed hub loads for character');
 const profiles=await request('/client/game/profile/list',{},child);
 check(profiles[0].Info.Nickname==='TypedSeason','Native profile route retains character data');
 await call('switch',{Mode:'normal'});
 if(mode==='cleanup'){
  await call('wipe',{CharacterId:child,OperationId:op()});
  check(!fs.existsSync(profile),'Wipe removes custom profile file');
  const recreated=await call('create',{CharacterId:child,SeasonId:state.season,OperationId:op(),Nickname:'TypedReborn',Side:'Usec',PerkIds:[]});
  check(recreated.SelectedCharacterId===child&&fs.existsSync(profile),'Recreation writes the same slot to custom folder');
  await call('switch',{Mode:'normal'});
  await call('delete',{CharacterId:child});
  check(!fs.existsSync(profile),'Delete removes custom profile file');
  check(fs.existsSync(path.join(server,'user/profiles',root+'.json')),'Delete preserves root account');
 }
 console.log('PASS '+checks+' storage checks ('+mode+')');
}
main().catch(e=>{console.error(e);process.exitCode=1});
