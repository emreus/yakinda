// Çalıştırma: node --test "src/**/*.test.mjs"   (Node 24+, .ts dosyasını doğrudan içe aktarır)
import assert from 'node:assert/strict';
import test from 'node:test';
import { isUuid, seg } from './path.ts';

const EVENT = '0198c0de-1111-7222-8333-444455556666';
const base = 'https://api.example';

/** Uygulamanın adresi kurma biçimi: `${API_URL}/api/events/${seg(id)}/messages/read` */
function chatReadUrl(id) {
  return new URL(`${base}/api/events/${seg(id)}/messages/read`);
}

// Geçerli bir kimlik kodlamadan etkilenmez: kurulan adres beklenen yolla birebir aynıdır.
test('geçerli kimlik adresi değiştirmez', () => {
  assert.equal(chatReadUrl(EVENT).pathname, `/api/events/${EVENT}/messages/read`);
});

// Kimliğin sonuna "/cancel?" eklenirse bu karakterler kodlanır ve değer tek bir yol parçası olarak
// kalır: sorgu bölümü oluşmaz, adres yine /messages/read ile biter ve parça sayısı değişmez.
test('kimliğe eklenen "/…?" başka bir uca istek attıramaz', () => {
  // Kodlanmasaydı: POST /api/events/<id>/cancel?/messages/read  → etkinlik iptal olurdu
  const url = chatReadUrl(`${EVENT}/cancel?`);
  assert.equal(url.search, '');
  assert.ok(url.pathname.endsWith('/messages/read'));
  assert.equal(url.pathname.split('/').length, 6); // "", api, events, <tek parça>, messages, read
});

// Üst klasöre çıkma denemelerinin (düz ya da kodlanmış "../", "#" ile kesme, araya ikinci bir kimlik
// ekleme) hiçbiri işe yaramaz: adres /api/events/ altında kalır, /messages/read ile biter; sorgu ve
// çapa bölümü oluşmaz.
test('"../" ile başka kaynağa çıkılamaz', () => {
  for (const id of ['../users/me/delete?x=', '..%2Fusers', '../../health#', `${EVENT}/../${EVENT}/leave?`]) {
    const url = chatReadUrl(id);
    assert.ok(url.pathname.startsWith('/api/events/'), `${id} → ${url.pathname}`);
    assert.ok(url.pathname.endsWith('/messages/read'), `${id} → ${url.pathname}`);
    assert.equal(url.pathname.split('/').length, 6, `${id} → ${url.pathname}`);
    assert.equal(url.search, '');
    assert.equal(url.hash, '');
  }
});

// ".", ".." ve boş değer "-" ile değiştirilir; değiştirilmeseydi adres çözümlenirken yol kısalırdı.
test('".", ".." ve boş değer adresi kısaltamaz', () => {
  for (const id of ['.', '..', '']) {
    assert.equal(chatReadUrl(id).pathname, '/api/events/-/messages/read');
  }
});

// isUuid küçük ve büyük harfli kimliği kabul eder; sonuna ek yapılmış, başında boşluk ya da sonunda
// satır sonu olan değeri, sıradan metni, boş değeri ve metin olmayan türleri reddeder.
test('isUuid yalnız kayıt kimliklerini kabul eder', () => {
  assert.equal(isUuid(EVENT), true);
  assert.equal(isUuid(EVENT.toUpperCase()), true);
  for (const value of [`${EVENT}/cancel?`, ` ${EVENT}`, `${EVENT}\n`, 'chat', '', undefined, null, 42, [EVENT]]) {
    assert.equal(isUuid(value), false, String(value));
  }
});
