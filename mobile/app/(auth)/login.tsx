import { Link } from 'expo-router';
import { useState } from 'react';
import { KeyboardAvoidingView, Platform, ScrollView, StyleSheet, Text } from 'react-native';
import { SafeAreaView } from 'react-native-safe-area-context';
import { errorMessage } from '@/api/client';
import { useAuth } from '@/auth/AuthContext';
import { Button, TextField } from '@/components/ui';
import { colors, font, spacing } from '@/theme';

export default function LoginScreen() {
  const { signIn } = useAuth();
  const [email, setEmail] = useState('');
  const [password, setPassword] = useState('');
  const [error, setError] = useState<string | null>(null);
  const [loading, setLoading] = useState(false);

  const submit = async () => {
    setError(null);
    setLoading(true);
    try {
      await signIn(email.trim(), password);
      // Başarılı girişte (auth)/_layout otomatik olarak ana sayfaya yönlendirir.
    } catch (e) {
      setError(errorMessage(e));
    } finally {
      setLoading(false);
    }
  };

  return (
    <SafeAreaView style={styles.safe}>
      <KeyboardAvoidingView behavior={Platform.OS === 'ios' ? 'padding' : undefined} style={styles.safe}>
        <ScrollView contentContainerStyle={styles.content} keyboardShouldPersistTaps="handled">
          <Text style={styles.logo}>📍</Text>
          <Text style={[font.title, styles.center]}>Yakında</Text>
          <Text style={[font.small, styles.center, styles.tagline]}>
            Okeye 4. mü lazım, sinemaya arkadaş mı? Yakınındaki etkinlikleri bul ya da kendin başlat.
          </Text>

          <TextField
            label="E-posta"
            value={email}
            onChangeText={setEmail}
            autoCapitalize="none"
            autoCorrect={false}
            keyboardType="email-address"
            autoComplete="email"
            textContentType="emailAddress"
            placeholder="ornek@mail.com"
          />
          <TextField
            label="Şifre"
            value={password}
            onChangeText={setPassword}
            secureTextEntry
            autoComplete="password"
            textContentType="password"
            onSubmitEditing={submit}
          />

          {error ? <Text style={styles.error}>{error}</Text> : null}

          <Button title="Giriş yap" onPress={submit} loading={loading} disabled={!email || !password} />

          <Link href="/register" style={styles.link}>
            Hesabın yok mu? <Text style={styles.linkStrong}>Kayıt ol</Text>
          </Link>
        </ScrollView>
      </KeyboardAvoidingView>
    </SafeAreaView>
  );
}

const styles = StyleSheet.create({
  safe: { flex: 1 },
  content: { flexGrow: 1, justifyContent: 'center', padding: spacing.xl, gap: spacing.lg },
  logo: { fontSize: 56, textAlign: 'center' },
  center: { textAlign: 'center' },
  tagline: { marginBottom: spacing.md },
  error: { color: colors.danger, textAlign: 'center' },
  link: { textAlign: 'center', color: colors.muted, marginTop: spacing.sm },
  linkStrong: { color: colors.primary, fontWeight: '600' },
});
