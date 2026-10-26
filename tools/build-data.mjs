// 由 Blank 16.txt 生成 Wordsbook 数据。
// 规则：词形并入原形卡的 Related（右侧标用法）、例句就近归到句子里用到的词、缺音标按文档转写习惯补。
// 用法：node tools/build-data.mjs tools/lines.json tools/wordsbook.import.json
import fs from "node:fs";

const SRC = process.argv[2] ?? "tools/lines.json";
const OUT = process.argv[3] ?? "tools/wordsbook.import.json";
const REPORT = process.argv[4] ?? "tools/import-report.txt";
const sections = JSON.parse(fs.readFileSync(SRC, "utf8"));
const LANG = "1afce1d3a1034daea296c5d542b73728";

// ================================================================ 词形表
const V = (lemma, usage) => ({ lemma, usage, kind: "verb" });
const P = (lemma, usage = "plural") => ({ lemma, usage, kind: "noun" });
const A = (lemma, usage = "-e") => ({ lemma, usage, kind: "adj" });

const FORMS = {
  // 复数
  mannen: P("man"), meisjes: P("meisje"), vrouwen: P("vrouw"), jongens: P("jongen"),
  kinderen: P("kind"), honden: P("hond"), boeken: P("boek"), eenden: P("eend"),
  dieren: P("dier"), olifanten: P("olifant"), katten: P("kat"), vissen: P("vis"),
  mensen: P("mens"), paarden: P("paard"), schildpadden: P("schildpad"),
  boterhammen: P("boterham"), beren: P("beer"), schapen: P("schaap"), muizen: P("muis"),
  bananen: P("banaan"), aardbeien: P("aardbei"), borden: P("bord"), appels: P("appel"),
  vogels: P("vogel"), kranten: P("krant"), "menu's": P("menu"), varkens: P("varken"),
  koeien: P("koe"), zoons: P("zoon"), tafels: P("tafel"), vrienden: P("vriend"),
  vriendinnen: P("vriendin"), huisdieren: P("huisdier"), namen: P("naam"),
  sokken: P("sok"), klompen: P("klomp"), broeken: P("broek"), vragen: P("vraag"),

  // 形容词 -e
  lange: A("lang"), grote: A("groot"), kleine: A("klein"), snelle: A("snel"),
  goede: A("goed"), zware: A("zwaar"), leuke: A("leuk"), jonge: A("jong"),
  oude: A("oud"), dure: A("duur"), verse: A("vers"), langzame: A("langzaam"),
  kleurrijke: A("kleurrijk"), slechte: A("slecht"),

  // 动词变位
  drink: V("drinken", "ik"), drinkt: V("drinken", "jij/hij"), drinken: V("drinken", "wij/zij"),
  eet: V("eten", "ik/jij/hij"), eten: V("eten", "wij/zij"),
  lees: V("lezen", "ik"), leest: V("lezen", "jij/hij"), lezen: V("lezen", "wij/zij"),
  spreek: V("spreken", "ik"), spreekt: V("spreken", "jij/hij"), spreken: V("spreken", "wij/zij"),
  loop: V("lopen", "ik"), loopt: V("lopen", "jij/hij"), lopen: V("lopen", "wij/zij"),
  schrijf: V("schrijven", "ik"), schrijft: V("schrijven", "jij/hij"), schrijven: V("schrijven", "wij/zij"),
  zwem: V("zwemmen", "ik"), zwemt: V("zwemmen", "jij/hij"), zwemmen: V("zwemmen", "wij/zij"),
  kook: V("koken", "ik"), kookt: V("koken", "jij/hij"),
  kom: V("komen", "ik"),
  slaap: V("slapen", "ik"), slaapt: V("slapen", "hij/zij"),
  speel: V("spelen", "ik"), speelt: V("spelen", "zij"), spelen: V("spelen", "wij/jullie"),
  wil: V("willen", "ik/hij"), wilt: V("willen", "jij"), willen: V("willen", "wij/zij"),
  zie: V("zien", "ik"), ziet: V("zien", "hij/zij"),
  hoor: V("horen", "ik"), hoort: V("horen", "hij/zij"), horen: V("horen", "jullie"),
  ga: V("gaan", "ik"), gaat: V("gaan", "hij/zij"), gaan: V("gaan", "wij"),
  maak: V("maken", "ik"), maken: V("maken", "jullie"),
  ken: V("kennen", "ik"), kent: V("kennen", "jij/u"),
  geef: V("geven", "ik"), geeft: V("geven", "hij/zij"), geven: V("geven", "wij"),
  heb: V("hebben", "ik"), hebt: V("hebben", "jij"), heeft: V("hebben", "hij/zij"),
  hebben: V("hebben", "wij/zij"),
  ben: V("zijn", "ik"), bent: V("zijn", "jij"), is: V("zijn", "hij/zij"), zijn: V("zijn", "wij/zij"),
  krijg: V("krijgen", "ik"), krijgt: V("krijgen", "hij/zij"),
  doe: V("doen", "ik"), doet: V("doen", "hij/zij"),
  draag: V("dragen", "ik"),
  woon: V("wonen", "ik"),
  stel: V("stellen", "ik"),
  antwoord: V("antwoorden", "ik"),
};

