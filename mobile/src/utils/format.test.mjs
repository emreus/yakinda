// Çalıştırma: node --test "src/**/*.test.mjs"   (Node 24+, .ts dosyasını doğrudan içe aktarır)
import assert from 'node:assert/strict';
import test from 'node:test';
import { effectiveEndsAt, formatEventBadge, formatEventRange } from './format.ts';
import { uniqueBy } from './list.ts';

const HOUR = 60 * 60 * 1000;
const NOW = Date.UTC(2026, 10, 22, 12, 0, 0);
const at = (hoursFromNow) => new Date(NOW + hoursFromNow * HOUR).toISOString();

/** Varsayılan: 2 saat sonra başlayan, 4 kişilik, 1 kişinin katıldığı aktif etkinlik. */
function event(overrides = {}) {
  return { status: 'Active', startsAt: at(2), endsAt: null, capacity: 4, participantCount: 1, spotsLeft: 3, ...overrides };
}

test('yaklaşan etkinlikte rozet kontenjan durumunu yazar', () => {
  assert.deepEqual(formatEventBadge(event(), NOW), { label: '3 kişi aranıyor', tone: 'success' });
  assert.deepEqual(formatEventBadge(event({ participantCount: 4, spotsLeft: 0 }), NOW), { label: 'Kontenjan dolu', tone: 'danger' });
});

test('başlamış ama bitmemiş etkinlikte "kişi aranıyor" yerine "Başladı" yazar', () => {
  assert.deepEqual(formatEventBadge(event({ startsAt: at(-1) }), NOW), { label: 'Başladı', tone: 'neutral' });
  // Tam başlangıç anı da başlamış sayılır (sunucu da bu andan sonra katılım almaz).
  assert.equal(formatEventBadge(event({ startsAt: at(0) }), NOW).label, 'Başladı');
});

test('bitiş saati yoksa etkinlik başlangıçtan 3 saat sonra bitmiş sayılır', () => {
  assert.equal(effectiveEndsAt(event({ startsAt: at(-4) })), NOW - 1 * HOUR);
  assert.equal(formatEventBadge(event({ startsAt: at(-4) }), NOW).label, 'Bitti');
  assert.equal(formatEventBadge(event({ startsAt: at(-4), endsAt: at(1) }), NOW).label, 'Başladı');
});

test('iptal edilen ve kaldırılan etkinlikte kontenjan yazılmaz', () => {
  assert.deepEqual(formatEventBadge(event({ status: 'Cancelled' }), NOW), { label: 'İptal edildi', tone: 'danger' });
  assert.deepEqual(formatEventBadge(event({ status: 'Removed' }), NOW), { label: 'Kaldırıldı', tone: 'danger' });
  // Geçmişte kalmış olsa da durum önce gelir.
  assert.equal(formatEventBadge(event({ status: 'Cancelled', startsAt: at(-10) }), NOW).label, 'İptal edildi');
});

test('bitiş aynı gündeyse ya da 12 saat içindeyse yalnızca saati yazılır', () => {
  const start = new Date(2026, 10, 22, 19, 0).toISOString();
  const sameDay = formatEventRange(start, new Date(2026, 10, 22, 21, 30).toISOString());
  const pastMidnight = formatEventRange(start, new Date(2026, 10, 23, 1, 0).toISOString());
  assert.match(sameDay, / – \d{2}:\d{2}$/);
  assert.match(pastMidnight, / – \d{2}:\d{2}$/);
});

test('günlerce süren etkinlikte bitişin günü de yazılır', () => {
  const start = new Date(2026, 10, 22, 19, 0).toISOString();
  const end = new Date(2026, 10, 24, 12, 0).toISOString();
  const text = formatEventRange(start, end);
  assert.ok(text.includes('24'), text);
  assert.doesNotMatch(text, / – \d{2}:\d{2}$/);
});

test('bitiş yoksa yalnızca başlangıç yazılır', () => {
  const start = new Date(2026, 10, 22, 19, 0).toISOString();
  assert.ok(!formatEventRange(start, null).includes('–'));
});

test('uniqueBy tekrarları atar, ilk görüleni ve sırayı korur', () => {
  const items = [
    { id: 'a', n: 1 },
    { id: 'b', n: 2 },
    { id: 'a', n: 3 },
    { id: 'c', n: 4 },
  ];
  assert.deepEqual(uniqueBy(items, (item) => item.id), [
    { id: 'a', n: 1 },
    { id: 'b', n: 2 },
    { id: 'c', n: 4 },
  ]);
  assert.deepEqual(uniqueBy([], (item) => item.id), []);
});
