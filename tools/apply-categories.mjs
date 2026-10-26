import fs from "node:fs";
import path from "node:path";

const DATA = "C:/Users/Lenovo/AppData/Local/Wordsbook/wordsbook.json";

const TAXONOMY = [
  {
    slug: "greetings",
    name: "Greetings & Politeness",
    words: [
      "hallo", "hoi", "goedemorgen", "goedendag", "goedenavond", "dag", "doei",
      "tot ziens", "welterusten", "alsjeblieft", "alstublieft", "bedankt",
      "dank je", "dank je wel", "het spijt me", "sorry", "pardon",
      "hoe gaat het?", "hoe heet je?", "ja", "nee", "oké",
    ],
  },
  {
    slug: "quantity",
    name: "Numbers & Quantity",
    words: [
      "twee", "alle", "allerlei", "elke", "enige", "enkele", "geen", "ieder",
      "sommige", "verschillende", "veel", "weinig", "genoeg", "beetje",
    ],
  },
  {
    slug: "colors",
    name: "Colors",
    words: [
      "blauw", "bruin", "geel", "grijs", "groen", "kleurrijk", "oranje",
      "paars", "rood", "roze", "wit", "zwart",
    ],
  },
  {
    slug: "animals",
    name: "Animals",
    words: [
      "dier", "beer", "eend", "gans", "hert", "hond", "hoorn", "huisdier",
      "kat", "kip", "koe", "konijn", "krab", "muis", "neushoorn", "olifant",
      "paard", "schaap", "schildpad", "spin", "staart", "uil", "varken", "vis", "vogel",
    ],
  },
  {
    slug: "clothes",
    name: "Clothes & Accessories",
    words: [
      "bril", "broek", "dragen", "hemd", "hoed", "jas", "jurk", "kleren",
      "klomp", "ondergoed", "pak", "rok", "schoen", "sok", "tas", "trui",
    ],
  },
  {
    slug: "food",
    name: "Food & Drink",
    words: [
      "aardbei", "appel", "avondeten", "banaan", "bier", "bord", "boterham",
      "brood", "dorst", "drinken", "ei", "eten", "fruit", "glas", "groente",
      "honger", "kaas", "koffie", "koken", "maaltijd", "menu",
      "middageten", "ontbijt", "pasta", "peper", "rijst", "sap", "sinaasappel",
      "soep", "suiker", "thee", "tomaat", "vlees", "water", "wijn", "zout",
    ],
  },
  {
    slug: "people",
    name: "People & Family",
    words: [
      "baby", "broer", "dochter", "familie", "gezin", "jongen", "kind", "man",
      "meisje", "meneer", "mens", "mevrouw", "moeder", "naam", "ouders",
      "vader", "vriend", "vriendin", "vrouw", "zoon", "zus",
    ],
  },
  {
    slug: "home",
    name: "Home & Objects",
    words: ["boek", "huis", "krant", "tafel"],
  },
  {
    slug: "languages",
    name: "Languages & Culture",
    words: ["engels", "nederlands", "vlag"],
  },
  {
    slug: "verbs",
    name: "Verbs",
    words: [
      "antwoorden", "doen", "gaan", "geven", "hebben", "heten", "horen",
      "kennen", "komen", "krijgen", "lezen", "lopen", "maken", "schrijven",
      "slapen", "spelen", "spreken", "stellen", "willen", "wonen", "zien",
      "zwemmen",
    ],
  },
  {
    slug: "adjectives",
    name: "Adjectives",
    words: [
      "belangrijk", "donker", "duur", "goed", "goedkoop", "groot", "heet",
      "interessant", "jong", "klein", "kort", "koud", "lang", "langzaam",
      "leeg", "lekker", "leuk", "licht", "makkelijk", "moeilijk", "nieuw",
      "oud", "rustig", "schoon", "slecht", "snel", "sterk", "vers", "vol",
      "warm", "zwaar", "zwak",
    ],
  },
  {
    slug: "pronouns",
    name: "Pronouns",
    words: [
      "alles", "dat", "haar", "hem", "hun", "hij", "iedereen", "iemand",
      "iets", "je", "jij", "jou", "jouw", "me", "men", "mijn", "ons", "onze",
      "u", "uw", "van jou", "van jullie", "van mij", "van ons", "wij", "ze",
      "zij",
    ],
  },
  {
    slug: "questions",
    name: "Question Words",
    words: [
      "hoe", "hoeveel", "waar", "waarom", "wanneer", "wat", "welke", "wie",
      "wiens", "vraag",
    ],
  },
  {
    slug: "adverbs",
    name: "Adverbs",
    words: ["daar", "hier", "misschien", "net", "niet", "nu", "ook", "soms", "wel"],
  },
  {
    slug: "prepositions",
    name: "Prepositions",
    words: [
      "aan", "achter", "bij", "door", "in", "met", "na", "naar", "naast",
      "onder", "op", "over", "tegen", "tijdens", "tussen", "uit", "van",
      "vanwege", "voor", "zonder", "zoals",
    ],
  },
  {
    slug: "conjunctions",
    name: "Conjunctions",
    words: ["dus", "maar", "of", "want"],
  },
];

