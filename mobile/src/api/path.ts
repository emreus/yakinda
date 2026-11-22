/**
 * API adresine eklenen değişken parçalar (etkinlik, kullanıcı, mesaj kimlikleri) için yardımcılar.
 *
 * Kimlikler çoğu zaman ekrana bağlantıyla gelir (yakinda://event/..., https://<api>/e/..., bildirim).
 * Bağlantıyı kullanıcı değil başkası hazırlamış olabilir. Kimlik adrese olduğu gibi eklenirse
 * "abc/cancel?" gibi bir değer, kullanıcının oturumuyla bambaşka bir uca istek atılmasına yol açar
 * (ör. kendi etkinliğini iptal ettirmek). Bu yüzden her parça `seg` ile kodlanır: "/", "?", "#" gibi
 * karakterler adresin yapısını değiştiremez; sunucu da kimlik biçiminde olmayan değeri 404 ile yanıtlar.
 *
 * Bu dosya başka dosya içe aktarmaz; testi (path.test.mjs) Node ile doğrudan çalışır.
 */

const UUID = /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i;

/** Değer bir kayıt kimliği (UUID) biçiminde mi? Bağlantıdan gelen kimlikler kullanılmadan önce kontrol edilir. */
export function isUuid(value: unknown): value is string {
  return typeof value === 'string' && UUID.test(value);
}

/**
 * Değeri adrese tek bir yol parçası olarak güvenle eklenecek hâle getirir: özel karakterleri kodlar;
 * ".", ".." ve boş değeri hiçbir kayda karşılık gelmeyen "-" ile değiştirir.
 */
export function seg(value: string): string {
  const encoded = encodeURIComponent(String(value));
  // "." ve ".." kodlanamaz ve adres çözümlenirken üst klasöre çıkar; boş değer ise parçayı yutar.
  // Üçü de hiçbir kayda karşılık gelmeyen bir değerle değiştirilir.
  return encoded === '' || encoded === '.' || encoded === '..' ? '-' : encoded;
}
