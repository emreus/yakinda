import { useMutation } from '@tanstack/react-query';
import { router, Stack, useLocalSearchParams } from 'expo-router';
import { useState } from 'react';
import { Alert, Pressable, ScrollView, StyleSheet, Text, View } from 'react-native';
import { errorMessage } from '@/api/client';
import { safetyApi } from '@/api/endpoints';
import type { ReportReason, ReportTargetType } from '@/api/types';
import { Button, TextField } from '@/components/ui';
import { contactSupport } from '@/legal';
import { colors, font, radius, spacing } from '@/theme';

const REASONS: { value: ReportReason; label: string; hint: string }[] = [
  { value: 'Inappropriate', label: 'Uygunsuz içerik', hint: 'Müstehcen, şiddet içeren ya da rahatsız edici' },
  { value: 'Harassment', label: 'Taciz veya hakaret', hint: 'Zorbalık, tehdit, nefret söylemi' },
  { value: 'Dangerous', label: 'Tehlikeli ya da yasa dışı', hint: 'Güvenliği tehdit eden etkinlik veya davranış' },
  { value: 'Fake', label: 'Sahte veya dolandırıcılık', hint: 'Gerçek olmayan etkinlik, para isteme' },
  { value: 'Spam', label: 'Spam veya reklam', hint: 'Tekrarlanan, alakasız içerik' },
  { value: 'Other', label: 'Diğer', hint: 'Aşağıda kısaca anlat' },
];

const TARGET_LABEL: Record<ReportTargetType, string> = {
  Event: 'etkinliği',
  User: 'kullanıcıyı',
  Message: 'mesajı',
};

/** Etkinlik, kullanıcı ya da mesaj şikayeti. Şikayetler en geç 24 saat içinde incelenir. */
export default function ReportScreen() {
  const { targetType, targetId, label } = useLocalSearchParams<{
    targetType: ReportTargetType;
    targetId: string;
    label?: string;
  }>();
  const [reason, setReason] = useState<ReportReason | null>(null);
  const [details, setDetails] = useState('');

  const mutation = useMutation({
    mutationFn: () => safetyApi.report(targetType, targetId, reason!, details.trim() || null),
    onSuccess: () => {
      Alert.alert('Teşekkürler', 'Şikayetini aldık; en geç 24 saat içinde inceleyeceğiz.', [
        { text: 'Tamam', onPress: () => router.back() },
      ]);
    },
    onError: (error) => Alert.alert('Gönderilemedi', errorMessage(error)),
  });

  return (
    <>
      <Stack.Screen options={{ title: 'Şikayet et' }} />
      <ScrollView style={styles.screen} contentContainerStyle={styles.content} keyboardShouldPersistTaps="handled">
        <Text style={font.body}>
          {label ? `"${label}" ` : 'Bu '}
          {TARGET_LABEL[targetType] ?? 'içeriği'} neden şikayet ediyorsun?
        </Text>

        <View style={styles.reasons}>
          {REASONS.map((item) => {
            const selected = reason === item.value;
            return (
              <Pressable
                key={item.value}
                style={[styles.reason, selected && styles.reasonSelected]}
                onPress={() => setReason(item.value)}
                accessibilityRole="radio"
                accessibilityState={{ selected }}
              >
                <Text style={[styles.reasonTitle, selected && styles.reasonTitleSelected]}>{item.label}</Text>
                <Text style={font.small}>{item.hint}</Text>
              </Pressable>
            );
          })}
        </View>

        <TextField
          label="Ayrıntı (isteğe bağlı)"
          value={details}
          onChangeText={setDetails}
          placeholder="Ne oldu? İncelememize yardımcı olur."
          multiline
          maxLength={500}
        />

        <Button title="Şikayeti gönder" onPress={() => mutation.mutate()} loading={mutation.isPending} disabled={!reason} />

        <Text style={[font.small, styles.center]}>
          Acil bir tehlike varsa 112'yi ara. Başka bir konu için{' '}
          <Text style={styles.link} onPress={() => contactSupport('Şikayet')}>
            bize yaz
          </Text>
          .
        </Text>
      </ScrollView>
    </>
  );
}

const styles = StyleSheet.create({
  screen: { backgroundColor: colors.background },
  content: { padding: spacing.lg, gap: spacing.lg, paddingBottom: spacing.xxl },
  reasons: { gap: spacing.sm },
  reason: {
    padding: spacing.md,
    borderRadius: radius.md,
    borderWidth: 1,
    borderColor: colors.border,
    backgroundColor: colors.card,
    gap: 2,
  },
  reasonSelected: { borderColor: colors.primary, backgroundColor: colors.primaryLight },
  reasonTitle: { fontSize: 15, fontWeight: '600', color: colors.text },
  reasonTitleSelected: { color: colors.primary },
  center: { textAlign: 'center' },
  link: { color: colors.primary, fontWeight: '600' },
});
