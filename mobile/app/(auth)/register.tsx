import { Link } from 'expo-router';
import { useState } from 'react';
import { KeyboardAvoidingView, Platform, ScrollView, StyleSheet, Text } from 'react-native';
import { SafeAreaView } from 'react-native-safe-area-context';
import { ApiError, errorMessage } from '@/api/client';
import { useAuth } from '@/auth/AuthContext';
import { Button, TextField } from '@/components/ui';
import { colors, font, spacing } from '@/theme';

type FieldErrors = Partial<Record<'email' | 'password' | 'displayName', string>>;

export default function RegisterScreen() {
  const { signUp } = useAuth();
  const [displayName, setDisplayName] = useState('');
  const [email, setEmail] = useState('');
  const [password, setPassword] = useState('');
  const [fieldErrors, setFieldErrors] = useState<FieldErrors>({});
  const [error, setError] = useState<string | null>(null);
  const [loading, setLoading] = useState(false);

  const submit = async () => {
    setError(null);
    setFieldErrors({});

    if (password.length < 8) {
      setFieldErrors({ password: 'Şifre en az 8 karakter olmalı.' });
      return;
    }

    setLoading(true);
    try {
      await signUp(email.trim(), password, displayName.trim());
    } catch (e) {
      if (e instanceof ApiError && e.fieldErrors) {
        setFieldErrors({
          email: e.fieldErrors.email?.[0],
          password: e.fieldErrors.password?.[0],
          displayName: e.fieldErrors.displayName?.[0],
        });
      } else {
        setError(errorMessage(e));
      }
    } finally {
      setLoading(false);
    }
  };

  return (
    <SafeAreaView style={styles.safe}>
      <KeyboardAvoidingView behavior={Platform.OS === 'ios' ? 'padding' : undefined} style={styles.safe}>
        <ScrollView contentContainerStyle={styles.content} keyboardShouldPersistTaps="handled">
          <Text style={[font.title, styles.center]}>Hesap oluştur</Text>
          <Text style={[font.small, styles.center]}>
            Düzenlediğin etkinlikler katılımcılar tarafından 5 üzerinden puanlanır; puan ortalaman profilinde görünür.
          </Text>

          <TextField
            label="Görünen ad"
            value={displayName}
            onChangeText={setDisplayName}
            placeholder="Örn. Ayşe K."
            error={fieldErrors.displayName}
            autoComplete="name"
          />
          <TextField
            label="E-posta"
            value={email}
            onChangeText={setEmail}
            autoCapitalize="none"
            autoCorrect={false}
            keyboardType="email-address"
            autoComplete="email"
            error={fieldErrors.email}
          />
          <TextField
            label="Şifre"
            value={password}
            onChangeText={setPassword}
            secureTextEntry
            autoComplete="new-password"
            textContentType="newPassword"
            hint="En az 8 karakter"
            error={fieldErrors.password}
          />

          {error ? <Text style={styles.error}>{error}</Text> : null}

          <Button
            title="Kayıt ol"
            onPress={submit}
            loading={loading}
            disabled={!email || !password || displayName.trim().length < 2}
          />

          <Link href="/login" style={styles.link}>
            Zaten hesabın var mı? <Text style={styles.linkStrong}>Giriş yap</Text>
          </Link>
        </ScrollView>
      </KeyboardAvoidingView>
    </SafeAreaView>
  );
}

const styles = StyleSheet.create({
  safe: { flex: 1 },
  content: { flexGrow: 1, justifyContent: 'center', padding: spacing.xl, gap: spacing.lg },
  center: { textAlign: 'center' },
  error: { color: colors.danger, textAlign: 'center' },
  link: { textAlign: 'center', color: colors.muted, marginTop: spacing.sm },
  linkStrong: { color: colors.primary, fontWeight: '600' },
});
