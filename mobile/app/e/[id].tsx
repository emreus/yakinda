import { Redirect, useLocalSearchParams } from 'expo-router';
import { isUuid } from '@/api/path';

/**
 * Paylaşım bağlantısı (https://<api>/e/{id}) Universal Links / App Links ile uygulamada açılınca
 * buraya gelir; etkinlik detayına yönlendirir. Bağlantıyı başkası hazırlamış olabilir: kimlik biçiminde
 * olmayan değer (ör. başka bir ekranın adresi) yönlendirmede kullanılmaz, ana sayfa açılır.
 */
export default function SharedEventRedirect() {
  const { id } = useLocalSearchParams<{ id: string }>();
  return <Redirect href={isUuid(id) ? `/event/${id}` : '/'} />;
}
