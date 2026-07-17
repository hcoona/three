'use strict';
const fs = require('fs');
const path = require('path');
const crypto = require('crypto');
const root = __dirname;
const LZString = require(path.join(root, 'www', 'js', 'libs', 'lz-string.js'));
const decodedDir = path.join(root, 'decoded');
fs.mkdirSync(decodedDir, { recursive: true });

const sha256 = s => crypto.createHash('sha256').update(s).digest('hex').toUpperCase();
const own = (o, k) => Object.prototype.hasOwnProperty.call(o, k);
function normalize(value, stats) {
  if (value === null || typeof value !== 'object') return value;
  if (Array.isArray(value)) return value.map(v => normalize(v, stats));
  if (own(value, '@r')) {
    stats.references++;
    return { __referenceId: value['@r'] };
  }
  if (own(value, '@a')) {
    stats.arrayWrappers++;
    return normalize(value['@a'], stats);
  }
  const output = {};
  if (own(value, '@')) {
    const name = value['@'];
    stats.classes[name] = (stats.classes[name] || 0) + 1;
    output.__class = name;
  }
  if (own(value, '@c')) stats.objectIds++;
  for (const [key, child] of Object.entries(value)) {
    if (key === '@' || key === '@c') continue;
    output[key] = normalize(child, stats);
  }
  return output;
}
function unwrap(value) {
  if (value === null || typeof value !== 'object') return value;
  if (Array.isArray(value)) return value.map(unwrap);
  if (own(value, '@a')) return unwrap(value['@a']);
  if (own(value, '@r')) return { __referenceId: value['@r'] };
  const output = {};
  for (const [key, child] of Object.entries(value)) {
    if (key === '@' || key === '@c') continue;
    output[key] = unwrap(child);
  }
  return output;
}
function objectKeys(value) {
  return value && typeof value === 'object' && !Array.isArray(value) ? Object.keys(value).sort() : [];
}
function array(value) { return Array.isArray(value) ? value : []; }
function inventoryEntries(obj, database) {
  if (!obj || typeof obj !== 'object') return [];
  return Object.entries(obj).map(([id, count]) => ({ id: Number(id), count, databaseName: database[Number(id)]?.name || null }));
}
function namedIndexes(values, names, limit = 30) {
  const result = [];
  array(values).forEach((value, id) => {
    if (id === 0 || value === null || value === false || value === 0 || value === '') return;
    result.push({ id, databaseName: names[id] || null, value });
  });
  return { count: result.length, sample: result.slice(0, limit) };
}

const dbDir = path.join(root, 'www', 'data');
const db = {};
for (const name of ['System','Actors','Classes','Items','Weapons','Armors','Skills','States','MapInfos','CommonEvents']) {
  db[name] = JSON.parse(fs.readFileSync(path.join(dbDir, name + '.json'), 'utf8'));
}
const mapNames = Object.fromEntries(db.MapInfos.filter(Boolean).map(x => [x.id, x.name]));
const databaseIndex = {
  system: {
    gameTitle: db.System.gameTitle,
    versionId: db.System.versionId,
    currencyUnit: db.System.currencyUnit,
    locale: db.System.locale,
    switchCount: db.System.switches.length - 1,
    variableCount: db.System.variables.length - 1
  },
  switches: db.System.switches.map((name, id) => ({ id, name })).filter(x => x.id && x.name),
  variables: db.System.variables.map((name, id) => ({ id, name })).filter(x => x.id && x.name),
  maps: db.MapInfos.filter(Boolean).map(x => ({ id: x.id, name: x.name, parentId: x.parentId, order: x.order })),
  actors: db.Actors.filter(Boolean).map(x => ({ id: x.id, name: x.name, classId: x.classId })),
  classes: db.Classes.filter(Boolean).map(x => ({ id: x.id, name: x.name })),
  counts: Object.fromEntries(['Actors','Classes','Items','Weapons','Armors','Skills','States','MapInfos','CommonEvents'].map(name => [name, db[name].filter(Boolean).length]))
};
fs.writeFileSync(path.join(root, 'database-index.json'), JSON.stringify(databaseIndex, null, 2), 'utf8');

