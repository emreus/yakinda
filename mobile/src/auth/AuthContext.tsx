import { useQueryClient } from '@tanstack/react-query';
import * as SecureStore from 'expo-secure-store';
import { createContext, useCallback, useContext, useEffect, useMemo, useRef, useState, type ReactNode } from 'react';
import { AppState } from 'react-native';
import { ApiError, setAuthBridge } from '@/api/client';
import { authApi } from '@/api/endpoints';
import type { AuthResponse, AuthUser } from '@/api/types';
import { unregisterPushAsync } from '@/notifications/push';

// Refresh token cihazın güvenli deposunda (iOS Keychain / Android Keystore) saklanır.
// Access token sadece bellekte tutulur; uygulama açılışında refresh token ile yenilenir.
const REFRESH_TOKEN_KEY = 'yakinda.refreshToken';

// Açılışta sunucuya ulaşılamazsa ikinci denemeden önce beklenen süre.
const STARTUP_RETRY_DELAY_MS = 1500;

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

/**
 * Oturum durumunu (yükleniyor, giriş yapılmış, çıkış yapılmış) ve kullanıcıyı tutar; giriş, kayıt, şifre
 * sıfırlama ve çıkış fonksiyonlarını bütün uygulamaya verir. Açılışta kayıtlı refresh token ile sessizce
 * giriş dener ve HTTP katmanına token köprüsünü kurar. Açılışta sunucuya ulaşılamadıysa kayıtlı oturum
 * silinmez; uygulama yeniden öne geldiğinde giriş tekrar denenir.
 */
export function AuthProvider({ children }: { children: ReactNode }) {
  const queryClient = useQueryClient();
  const [status, setStatus] = useState<AuthStatus>('loading');
  const [user, setUser] = useState<AuthUser | null>(null);
  const accessTokenRef = useRef<string | null>(null);
  const refreshInFlight = useRef<Promise<boolean> | null>(null);

  // Yeni oturumu uygular: erişim token'ını bellekte, refresh token'ı cihazın güvenli deposunda saklar,
  // kullanıcıyı ve "giriş yapılmış" durumunu ayarlar.
  const applySession = useCallback(async (session: AuthResponse) => {
    accessTokenRef.current = session.accessToken;
    await SecureStore.setItemAsync(REFRESH_TOKEN_KEY, session.refreshToken);
    setUser(session.user);
    setStatus('signedIn');
  }, []);

  // Oturumu kapatır: token'ları siler, kullanıcıyı boşaltır ve sorgu önbelleğini temizler (bir sonraki
  // kullanıcı öncekinin verisini görmesin).
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

    // Yenileme isteği. Aynı anda gelen bütün çağrılar bu tek Promise'i bekler.
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

  // Uygulama açılışı: kayıtlı oturum varsa sessizce yenile. Yenileme başarısız olduğu halde token hâlâ
  // cihazdaysa sunucu onu reddetmemiş, yalnızca ulaşılamamıştır (anlık bağlantı kopukluğu); kısa bir
  // aradan sonra bir kez daha denenir, o da olmazsa giriş ekranı gösterilir.
  useEffect(() => {
    let cancelled = false;
    (async () => {
      let ok = await refreshAccessToken();
      if (!ok && !cancelled && (await SecureStore.getItemAsync(REFRESH_TOKEN_KEY))) {
        await new Promise((resolve) => setTimeout(resolve, STARTUP_RETRY_DELAY_MS));
        if (!cancelled) ok = await refreshAccessToken();
      }
      if (!cancelled && !ok) setStatus((current) => (current === 'loading' ? 'signedOut' : current));
    })();
    return () => {
      cancelled = true;
    };
    // sadece ilk açılışta
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, []);

  // Giriş ekranındayken uygulama yeniden öne gelirse kayıtlı oturum bir kez daha denenir: açılışta
  // internet yokken giriş ekranına düşen kullanıcı, bağlantı gelince yeniden şifre yazmak zorunda kalmaz.
  // Kullanıcı kendi çıkış yaptıysa token silinmiştir; bu durumda sunucuya istek gitmez.
  useEffect(() => {
    if (status !== 'signedOut') return;
    const subscription = AppState.addEventListener('change', (next) => {
      if (next === 'active') void refreshAccessToken();
    });
    return () => subscription.remove();
  }, [status, refreshAccessToken]);

  // Giriş: API'den dönen oturumu uygular. Hata olursa fırlatır; ekran hata metnini gösterir.
  const signIn = useCallback(
    async (email: string, password: string) => applySession(await authApi.login(email, password)),
    [applySession],
  );

  // Kodla şifre sıfırlama: başarılı olursa sunucu oturum döner ve kullanıcı giriş yapmış olur.
  const resetPassword = useCallback(
    async (email: string, code: string, newPassword: string) =>
      applySession(await authApi.resetPassword(email, code, newPassword)),
    [applySession],
  );

  // Kayıt: hesabı açar ve dönen oturumu uygular.
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

/** Oturum bilgisine ve giriş/çıkış fonksiyonlarına erişir; AuthProvider dışında çağrılırsa hata fırlatır. */
export function useAuth(): AuthContextValue {
  const context = useContext(AuthContext);
  if (!context) throw new Error('useAuth, AuthProvider içinde kullanılmalı.');
  return context;
}
