import { Redirect, useLocalSearchParams } from 'expo-router';

/**
 * Paylaşım bağlantısı (https://<api>/e/{id}) Universal Links / App Links ile uygulamada açılınca
 * buraya gelir; etkinlik detayına yönlendirir.
 */
export default function SharedEventRedirect() {
  const { id } = useLocalSearchParams<{ id: string }>();
  return <Redirect href={`/event/${id}`} />;
}