// ================================================================ 词性与性质
const INT = new Set(["hallo", "hoi", "goedemorgen", "dag", "doei", "bedankt", "goedendag",
  "welterusten", "goedenavond", "ja", "nee", "alsjeblieft", "alstublieft", "oké", "sorry",
  "pardon", "dank je", "dank je wel", "tot ziens", "het spijt me"]);
const NUM = new Set(["twee"]);
const TAGLESS = new Set(["hoe heet je", "hoe gaat het", "van mij", "van jullie", "van jou", "van ons"]);

const VERBS = new Set(["drinken", "eten", "lezen", "spreken", "lopen", "schrijven", "zwemmen",
  "koken", "komen", "slapen", "spelen", "willen", "zien", "horen", "gaan", "maken", "kennen",
  "geven", "hebben", "zijn", "krijgen", "doen", "heten", "dragen", "wonen", "stellen", "antwoorden"]);
const ADJS = new Set(["lang", "groot", "klein", "kort", "snel", "langzaam", "goed", "zwaar",
  "slecht", "licht", "sterk", "zwak", "nieuw", "leuk", "jong", "moeilijk", "oud", "makkelijk",
  "vol", "leeg", "duur", "vers", "belangrijk", "goedkoop", "koud", "warm", "rustig", "schoon",
  "interessant", "lekker", "roze", "wit", "grijs", "groen", "oranje", "bruin", "zwart", "blauw",
  "geel", "paars", "rood", "donker", "kleurrijk", "heet"]);
const ADV = new Set(["niet", "ook", "wel", "genoeg", "veel", "weinig", "beetje", "nu", "hier",
  "daar", "soms", "misschien", "net", "hoe", "waar", "waarom", "wanneer", "hoeveel"]);
const DET = new Set(["geen", "alle", "allerlei", "sommige", "verschillende", "elke", "ieder",
  "enige", "enkele"]);
const PREP = new Set(["op", "naar", "voor", "in", "uit", "met", "naast", "na", "bij", "van",
  "tussen", "aan", "over", "onder", "door", "achter", "tegen", "zonder", "vanwege", "tijdens", "zoals"]);
const CONJ = new Set(["of", "maar", "want", "dus"]);
const PRON = new Set(["je", "hij", "ze", "dat", "jij", "zij", "wij", "ik", "we", "jullie", "u",
  "me", "hem", "jou", "haar", "hun", "ons", "jouw", "onze", "mijn", "uw", "iemand", "iets",
  "alles", "iedereen", "men", "wat", "wie", "wiens", "welke"]);