const files = fs.readdirSync(root).filter(n => n.endsWith('.rpgsave')).sort((a,b) => a.localeCompare(b, undefined, {numeric:true}));
const report = { generatedAtUtc: new Date().toISOString(), files: [], slots: [], database: databaseIndex.system, counts: databaseIndex.counts };
let globalInfo = null;
for (const name of files) {
  const bytes = fs.readFileSync(path.join(root, name));
  const compressed = bytes.toString('utf8');
  const json = LZString.decompressFromBase64(compressed);
  let raw, parseError = null;
  try { raw = JSON.parse(json); } catch (e) { parseError = String(e); }
  fs.writeFileSync(path.join(decodedDir, name + '.json'), json, 'utf8');
  const recompressed = LZString.compressToBase64(json);
  const item = {
    name,
    compressedBytes: bytes.length,
    compressedSha256: sha256(bytes),
    base64AlphabetOnly: /^[A-Za-z0-9+/=]+$/.test(compressed),
    base64LengthMultipleOf4: compressed.length % 4 === 0,
    decompressedChars: json?.length ?? null,
    jsonSha256: json == null ? null : sha256(Buffer.from(json, 'utf8')),
    parseOk: !parseError,
    parseError,
    deterministicRoundTripExact: recompressed === compressed,
    rootType: Array.isArray(raw) ? 'array' : typeof raw,
    topLevelKeys: raw && !Array.isArray(raw) ? Object.keys(raw).filter(k => k !== '@c').sort() : null
  };
  if (raw && name.startsWith('file')) {
    const stats = { classes: {}, objectIds: 0, references: 0, arrayWrappers: 0 };
    const data = normalize(raw, stats);
    item.jsonEx = stats;
    item.memberKeys = Object.fromEntries(Object.entries(data).filter(([,v]) => v && typeof v === 'object' && !Array.isArray(v)).map(([k,v]) => [k, objectKeys(v).filter(x => x !== '__class')]));
    const actorsData = array(data.actors?._data);
    const actors = actorsData.filter(Boolean).map(actor => ({
      actorId: actor._actorId,
      databaseName: db.Actors[actor._actorId]?.name || null,
      classId: actor._classId,
      className: db.Classes[actor._classId]?.name || null,
      level: actor._level,
      hp: actor._hp,
      mp: actor._mp,
      tp: actor._tp,
      exp: actor._exp,
      skills: array(actor._skills),
      states: array(actor._states),
      equips: array(actor._equips).map(e => e ? { dataClass: e._dataClass, itemId: e._itemId } : null),
      keySet: objectKeys(actor).filter(x => x !== '__class')
    }));
    const summary = {
      name,
      system: {
        saveEnabled: data.system?._saveEnabled,
        battleCount: data.system?._battleCount,
        winCount: data.system?._winCount,
        escapeCount: data.system?._escapeCount,
        saveCount: data.system?._saveCount,
        versionId: data.system?._versionId,
        framesOnSave: data.system?._framesOnSave,
        playSecondsApprox: data.system?._framesOnSave == null ? null : Math.floor(data.system._framesOnSave / 60),
        keySet: objectKeys(data.system).filter(x => x !== '__class')
      },
      party: {
        gold: data.party?._gold,
        steps: data.party?._steps,
        actorIds: array(data.party?._actors),
        itemKinds: objectKeys(data.party?._items).length,
        weaponKinds: objectKeys(data.party?._weapons).length,
        armorKinds: objectKeys(data.party?._armors).length,
        items: inventoryEntries(data.party?._items, db.Items),
        weapons: inventoryEntries(data.party?._weapons, db.Weapons),
        armors: inventoryEntries(data.party?._armors, db.Armors),
        keySet: objectKeys(data.party).filter(x => x !== '__class')
      },
      actors,
      map: {
        mapId: data.map?._mapId,
        databaseName: mapNames[data.map?._mapId] || null,
        eventSlots: array(data.map?._events).length,
        keySet: objectKeys(data.map).filter(x => x !== '__class')
      },
      player: {
        x: data.player?._x,
        y: data.player?._y,
        direction: data.player?._direction,
        vehicleType: data.player?._vehicleType,
        transparent: data.player?._transparent,
        keySet: objectKeys(data.player).filter(x => x !== '__class')
      },
      timer: data.timer,
      switches: namedIndexes(data.switches?._data, db.System.switches),
      variables: namedIndexes(data.variables?._data, db.System.variables),
      selfSwitchCount: objectKeys(data.selfSwitches?._data).length,
      saveParams: data.saveParams,
      customTopLevelKeys: Object.keys(data).filter(k => !['system','screen','timer','switches','variables','selfSwitches','actors','party','map','player','saveParams','__class'].includes(k)),
      rootKeySet: objectKeys(data).filter(x => x !== '__class')
    };
    report.slots.push(summary);
  } else if (name === 'global.rpgsave' && raw) {
    globalInfo = raw;
    item.globalEntries = raw.map((x, id) => x ? ({ id, globalId: x.globalId, title: x.title, playtime: x.playtime, timestamp: x.timestamp, timestampUtc: new Date(x.timestamp).toISOString(), characterCount: array(x.characters).length, faceCount: array(x.faces).length }) : null);
  } else if (name === 'config.rpgsave' && raw) {
    item.config = raw;
  }
  report.files.push(item);
}
if (globalInfo) {
  report.globalSlotConsistency = report.slots.map(slot => {
    const id = Number(slot.name.match(/file(\d+)\.rpgsave/)[1]);
    const g = globalInfo[id];
    return {
      id,
      file: slot.name,
      globalEntryPresent: !!g,
      globalVersionTitle: g?.title || null,
      playtime: g?.playtime || null,
      fileFramesPlaytime: slot.system.framesOnSave == null ? null : `${String(Math.floor(slot.system.framesOnSave/216000)).padStart(2,'0')}:${String(Math.floor(slot.system.framesOnSave/3600)%60).padStart(2,'0')}:${String(Math.floor(slot.system.framesOnSave/60)%60).padStart(2,'0')}`,
      versionIdMatchesDatabase: slot.system.versionId === db.System.versionId
    };
  });
}
const slotTopSets = report.slots.map(s => s.rootKeySet.join('|'));
report.schema = {
  allSlotTopLevelKeysIdentical: new Set(slotTopSets).size === 1,
  topLevelKeys: report.slots[0]?.rootKeySet || [],
  memberKeyUnions: {}
};
for (const section of ['system','party','map','player']) {
  const union = new Set();
  for (const f of report.files.filter(x => x.memberKeys)) for (const k of (f.memberKeys[section] || [])) union.add(k);
  report.schema.memberKeyUnions[section] = [...union].sort();
}
fs.writeFileSync(path.join(root, 'analysis-report.json'), JSON.stringify(report, null, 2), 'utf8');
console.log(JSON.stringify({
  decodedFiles: report.files.length,
  slotFiles: report.slots.length,
  allParsed: report.files.every(x => x.parseOk),
  allRoundTripExact: report.files.every(x => x.deterministicRoundTripExact),
  allSlotTopLevelKeysIdentical: report.schema.allSlotTopLevelKeysIdentical,
  topLevelKeys: report.schema.topLevelKeys,
  slotIds: report.slots.map(s => Number(s.name.match(/\d+/)[0])),
  missingSlotIds: Array.from({length:20},(_,i)=>i+1).filter(id => !report.slots.some(s => Number(s.name.match(/\d+/)[0]) === id)),
  outputs: ['analysis-report.json','database-index.json','decoded\\*.json']
}, null, 2));
