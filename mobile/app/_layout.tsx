import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { Stack } from 'expo-router';
import { StatusBar } from 'expo-status-bar';
import { useState } from 'react';
import { ApiError } from '@/api/client';
import { AuthProvider } from '@/auth/AuthContext';
import { SignedOutGuard } from '@/auth/SignedOutGuard';
import { colors } from '@/theme';

// Uygulama paylaşılan bir bağlantıyla (ör. etkinlik detayı) doğrudan açıldığında altına sekmeler yerleşir;
// böylece geri tuşu ana sayfaya döner. Bu olmadan ekranın altında hiçbir şey olmaz: başlıkta geri oku
// çıkmaz ve iOS'ta ana sayfaya dönmenin yolu kalmaz.
export const unstable_settings = {
  anchor: '(tabs)',
};

/**
 * Uygulamanın kök düzeni. Veri önbelleğini (QueryClient: veri 30 saniye taze sayılır, 4xx hatalarında
 * yeniden denenmez) ve oturum sağlayıcısını kurar; ekran yığınını ve başlıklarını tanımlar.
 */
export default function RootLayout() {
  const [queryClient] = useState(
    () =>
      new QueryClient({
        defaultOptions: {
          queries: {
            staleTime: 30_000,
            // 4xx hatalarında tekrar denemenin anlamı yok
            retry: (failureCount, error) =>
              !(error instanceof ApiError && error.status >= 400 && error.status < 500) && failureCount < 2,
          },
        },
      }),
  );

  return (
    <QueryClientProvider client={queryClient}>
      <AuthProvider>
        <StatusBar style="dark" />
        <SignedOutGuard />
        <Stack
          screenOptions={{
            headerTintColor: colors.primary,
            headerTitleStyle: { color: colors.text },
            headerBackTitle: 'Geri',
            contentStyle: { backgroundColor: colors.background },
          }}
        >
          <Stack.Screen name="(tabs)" options={{ headerShown: false }} />
          <Stack.Screen name="(auth)" options={{ headerShown: false }} />
          <Stack.Screen name="event/[id]" options={{ title: 'Etkinlik' }} />
          <Stack.Screen name="user/[id]" options={{ title: 'Profil' }} />
        </Stack>
      </AuthProvider>
    </QueryClientProvider>
  );
}