const SUBJ = new Set(["ik", "je", "jij", "hij", "ze", "zij", "wij", "we", "jullie", "u"]);
const SUBJ_CANON = { ik: "ik", je: "jij", jij: "jij", hij: "hij", ze: "zij", zij: "zij",
  wij: "wij", we: "wij", jullie: "jullie", u: "u" };

const NATURE = {
  vrouw: "de", man: "de", jongen: "de", meisje: "het", kind: "het", appel: "de", sap: "het",
  water: "het", brood: "het", rijst: "de", boterham: "de", krant: "de", menu: "het",
  boek: "het", vis: "de", wijn: "de", thee: "de", kip: "de", ei: "het", koffie: "de",
  kaas: "de", pasta: "de", suiker: "de", ontbijt: "het", fruit: "het", middageten: "het",
  bier: "het", tomaat: "de", aardbei: "de", banaan: "de", sinaasappel: "de", vlees: "het",
  soep: "de", avondeten: "het", groente: "de", maaltijd: "de", glas: "het", zout: "het",
  peper: "de", bord: "het", dorst: "de", honger: "de", kat: "de", muis: "de", vogel: "de",
  hond: "de", dier: "het", mens: "de", schildpad: "de", olifant: "de", konijn: "het",
  paard: "het", koe: "de", eend: "de", varken: "het", hert: "het", schaap: "het",
  beer: "de", gans: "de", uil: "de", spin: "de", krab: "de", hoorn: "de", neushoorn: "de",
  staart: "de", broek: "de", jas: "de", jurk: "de", schoen: "de", hoed: "de", sok: "de",
  rok: "de", kleren: "de", trui: "de", ondergoed: "het", klomp: "de", tas: "de", bril: "de",
  hemd: "het", pak: "het", huis: "het", moeder: "de", vader: "de", familie: "de",
  ouders: "de", baby: "de", zus: "de", zoon: "de", dochter: "de", broer: "de", gezin: "het",
  huisdier: "het", vriend: "de", vriendin: "de", naam: "de", vlag: "de", tafel: "de",
  vraag: "de", meneer: "de", mevrouw: "de", engels: "het", nederlands: "het",
  "eten#noun": "het", "zijn#poss": "",
};

// ================================================================ 释义
const MEANING = {
  ze: "she; they", jij: "you", zij: "she; they", wij: "we", zijn: "to be", "zijn#poss": "his",
  hebben: "to have", wat: "some; what", weinig: "little; few", onze: "our", roze: "pink",
  dorst: "thirst", honger: "hunger", mens: "human; person", huisdier: "pet", vriend: "friend",
  vriendin: "girlfriend; friend", kleren: "clothes", ouders: "parents", schoen: "shoe",
  sok: "sock", klomp: "clog", groente: "vegetables", "eten#noun": "food", "de baby": "baby",
  heet: "hot; spicy; horny", heten: "to be called", welke: "which", wel: "indeed",
  veel: "much; many; a lot", enige: "a few; some", enkele: "a few", beetje: "a little; a bit", zoals: "like; such as", gezin: "family (household)",
  achter: "behind", zonder: "without", uit: "out of; from", geen: "not a", baby: "baby",
  drinken: "to drink", eten: "to eat", lezen: "to read", spreken: "to speak", lopen: "to walk",
  schrijven: "to write", zwemmen: "to swim", koken: "to cook", komen: "to come",
  slapen: "to sleep", spelen: "to play", willen: "to want", zien: "to see", horen: "to hear",
  gaan: "to go", maken: "to make", kennen: "to know", geven: "to give", krijgen: "to get; to receive",
  doen: "to do; to put", dragen: "to wear", wonen: "to live (in)", stellen: "to ask (a question)",
  antwoorden: "to answer", kleurrijk: "colorful", vrij: "free",
  "hoe heet je?": "what's your name?", "hoe gaat het?": "how is it going?; how are you?",
  "van mij": "mine", "van jullie": "yours (plural)", "van jou": "yours", "van ons": "ours",
  "het spijt me": "I'm sorry", "tot ziens": "goodbye (see you)", "dank je wel": "thank you",
  "dank je": "thanks", alsjeblieft: "please", alstublieft: "please (formal)", pardon: "excuse me",
};

