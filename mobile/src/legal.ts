import Constants from 'expo-constants';
import { Alert, Linking } from 'react-native';
import { API_URL } from '@/api/client';

/**
 * Gizlilik politikası, kullanım koşulları ve hesap silme sayfaları backend'den sunulur
 * (/legal/...). Mağaza başvurusunda da aynı adresler verilir.
 */
export type LegalPage = 'privacy' | 'terms' | 'delete-account';

/** Yasal sayfanın tam adresini üretir: API adresi + /legal/sayfa. */
export function legalUrl(page: LegalPage): string {
  return `${API_URL}/legal/${page}`;
}

/** Yasal sayfayı tarayıcıda açar; açılamazsa adresi bir uyarıyla gösterir. */
export function openLegalPage(page: LegalPage) {
  Linking.openURL(legalUrl(page)).catch(() => Alert.alert('Sayfa açılamadı', legalUrl(page)));
}

/** app.json → expo.extra.supportEmail (yayınlamadan önce kendi adresinizle değiştirin). */
export const SUPPORT_EMAIL: string =
  (Constants.expoConfig?.extra?.supportEmail as string | undefined) ?? 'destek@ornek.com';

/**
 * E-posta uygulamasını destek adresi ve konu satırı dolu olarak açar; açılamazsa adresi uyarıyla gösterir.
 */
export function contactSupport(subject = 'Yakında destek') {
  const url = `mailto:${SUPPORT_EMAIL}?subject=${encodeURIComponent(subject)}`;
  Linking.openURL(url).catch(() => Alert.alert('E-posta uygulaması açılamadı', `Bize şu adresten yazabilirsin: ${SUPPORT_EMAIL}`));
}