const ES_TAXONOMY = [
  { slug: "adjectives", name: "Adjectives", words: ["nuevo", "joven", "difícil"] },
  { slug: "food", name: "Food & Drink", words: ["agua", "pan"] },
];

const catId = (langId, slug) => `cat-${langId}-${slug}`;

const data = JSON.parse(fs.readFileSync(DATA, "utf8"));
const nl = data.languages.find((l) => l.name === "Nederlands");
const es = data.languages.find((l) => l.name === "Español");
if (!nl || !es) throw new Error("expected Nederlands and Español languages");

const wordIndex = new Map();
for (const cat of TAXONOMY) {
  for (const word of cat.words) {
    if (wordIndex.has(word)) throw new Error(`duplicate taxonomy word: ${word}`);
    wordIndex.set(word, cat.slug);
  }
}

const nlWords = data.words.filter((w) => w.langId === nl.id);
const missing = [];
const assigned = new Map();
for (const w of nlWords) {
  const slug = w.word === "zijn"
    ? (w.meaning.startsWith("pron") ? "pronouns" : "verbs")
    : wordIndex.get(w.word);
  if (!slug) {
    missing.push(`${w.word} | ${w.meaning}`);
    continue;
  }
  w.categoryIds = [catId(nl.id, slug)];
  assigned.set(w.id, slug);
}
if (missing.length) {
  console.error("UNMAPPED NL WORDS:\n" + missing.join("\n"));
  process.exit(1);
}

const usedWords = new Set(nlWords.map((w) => w.word));
const extra = [...wordIndex.keys()].filter((w) => !usedWords.has(w));
if (extra.length) {
  console.error("TAXONOMY WORDS NOT IN DATA: " + extra.join(", "));
  process.exit(1);
}

const esWords = data.words.filter((w) => w.langId === es.id);
const esIndex = new Map();
for (const cat of ES_TAXONOMY) {
  for (const word of cat.words) esIndex.set(word, cat.slug);
}
for (const w of esWords) {
  const slug = esIndex.get(w.word);
  if (!slug) {
    console.error(`UNMAPPED ES WORD: ${w.word} | ${w.meaning}`);
    process.exit(1);
  }
  w.categoryIds = [catId(es.id, slug)];
}

const otherCategories = data.categories.filter(
  (c) => c.langId !== nl.id && c.langId !== es.id,
);
data.categories = [
  ...otherCategories,
  ...TAXONOMY.map((c) => ({ id: catId(nl.id, c.slug), langId: nl.id, name: c.name })),
  ...ES_TAXONOMY.map((c) => ({ id: catId(es.id, c.slug), langId: es.id, name: c.name })),
];

const counts = new Map();
for (const w of nlWords) {
  const slug = assigned.get(w.id);
  counts.set(slug, (counts.get(slug) || 0) + 1);
}
const total = [...counts.values()].reduce((a, b) => a + b, 0);
if (total !== nlWords.length) {
  console.error(`count mismatch: ${total} vs ${nlWords.length}`);
  process.exit(1);
}

const now = new Date();
const pad = (n) => String(n).padStart(2, "0");
const stamp = `${now.getFullYear()}${pad(now.getMonth() + 1)}${pad(now.getDate())}${pad(now.getHours())}${pad(now.getMinutes())}${pad(now.getSeconds())}`;
const backup = DATA.replace(/\.json$/, `.backup-${stamp}.json`);
fs.copyFileSync(DATA, backup);
if (fs.statSync(backup).size !== fs.statSync(DATA).size) {
  throw new Error("backup size mismatch");
}

const tmp = path.join(path.dirname(DATA), `wordsbook.tmp-${process.pid}.json`);
fs.writeFileSync(tmp, JSON.stringify(data, null, 2) + "\n", "utf8");
fs.renameSync(tmp, DATA);

console.log(`backup: ${backup}`);
console.log(`NL words: ${nlWords.length}, categories: ${TAXONOMY.length}`);
for (const cat of TAXONOMY) {
  console.log(`${String(counts.get(cat.slug) || 0).padStart(3)}  ${cat.name}`);
}
console.log(`ES words: ${esWords.length}, categories: ${ES_TAXONOMY.length}`);
console.log(`total categories: ${data.categories.length}`);