const PHRASES = new Set(["dank je", "dank je wel", "tot ziens", "hoe heet je", "hoe gaat het",
  "het spijt me", "van mij", "van jullie", "van jou", "van ons"]);

// 文档里拼错/写反的释义
const EN_FIX = [
  [/^excure me$/i, "excuse me"],
  [/^You eten$/i, "you eat"],
  [/^Sometimes zie je mij$/i, "Sometimes you see me"],
  [/^I am from België$/i, "I am from Belgium"],
  [/^the cat sleeps on your koat$/i, "the cat sleeps on your coat"],
  [/^the woman; woman$/i, "woman"],
  [/^how much\/many$/i, "how much / how many"],
  [/^This girl$/i, "girl"],
];

const NL_FIX = [
  [/[‘’]/g, "'"],
  [/^g \[[^\]]+\]:\s*/u, ""],
  [/goedmongen/gi, "goedemorgen"],
  [/the boek/gi, "het boek"],
  [/zie ye/gi, "zie je"],
  [/\bhood\b/gi, "hond"],
  [/\bkout\b/gi, "koud"],
  [/en en jongen/gi, "en een jongen"],
  [/\bJulie\b/g, "Jullie"],
];

const EN_FOR_NL = new Map([
  ["Het is lang", "It is long"],
  ["Heb je het koud?", "Are you cold?"],
  ["De vader van het meisje is een slechte man", "The girl's father is a bad man"],
]);

const PRON_FILL = {
  // 名词（mevrouw 文档只写了 me-）
  mevrouw: "me-vrouw", appel: "a-pel", menu: "me-nu", bier: "beer", bord: "bord", muis: "mous", krab: "krab",
  dorst: "dors(t)", boek: "book", sok: "sok", klomp: "klomp",
  beer: "beer", rok: "rok", hemd: "hemt", dochter: "dogh-ter", huisdier: "huis-deer",
  vriend: "vriend", vraag: "vraagh", baby: "bei-bie", "eten#noun": "ei-ten", welke: "wel-ke",
  // 形容词
  lang: "lang", klein: "klein", kort: "kort", sterk: "sterk", zwak: "zwak", vol: "vol",
  warm: "warm", wit: "wit", kleurrijk: "kě-lo-lai-k",
  // 副词 / 连词 / 介词
  hoe: "hoo", of: "of", maar: "maar", want: "want", ook: "ook", wel: "wel", net: "net",
  nu: "nu", hier: "heer", daar: "daar", soms: "soms", men: "men", op: "op", naar: "naar",
  voor: "voor", in: "in", met: "met", naast: "naast", na: "na", bij: "bei", van: "van",
  aan: "aan", over: "o-ver", onder: "on-der", door: "door", zonder: "zon-der",
  wie: "wee", waar: "waar", waarom: "waa-rom", wanneer: "wan-neer", wiens: "weens",
  hoeveel: "hoe-veel", wat: "wat",
  // 代词
  me: "me", hem: "hem", jou: "you", haar: "haar", hun: "hun", ons: "ons", jouw: "you",
  mijn: "mein", uw: "u", u: "u", onze: "on-ze", jij: "ja-(ei)", zij: "zei", wij: "wei",
  // 感叹 / 固定说法
  ja: "ya", oké: "o-kei", "van mij": "van mei", "van jullie": "van you-li",
  "van jou": "van you", "van ons": "van ons",
  // 动词原形
  drinken: "drin-ken", eten: "ei-ten", lezen: "lei-zen", spreken: "spre-ken",
  lopen: "lo-pen", schrijven: "s-grei-ven", zwemmen: "zwem-men", koken: "ko-ken",
  komen: "ko-men", slapen: "sla-pen", spelen: "spei-len", willen: "wil-len",
  zien: "zeen", horen: "ho-ren", gaan: "ghaan", maken: "ma-ken", kennen: "ken-nen",
  geven: "ghei-ven", hebben: "he-ben", zijn: "zein", krijgen: "krei-ghen", doen: "doon",
  heten: "hei-te(n)", dragen: "dra-ghen", wonen: "wo-nen", stellen: "stel-len",
  antwoorden: "an-twoor-den",
};

