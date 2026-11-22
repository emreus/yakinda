import { router } from 'expo-router';

/**
 * Önceki ekrana döner; dönülecek ekran yoksa (uygulama paylaşılan bir bağlantıyla doğrudan bu ekranda
 * açıldıysa) ana sayfaya gider. Böylece "Geri dön" butonları ve işlem sonrası geri dönüşler hiçbir
 * durumda işlevsiz kalmaz.
 */
export function goBackOrHome(): void {
  if (router.canGoBack()) router.back();
  else router.replace('/');
}
