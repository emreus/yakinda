import { Redirect, Stack } from 'expo-router';
import { useAuth } from '@/auth/AuthContext';
import { FullScreenLoader } from '@/components/ui';
import { colors } from '@/theme';

export default function AuthLayout() {
  const { status } = useAuth();

  if (status === 'loading') return <FullScreenLoader />;
  if (status === 'signedIn') return <Redirect href="/" />;

  return <Stack screenOptions={{ headerShown: false, contentStyle: { backgroundColor: colors.background } }} />;
}