// 需要单独处理的文档行（词条、同形异义、以及文档里没有原形行的动词）
const SENSE_RAW = [
  [/^heet \/ called/, { key: "heet", word: "heet", pos: "adj", kind: "adj",
    meaning: "hot; spicy; horny" }],
  [/^welke; welk/, { key: "welke", word: "welke", pos: "pron", kind: "pron",
    meaning: "which", extra: [["welk", "het + singular"]] }],
  [/^zijn \/ his/, { key: "zijn#poss", word: "zijn", pos: "pron", kind: "pron",
    meaning: "his" }],
  [/^het eten \/ the food/, { key: "eten#noun", word: "eten", pos: "noun", kind: "noun",
    nature: "het", meaning: "food" }],
];

// ================================================================ 工具函数
const strip = (s) => s.toLowerCase().replace(/[?.!,;:]+$/g, "").trim();
const tokens = (s) => strip(s).split(/[^a-zà-ÿ0-9'’]+/i).filter(Boolean);
const articleOf = (s) => (s.match(/^(de|het|een)\s+/i) ?? [])[1]?.toLowerCase() ?? "";
const bare = (s) => s.replace(/^(de|het|een)\s+/i, "").trim();
const cleanParens = (s) => s.replace(/\([^)]*\)/g, "").replace(/\s+/g, " ").trim();
const CJK = /[\u3000-\u9fff\uff00-\uffef]/;

function posOf(word) {
  if (VERBS.has(word)) return "verb";
  if (ADJS.has(word)) return "adj";
  if (ADV.has(word)) return "adv";
  if (DET.has(word)) return "det";
  if (PREP.has(word)) return "prep";
  if (CONJ.has(word)) return "conj";
  if (PRON.has(word)) return "pron";
  if (INT.has(word)) return "int";
  if (NUM.has(word)) return "num";
  return "noun";
}

function cleanEn(s) {
  let t = (s ?? "").replace(/\[[^\]]+\]/g, "");
  if (CJK.test(t)) t = t.split(/\s+\/\s+/).filter(p => !CJK.test(p)).join(" ");
  t = t.replace(/\s+/g, " ").replace(/\s*;\s*$/, "").trim();
  for (const [re, to] of EN_FIX) t = t.replace(re, to);
  return t;
}

function cleanNl(s) {
  let t = s;
  for (const [re, to] of NL_FIX) t = t.replace(re, to);
  return t.replace(/\s+/g, " ").trim();
}

function cleanPron(p) {
  return p.replace(/^(the\(d\)|he\(t\)|the|an|en)\s+/i, "").trim();
}

