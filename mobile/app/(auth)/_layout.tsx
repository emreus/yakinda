import { Redirect, Stack } from 'expo-router';
import { useAuth } from '@/auth/AuthContext';
import { FullScreenLoader } from '@/components/ui';
import { colors } from '@/theme';

/**
 * Giriş ekranlarının ortak düzeni. Oturum durumu yüklenirken gösterge çıkar; kullanıcı giriş yapmışsa
 * ana sayfaya yönlendirir, yapmamışsa giriş, kayıt ve şifre sıfırlama ekranlarını gösterir.
 */
export default function AuthLayout() {
  const { status } = useAuth();

  if (status === 'loading') return <FullScreenLoader />;
  if (status === 'signedIn') return <Redirect href="/" />;

  return <Stack screenOptions={{ headerShown: false, contentStyle: { backgroundColor: colors.background } }} />;
}
