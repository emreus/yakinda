import { useRouter, useSegments } from 'expo-router';
import { useEffect } from 'react';
import { useAuth } from './AuthContext';

/**
 * Oturum kapalıyken giriş ekranları dışında bir ekranda kalınmasını önler. Sekmelerin düzeni giriş
 * ekranına kendisi yönlendirir, ama o yönlendirme yalnızca sekmeler öndeyken çalışır. Sekmelerin üstünde
 * başka bir ekran açıkken oturum kapanırsa (hesap silme, oturumun sona ermesi) ya da uygulama paylaşılan
 * bir bağlantıyla oturumsuz açılırsa kullanıcı o ekranda kalırdı. Burada üstteki ekranlar kapatılır ve
 * sekmelerin yönlendirmesi devreye girer; altta dönülecek ekran yoksa doğrudan giriş ekranına geçilir.
 */
export function SignedOutGuard() {
  const { status } = useAuth();
  const router = useRouter();
  const segments = useSegments();
  const group: string | undefined = segments[0];
  // Gezinme her değiştiğinde yeniden bakılır: üstteki ekranlar kapandıktan sonra hâlâ sekmelerde ya da
  // giriş ekranında değilsek bir adım daha atılır.
  const path = segments.join('/');

  useEffect(() => {
    if (status !== 'signedOut' || !group || group === '(auth)' || group === '(tabs)') return;
    if (router.canDismiss()) router.dismissAll();
    else router.replace('/login');
  }, [status, group, path, router]);

  return null;
}
