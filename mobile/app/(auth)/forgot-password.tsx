import { Link, useLocalSearchParams } from 'expo-router';
import { useState } from 'react';
import { KeyboardAvoidingView, Platform, ScrollView, StyleSheet, Text } from 'react-native';
import { SafeAreaView } from 'react-native-safe-area-context';
import { ApiError, errorMessage } from '@/api/client';
import { authApi } from '@/api/endpoints';
import { useAuth } from '@/auth/AuthContext';
import { Button, TextField } from '@/components/ui';
import { colors, font, spacing } from '@/theme';

/**
 * İki adım: 1) e-posta → 6 haneli kod gönderilir, 2) kod + yeni şifre.
 * Başarılı olunca kullanıcı giriş yapmış olur; (auth) düzeni ana ekrana yönlendirir.
 */
export default function ForgotPasswordScreen() {
  const params = useLocalSearchParams<{ email?: string }>();
  const { resetPassword } = useAuth();
  const [step, setStep] = useState<'email' | 'code'>('email');
  const [email, setEmail] = useState(params.email ?? '');
  const [code, setCode] = useState('');
  const [password, setPassword] = useState('');
  const [error, setError] = useState<string | null>(null);
  const [info, setInfo] = useState<string | null>(null);
  const [loading, setLoading] = useState(false);

  // Birinci adım: sıfırlama kodu ister. Sunucu hesap olsa da olmasa da aynı yanıtı verdiği için bilgi
  // metni de buna göre yazılır; sonra kod adımına geçilir.
  const sendCode = async () => {
    setError(null);
    setLoading(true);
    try {
      await authApi.forgotPassword(email.trim());
      setInfo(`Bu adrese kayıtlı bir hesap varsa ${email.trim()} adresine 6 haneli bir kod gönderdik. Kod 15 dakika geçerli.`);
      setStep('code');
    } catch (e) {
      setError(errorMessage(e));
    } finally {
      setLoading(false);
    }
  };

  // İkinci adım: kod ve yeni şifreyle sıfırlar (şifre 8 karakterden kısaysa istek atılmaz). Başarıda
  // kullanıcı giriş yapmış olur. Kodun süresi dolduysa ya da deneme hakkı bittiyse ilk adıma dönülür.
  const submitReset = async () => {
    setError(null);
    if (password.length < 8) {
      setError('Yeni şifre en az 8 karakter olmalı.');
      return;
    }
    setLoading(true);
    try {
      await resetPassword(email.trim(), code.trim(), password);
    } catch (e) {
      const expired = e instanceof ApiError && (e.code === 'auth.code_expired' || e.code === 'auth.code_attempts_exceeded');
      setError(errorMessage(e));
      if (expired) {
        setCode('');
        setStep('email');
      }
    } finally {
      setLoading(false);
    }
  };

  return (
    <SafeAreaView style={styles.safe}>
      <KeyboardAvoidingView style={styles.safe} behavior={Platform.OS === 'ios' ? 'padding' : undefined}>
        <ScrollView contentContainerStyle={styles.content} keyboardShouldPersistTaps="handled">
          <Text style={styles.logo}>🔑</Text>
          <Text style={[font.title, styles.center]}>Şifreni sıfırla</Text>

          {step === 'email' ? (
            <>
              <Text style={[font.body, styles.center]}>Hesabının e-posta adresini yaz, sana bir kod gönderelim.</Text>
              <TextField
                label="E-posta"
                value={email}
                onChangeText={setEmail}
                keyboardType="email-address"
                autoCapitalize="none"
                autoComplete="email"
                textContentType="emailAddress"
                onSubmitEditing={sendCode}
              />
              {error ? <Text style={styles.error}>{error}</Text> : null}
              <Button title="Kod gönder" onPress={sendCode} loading={loading} disabled={!email.includes('@')} />
            </>
          ) : (
            <>
              {info ? <Text style={[font.body, styles.center]}>{info}</Text> : null}
              <TextField
                label="Kod"
                value={code}
                onChangeText={(text) => setCode(text.replace(/[^0-9]/g, '').slice(0, 6))}
                keyboardType="number-pad"
                autoComplete="one-time-code"
                textContentType="oneTimeCode"
                placeholder="123456"
                maxLength={6}
              />
              <TextField
                label="Yeni şifre"
                value={password}
                onChangeText={setPassword}
                secureTextEntry
                autoComplete="new-password"
                textContentType="newPassword"
                hint="En az 8 karakter"
                onSubmitEditing={submitReset}
              />
              {error ? <Text style={styles.error}>{error}</Text> : null}
              <Button title="Şifreyi değiştir ve giriş yap" onPress={submitReset} loading={loading} disabled={code.length !== 6 || !password} />
              <Text style={styles.link} onPress={sendCode}>
                Kod gelmedi mi? <Text style={styles.linkStrong}>Tekrar gönder</Text>
              </Text>
            </>
          )}

          <Link href="/login" style={styles.link}>
            <Text style={styles.linkStrong}>Girişe dön</Text>
          </Link>
        </ScrollView>
      </KeyboardAvoidingView>
    </SafeAreaView>
  );
}

const styles = StyleSheet.create({
  safe: { flex: 1 },
  content: { flexGrow: 1, justifyContent: 'center', padding: spacing.xl, gap: spacing.lg },
  logo: { fontSize: 48, textAlign: 'center' },
  center: { textAlign: 'center' },
  error: { color: colors.danger, textAlign: 'center' },
  link: { textAlign: 'center', color: colors.muted, marginTop: spacing.sm },
  linkStrong: { color: colors.primary, fontWeight: '600' },
});