function normalize(raw) {
  let nl = raw.nl ?? "", en = raw.en ?? "", pron = raw.pron ?? "";
  if (!nl) {
    let t = (raw.raw ?? "").trim();
    const pm = t.match(/\[([^\]]+)\]/);
    if (pm) {
      pron = pm[1].replace(/[‘’'"]/g, "").trim();
      t = t.replace(pm[0], "").trim();
    }
    const q = t.match(/^(.+?\?)\s+(.+)$/);
    if (q) { nl = q[1].trim(); en = q[2].trim(); }
    else nl = t;
  }
  nl = cleanNl(nl);
  en = cleanEn(en);
  if (!en && EN_FOR_NL.has(nl)) en = EN_FOR_NL.get(nl);
  return { nl, en, pron: cleanPron(pron.replace(/[‘’'"]/g, "").trim()) };
}

function sentenceEn(s) {
  let t = cleanEn(s).replace(/^\(([^)]*)\)\s*/, "$1 ").replace(/\s+/g, " ").trim();
  if (!t) return "";
  return t[0].toUpperCase() + t.slice(1);
}

// ================================================================ 卡片
const cards = new Map();

function newCard(key, word, opts = {}) {
  let kind = opts.kind ?? (VERBS.has(word) ? "verb" : ADJS.has(word) ? "adj" : "noun");
  return {
    key, word, kind,
    pos: opts.pos !== undefined ? opts.pos : posOf(word),
    nature: opts.nature !== undefined ? opts.nature : (NATURE[word] ?? NATURE[key] ?? ""),
    meanings: [], pron: "", date: opts.date ?? "", anchors: [],
    extra: [], lineUsages: new Map(), sentences: [],
  };
}

function getCard(key, word, opts = {}) {
  if (!cards.has(key)) cards.set(key, newCard(key, word, opts));
  const c = cards.get(key);
  if (!c.date && opts.date) c.date = opts.date;
  return c;
}

function lemmaCard(form, date) {
  const f = FORMS[form];
  const c = getCard(f.lemma, f.lemma, { kind: f.kind, date });
  if (!c.date) c.date = date;
  return c;
}

function addForm(card, form, usage, anchor) {
  if (!card.lineUsages.has(form)) card.lineUsages.set(form, new Set());
  for (const u of usage.split("/")) if (u) card.lineUsages.get(form).add(u);
  if (anchor != null) card.anchors.push(anchor);
}

function addMeaning(card, en) {
  let t = cleanEn(en);
  if (!t) return;
  if (card.kind === "noun") t = t.replace(/^(the|a|an)\s+/i, "").trim();
  if (!t) return;
  for (const part of t.split(/\s*;\s*/)) {
    const p = part.trim();
    if (!p) continue;
    if (!card.meanings.some(m => m.toLowerCase() === p.toLowerCase())) card.meanings.push(p);
  }
}

function addSentence(card, idx, nl, en) {
  const e = sentenceEn(en);
  if (!e) return;
  const text = `${nl} — ${e}`;
  if (card.sentences.some(s => s.text === text)) return;
  card.sentences.push({ idx, text });
}

function relatedForms(c) {
  const forms = [];
  for (const [form, f] of Object.entries(FORMS)) {
    if (f.lemma !== c.word || f.kind !== c.kind) continue;
    if (strip(form) === strip(c.word)) continue;
    if (!forms.includes(form)) forms.push(form);
  }
  for (const [form] of c.extra) if (!forms.includes(form)) forms.push(form);
  return forms;
}

function usageFor(c, form) {
  const set = new Set(c.lineUsages.get(form) ?? []);
  const f = FORMS[form];
  if (f && f.lemma === c.word && f.kind === c.kind) {
    for (const u of f.usage.split("/")) set.add(u);
  }
  const ex = c.extra.find(([ef]) => ef === form);
  if (ex) for (const u of ex[1].split("/")) set.add(u);
  return [...set].join("/");
}

// ================================================================ 第一遍：建卡、并变形、收例句
let lineNo = 0;
for (const sec of sections) {
  for (const raw of sec.lines) {
    lineNo++;
    const L = normalize(raw);
    if (!L.nl) continue;
    const clean = cleanParens(L.nl);
    const toks = tokens(clean);

    if (L.nl.toLowerCase() === "ham") continue; // 文档笔误，hun 的重复行

    let sense = null;
    for (const [re, s] of SENSE_RAW) if (re.test(raw.raw ?? "")) sense = s;

    if (sense) {
      const c = getCard(sense.key, sense.word, {
        pos: sense.pos, kind: sense.kind, nature: sense.nature ?? "",
        date: sec.date,
      });
      if (sense.meaning) c.meanings = [sense.meaning];
      for (const [ef, eu] of sense.extra ?? []) c.extra.push([ef, eu]);
      if (L.pron && !c.pron) c.pron = L.pron;
      c.anchors.push(lineNo);
      continue;
    }

    // 词形行：单个变形，或主语代词 + 变形
    let form = null;
    if (toks.length === 1 && FORMS[toks[0]]) form = toks[0];
    else if (toks.length === 2 && SUBJ.has(toks[0]) && FORMS[toks[1]]) form = toks[1];
    if (form) {
      const f = FORMS[form];
      const card = lemmaCard(form, sec.date);
      addForm(card, form, f.usage, lineNo);
      if (toks.length === 2 && SUBJ.has(toks[0])) {
        card.lineUsages.get(form).add(SUBJ_CANON[toks[0]]);
      }
      if (form === f.lemma && L.pron && !card.pron) card.pron = L.pron;
      if (f.kind === "verb" && L.en) {
        const hasSubject = toks.length === 2;
        const gloss = !hasSubject && !L.en.startsWith("(") && !L.nl.includes("?")
          && L.en.split(/\s+/).length <= 4;
        if (gloss) addMeaning(card, L.en);
        else addSentence(card, lineNo, clean, L.en);
      }
      continue;
    }

    // 词条行
    const isPhrase = PHRASES.has(strip(clean));
    const art = isPhrase ? "" : articleOf(clean);
    const display = (isPhrase ? clean : bare(clean)).toLowerCase().replace(/\s+/g, " ").trim();
    const key = strip(display);
    const single = !/\s/.test(display) && !/\?/.test(clean) && !clean.includes(";");
    if (isPhrase || single) {
      const f = FORMS[key];
      if (!isPhrase && art && f && f.kind === "noun") {
        // 复数词条并进原形
        const card = lemmaCard(key, sec.date);
        addForm(card, key, f.usage, lineNo);
        continue;
      }
      const pos = INT.has(key) ? "int" : (TAGLESS.has(key) || isPhrase) ? "" : posOf(key);
      const c = getCard(key, display, {
        date: sec.date,
        nature: isPhrase ? "" : (art === "de" || art === "het" ? art : undefined),
        pos,
      });
      if (!isPhrase && (art === "de" || art === "het")) c.nature = art;
      if (L.pron && !c.pron) c.pron = L.pron;
      if (L.en) addMeaning(c, L.en);
      c.anchors.push(lineNo);
      const paren = L.nl.match(/\(([^)]+)\)/);
      if (paren && FORMS[strip(paren[1])]) {
        const pf = FORMS[strip(paren[1])];
        addForm(c, strip(paren[1]), pf.usage, lineNo);
      }
      continue;
    }
  }
}

// 文档把 called / hot 写在同一行：heet 是形容词卡，heten 是动词卡
{
  const heten = getCard("heten", "heten", { kind: "verb", pos: "verb", date: "2026-08-12" });
  addForm(heten, "heet", "ik/hij/zij", 13);
  heten.extra.push(["heet", "ik/hij/zij"]);
}

// ================================================================ 第二遍：整句归属
const attached = [];
lineNo = 0;
for (const sec of sections) {
  for (const raw of sec.lines) {
    lineNo++;
    const L = normalize(raw);
    if (!L.nl || !L.en) continue;
    const clean = cleanParens(L.nl);
    const toks = tokens(clean);

    let isEntry = false;
    for (const [re] of SENSE_RAW) if (re.test(raw.raw ?? "")) isEntry = true;
    if (toks.length === 1 && FORMS[toks[0]]) isEntry = true;
    if (toks.length === 2 && SUBJ.has(toks[0]) && FORMS[toks[1]]) isEntry = true;
    if (PHRASES.has(strip(clean))) isEntry = true;
    const art = articleOf(clean);
    const display = bare(clean);
    if (art && !/\s/.test(display)) isEntry = true;
    if (!art && !/\s/.test(display) && !/\?/.test(clean) && !clean.includes(";")) isEntry = true;
    if (isEntry) continue;

    const text = strip(clean);
    let best = null;
    for (const c of cards.values()) {
      if (c.key === "heet") continue;
      const words = [c.word, ...relatedForms(c)].map(w => strip(w));
      const hit = words.some(w => w.includes(" ") ? text.includes(w) : toks.includes(w));
      if (!hit || c.anchors.length === 0) continue;
      const d = Math.min(...c.anchors.map(a => Math.abs(a - lineNo)));
      if (!best || d < best.d) best = { c, d };
    }
    if (!best) {
      attached.push(`UNATTACHED | ${sec.date} | ${L.nl} = ${L.en}`);
      continue;
    }
    addSentence(best.c, lineNo, clean, L.en);
    attached.push(`ATTACH | ${sec.date} | ${L.nl} -> ${best.c.word} (d=${best.d})`);
  }
}

// ================================================================ 输出
const ordered = [...cards.values()]
  .filter(c => c.word.length > 0)
  .sort((a, b) => a.date.localeCompare(b.date) || (a.anchors[0] ?? 0) - (b.anchors[0] ?? 0));

const words = [];
const missingPron = [];
const derivedPron = [];
const noDesc = [];
let n = 0;
for (const c of ordered) {
  const forms = relatedForms(c);
  const usages = forms.map(f => usageFor(c, f));
  let pron = c.pron === "me-" ? "me-vrouw" : c.pron;
  if (!pron) {
    pron = PRON_FILL[c.key] || PRON_FILL[c.word] || "";
    if (pron) derivedPron.push(c.key);
  }
  if (!pron) missingPron.push(c.key);

  const meaning = MEANING[c.key] ?? MEANING[c.word] ?? (c.meanings.join("; ") || c.word);
  const withPos = c.pos ? `${c.pos}. ${meaning}` : meaning;

  const out = {
    id: `nl${String(++n).padStart(3, "0")}`,
    langId: LANG,
    word: c.word,
    meaning: withPos,
    pronunciation: pron,
    description: c.sentences.sort((a, b) => a.idx - b.idx).map(s => s.text).join("\n"),
    nature: c.nature,
    relatedUsage: usages.join(" · "),
    date: c.date,
    mark: 0,
    related: forms,
    relatedForms: forms.map((form, i) => ({ form, nature: usages[i] ?? "" })),
    categoryIds: [],
  };
  words.push(out);
  if (!out.description) noDesc.push(c.word);
}

const days = [...new Set(sections.map(s => s.date))].sort();
fs.writeFileSync(OUT, JSON.stringify({ words, days: days.map(date => ({ langId: LANG, date })) }, null, 1));

const rep = [];
rep.push(`cards=${words.length} days=${days.length}`);
rep.push(`missing pronunciation (${missingPron.length}): ${missingPron.join(", ")}`);
rep.push(`derived pronunciation (${derivedPron.length}): ${derivedPron.join(", ")}`);
rep.push(`cards without description: ${noDesc.length}`);
rep.push(`sentences attached: ${attached.filter(x => x.startsWith("ATTACH")).length}`);
rep.push(...attached);
rep.push("", "cards:");
for (const w of words) {
  rep.push(`${w.id} ${w.date} | ${w.word} | ${w.nature || "-"} | ${w.meaning} | /${w.pronunciation}/ | rel=${w.related.join(",")} | ${w.relatedUsage}`);
}
fs.writeFileSync(REPORT, rep.join("\n"));
console.log(`cards=${words.length} days=${days.length} missingPron=${missingPron.length} noDesc=${noDesc.length}`);
if (missingPron.length) console.log("missing pron:", missingPron.join(", "));
console.log(`report -> ${REPORT}`);
