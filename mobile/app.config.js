/**
 * app.json'un üzerine derleme anında eklenen ayarlar.
 *
 * APP_LINK_DOMAIN (ör. api.yakinda.app) verilirse paylaşım bağlantıları (https://<alan-adı>/e/{id})
 * doğrudan uygulamada açılır: iOS Universal Links (associatedDomains) ve Android App Links (autoVerify).
 * Sunucu, doğrulama dosyalarını (/.well-known/...) API ayarlarıyla sunar; README → "Paylaşım bağlantısı".
 * Değerler eas.json'daki derleme profillerinden ve EAS ortam değişkenlerinden gelir; Expo Go'da/yerelde boş kalır
 * ve hiçbir şey eklenmez.
 */
module.exports = ({ config }) => {
  const domain = process.env.APP_LINK_DOMAIN;
  // Android'de harita için Google Maps anahtarı (Expo Go kendi anahtarını kullanır; mağaza derlemesinde şart).
  // EAS ortam değişkeni olarak verilir: README → "Mağaza derlemesi".
  const mapsKey = process.env.GOOGLE_MAPS_API_KEY;

  const ios = { ...config.ios };
  const android = { ...config.android };

  if (domain) {
    ios.associatedDomains = [`applinks:${domain}`];
    android.intentFilters = [
      {
        action: 'VIEW',
        autoVerify: true,
        data: [{ scheme: 'https', host: domain, pathPrefix: '/e/' }],
        category: ['BROWSABLE', 'DEFAULT'],
      },
    ];
  }

  if (mapsKey) {
    android.config = { ...android.config, googleMaps: { apiKey: mapsKey } };
  }

  return { ...config, ios, android };
};
