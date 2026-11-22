import { useQueryClient } from '@tanstack/react-query';
import * as SecureStore from 'expo-secure-store';
import { createContext, useCallback, useContext, useEffect, useMemo, useRef, useState, type ReactNode } from 'react';
import { ApiError, setAuthBridge } from '@/api/client';
import { authApi } from '@/api/endpoints';
import type { AuthResponse, AuthUser } from '@/api/types';
import { unregisterPushAsync } from '@/notifications/push';

// Refresh token cihazın güvenli deposunda (iOS Keychain / Android Keystore) saklanır.
// Access token sadece bellekte tutulur; uygulama açılışında refresh token ile yenilenir.
const REFRESH_TOKEN_KEY = 'yakinda.refreshToken';

type AuthStatus = 'loading' | 'signedIn' | 'signedOut';

interface AuthContextValue {
  status: AuthStatus;
  user: AuthUser | null;
  signIn: (email: string, password: string) => Promise<void>;
  /** acceptTerms: kullanım koşulları ve gizlilik politikası kabul edildi (zorunlu) */
  signUp: (email: string, password: string, displayName: string, acceptTerms: boolean) => Promise<void>;
  /** Şifre sıfırlama kodu doğruysa yeni şifreyle giriş yapar. */
  resetPassword: (email: string, code: string, newPassword: string) => Promise<void>;
  signOut: () => Promise<void>;
}

const AuthContext = createContext<AuthContextValue | null>(null);

export function AuthProvider({ children }: { children: ReactNode }) {
  const queryClient = useQueryClient();
  const [status, setStatus] = useState<AuthStatus>('loading');
  const [user, setUser] = useState<AuthUser | null>(null);
  const accessTokenRef = useRef<string | null>(null);
  const refreshInFlight = useRef<Promise<boolean> | null>(null);

  const applySession = useCallback(async (session: AuthResponse) => {
    accessTokenRef.current = session.accessToken;
    await SecureStore.setItemAsync(REFRESH_TOKEN_KEY, session.refreshToken);
    setUser(session.user);
    setStatus('signedIn');
  }, []);

  const clearSession = useCallback(async () => {
    accessTokenRef.current = null;
    await SecureStore.deleteItemAsync(REFRESH_TOKEN_KEY);
    setUser(null);
    setStatus('signedOut');
    queryClient.clear();
  }, [queryClient]);

  /** Aynı anda birden fazla 401 gelirse tek bir refresh isteği atılır. */
  const refreshAccessToken = useCallback((): Promise<boolean> => {
    if (refreshInFlight.current) return refreshInFlight.current;

    const task = (async () => {
      try {
        const storedToken = await SecureStore.getItemAsync(REFRESH_TOKEN_KEY);
        if (!storedToken) return false;
        await applySession(await authApi.refresh(storedToken));
        return true;
      } catch (error) {
        // Token geçersiz/süresi dolmuş => oturumu kapat. Ağ hatasında token'a dokunma.
        if (error instanceof ApiError && (error.status === 401 || error.status === 400)) {
          await clearSession();
        }
        return false;
      } finally {
        refreshInFlight.current = null;
      }
    })();

    refreshInFlight.current = task;
    return task;
  }, [applySession, clearSession]);

  useEffect(() => {
    setAuthBridge({ getAccessToken: () => accessTokenRef.current, refreshAccessToken });
    return () => setAuthBridge(null);
  }, [refreshAccessToken]);

  // Uygulama açılışı: kayıtlı oturum varsa sessizce yenile.
  useEffect(() => {
    let cancelled = false;
    refreshAccessToken().then((ok) => {
      if (!cancelled && !ok) setStatus((current) => (current === 'loading' ? 'signedOut' : current));
    });
    return () => {
      cancelled = true;
    };
    // sadece ilk açılışta
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, []);

  const signIn = useCallback(
    async (email: string, password: string) => applySession(await authApi.login(email, password)),
    [applySession],
  );

  const resetPassword = useCallback(
    async (email: string, code: string, newPassword: string) =>
      applySession(await authApi.resetPassword(email, code, newPassword)),
    [applySession],
  );

  const signUp = useCallback(
    async (email: string, password: string, displayName: string, acceptTerms: boolean) =>
      applySession(await authApi.register(email, password, displayName, acceptTerms)),
    [applySession],
  );

  /**
   * Önce bu cihazın push token'ı silinir (access token hâlâ geçerliyken), sonra refresh token sunucuda
   * iptal edilir ve oturum kapanır.
   */
  const signOut = useCallback(async () => {
    try {
      await unregisterPushAsync();
    } catch {
      // Ağ hatası: token sunucuda kalır; bu cihazda başka hesapla giriş yapılınca o hesaba taşınır.
    }
    try {
      const storedToken = await SecureStore.getItemAsync(REFRESH_TOKEN_KEY);
      if (storedToken) await authApi.logout(storedToken);
    } catch {
      // Ağ hatası: token sunucuda süresi dolana kadar kalır; cihazdan yine de silinir.
    }
    await clearSession();
  }, [clearSession]);

  const value = useMemo<AuthContextValue>(
    () => ({ status, user, signIn, signUp, resetPassword, signOut }),
    [status, user, signIn, signUp, resetPassword, signOut],
  );

  return <AuthContext.Provider value={value}>{children}</AuthContext.Provider>;
}

export function useAuth(): AuthContextValue {
  const context = useContext(AuthContext);
  if (!context) throw new Error('useAuth, AuthProvider içinde kullanılmalı.');
  return context;
}
