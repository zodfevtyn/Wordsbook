import { readFileSync, writeFileSync } from 'node:fs';

const file = 'C:/Users/Lenovo/AppData/Local/Wordsbook/wordsbook.json';
const data = JSON.parse(readFileSync(file, 'utf8'));

const nl = data.languages.find((l) => l.code === 'nl').id;
const es = data.languages.find((l) => l.code === 'es').id;

const cat = (langId, name) => ({ id: `cat-${langId}-${name}`, langId, name });
data.categories = [
  cat(nl, 'Adjectives'),
  cat(nl, 'Everyday'),
  cat(es, 'Adjectives'),
];

let n = 0;
const word = (langId, w, meaning, pron, date, mark, related, cats) => ({
  id: `w${++n}`,
  langId,
  word: w,
  meaning,
  pronunciation: pron,
  description: '',
  date,
  mark,
  related: related ?? [],
  categoryIds: cats ?? [],
});

const adjectives = data.categories[0].id;
const everyday = data.categories[1].id;

data.words = [
  word(nl, 'nieuw', 'adj. new', 'new', '2026-10-13', 0, ['leuke'], [adjectives]),
  word(nl, 'leuk', 'adj. nice', 'louk', '2026-10-13', 0, ['leuke'], [adjectives]),
  word(nl, 'jong', 'adj. young', 'youn', '2026-10-13', 2, ['jonge'], [adjectives]),
  word(nl, 'moeilijk', 'adj. difficult', 'mu-yu-le-k', '2026-10-13', 1, [], [adjectives]),
  word(nl, 'snel', 'adj. fast', 'snel', '2026-10-12', 0, ['snelle'], [adjectives]),
  word(nl, 'groot', 'adj. big', 'ghroht', '2026-10-12', 0, ['grote'], [adjectives]),
  word(nl, 'water', 'n. water', 'vah-ter', '2026-10-10', 0, [], [everyday]),
  word(nl, 'brood', 'n. bread', 'broht', '2026-10-10', 2, [], [everyday]),
  word(nl, 'fiets', 'n. bicycle', 'fis', '2026-10-04', 0, [], [everyday]),
  word(nl, 'huis', 'n. house', 'hey-s', '2026-10-04', 0, ['huizen'], [everyday]),
  word(es, 'nuevo', 'adj. new', 'nweh-boh', '2026-10-13', 0, ['nueva'], [adjectives]),
  word(es, 'joven', 'adj. young', 'hoh-ben', '2026-10-12', 0, [], [adjectives]),
  word(es, 'difícil', 'adj. difficult', 'dee-fee-seel', '2026-10-12', 1, [], [adjectives]),
  word(es, 'agua', 'n. water', 'ah-gwah', '2026-09-18', 0, ['aguas'], []),
  word(es, 'pan', 'n. bread', 'pahn', '2026-09-18', 0, [], []),
];

data.days = [
  { langId: nl, date: '2026-10-13' },
  { langId: nl, date: '2026-10-12' },
  { langId: nl, date: '2026-10-10' },
  { langId: nl, date: '2026-10-04' },
  { langId: nl, date: '2026-10-21' },
  { langId: es, date: '2026-10-13' },
  { langId: es, date: '2026-10-12' },
  { langId: es, date: '2026-09-18' },
];

data.settings.activeLangId = nl;

writeFileSync(file, JSON.stringify(data, null, 2));
console.log(`seeded ${data.words.length} words, ${data.categories.length} categories`);
