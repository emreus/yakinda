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

test('geçerli kimlik adresi değiştirmez', () => {
  assert.equal(chatReadUrl(EVENT).pathname, `/api/events/${EVENT}/messages/read`);
});

test('kimliğe eklenen "/…?" başka bir uca istek attıramaz', () => {
  // Kodlanmasaydı: POST /api/events/<id>/cancel?/messages/read  → etkinlik iptal olurdu
  const url = chatReadUrl(`${EVENT}/cancel?`);
  assert.equal(url.search, '');
  assert.ok(url.pathname.endsWith('/messages/read'));
  assert.equal(url.pathname.split('/').length, 6); // "", api, events, <tek parça>, messages, read
});

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

test('".", ".." ve boş değer adresi kısaltamaz', () => {
  for (const id of ['.', '..', '']) {
    assert.equal(chatReadUrl(id).pathname, '/api/events/-/messages/read');
  }
});

test('isUuid yalnız kayıt kimliklerini kabul eder', () => {
  assert.equal(isUuid(EVENT), true);
  assert.equal(isUuid(EVENT.toUpperCase()), true);
  for (const value of [`${EVENT}/cancel?`, ` ${EVENT}`, `${EVENT}\n`, 'chat', '', undefined, null, 42, [EVENT]]) {
    assert.equal(isUuid(value), false, String(value));
  }
});
