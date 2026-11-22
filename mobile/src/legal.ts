import Constants from 'expo-constants';
import { Alert, Linking } from 'react-native';
import { API_URL } from '@/api/client';

/**
 * Gizlilik politikası, kullanım koşulları ve hesap silme sayfaları backend'den sunulur
 * (/legal/...). Mağaza başvurusunda da aynı adresler verilir.
 */
export type LegalPage = 'privacy' | 'terms' | 'delete-account';

export function legalUrl(page: LegalPage): string {
  return `${API_URL}/legal/${page}`;
}

export function openLegalPage(page: LegalPage) {
  Linking.openURL(legalUrl(page)).catch(() => Alert.alert('Sayfa açılamadı', legalUrl(page)));
}

/** app.json → expo.extra.supportEmail (yayınlamadan önce kendi adresinizle değiştirin). */
export const SUPPORT_EMAIL: string =
  (Constants.expoConfig?.extra?.supportEmail as string | undefined) ?? 'destek@ornek.com';

export function contactSupport(subject = 'Yakında destek') {
  const url = `mailto:${SUPPORT_EMAIL}?subject=${encodeURIComponent(subject)}`;
  Linking.openURL(url).catch(() => Alert.alert('E-posta uygulaması açılamadı', `Bize şu adresten yazabilirsin: ${SUPPORT_EMAIL}`));
}
