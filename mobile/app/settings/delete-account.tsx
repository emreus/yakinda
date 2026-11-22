import { useMutation } from '@tanstack/react-query';
import { Stack } from 'expo-router';
import { useState } from 'react';
import { Alert, KeyboardAvoidingView, Platform, ScrollView, StyleSheet, Text, View } from 'react-native';
import { errorMessage } from '@/api/client';
import { safetyApi } from '@/api/endpoints';
import { useAuth } from '@/auth/AuthContext';
import { Button, Card, TextField } from '@/components/ui';
import { openLegalPage } from '@/legal';
import { colors, font, spacing } from '@/theme';

/** Hesabı kalıcı olarak siler (App Store ve Google Play zorunluluğu). Şifre tekrar istenir. */
export default function DeleteAccountScreen() {
  const { signOut } = useAuth();
  const [password, setPassword] = useState('');

  const mutation = useMutation({
    mutationFn: () => safetyApi.deleteAccount(password),
    onSuccess: async () => {
      await signOut();
      Alert.alert('Hesabın silindi', 'Seni özleyeceğiz. Dilediğin zaman yeniden kayıt olabilirsin.');
    },
    onError: (error) => Alert.alert('Hesap silinemedi', errorMessage(error)),
  });

  const confirm = () =>
    Alert.alert('Hesabını sil', 'Bu işlem geri alınamaz. Emin misin?', [
      { text: 'Vazgeç', style: 'cancel' },
      { text: 'Hesabımı sil', style: 'destructive', onPress: () => mutation.mutate() },
    ]);

  return (
    <>
      <Stack.Screen options={{ title: 'Hesabımı sil' }} />
      <KeyboardAvoidingView style={styles.flex} behavior={Platform.OS === 'ios' ? 'padding' : undefined}>
        <ScrollView style={styles.screen} contentContainerStyle={styles.content} keyboardShouldPersistTaps="handled">
          <Card style={styles.card}>
            <Text style={font.subtitle}>Hesabını silersen:</Text>
            <View style={styles.list}>
              <Text style={font.body}>• E-postan, adın, şifren, bildirimlerin ve yorum metinlerin silinir.</Text>
              <Text style={font.body}>• Yaklaşan etkinliklerdeki yerin boşalır; sıradaki kişi alınır.</Text>
              <Text style={font.body}>• Düzenlediğin yaklaşan etkinlikler iptal edilir ve katılımcılara haber verilir.</Text>
              <Text style={font.body}>
                • Geçmiş etkinlikler ve verdiğin oyların sayıları, başkalarının puan ortalamaları bozulmasın diye
                "Silinmiş kullanıcı" olarak kalır.
              </Text>
            </View>
            <Text style={styles.link} onPress={() => openLegalPage('delete-account')}>
              Ayrıntılar
            </Text>
          </Card>

          <TextField
            label="Onaylamak için şifreni gir"
            value={password}
            onChangeText={setPassword}
            secureTextEntry
            autoComplete="current-password"
            textContentType="password"
          />

          <Button title="Hesabımı kalıcı olarak sil" variant="danger" onPress={confirm} loading={mutation.isPending} disabled={!password} />
        </ScrollView>
      </KeyboardAvoidingView>
    </>
  );
}

const styles = StyleSheet.create({
  flex: { flex: 1 },
  screen: { backgroundColor: colors.background },
  content: { padding: spacing.lg, gap: spacing.lg },
  card: { gap: spacing.md },
  list: { gap: spacing.sm },
  link: { color: colors.primary, fontWeight: '600' },
});
