import { useState } from 'react';
import { router } from 'expo-router';
import { ActivityIndicator, Alert, Pressable, RefreshControl, ScrollView, StyleSheet, Text, View } from 'react-native';
import { errorMessage } from '@/api/client';
import type { MyEventsRole } from '@/api/endpoints';
import { useAuth } from '@/auth/AuthContext';
import { EventCard } from '@/components/EventCard';
import { ProfileHeader } from '@/components/ProfileHeader';
import { Button, Card, Chip, EmptyState } from '@/components/ui';
import { contactSupport, openLegalPage, SUPPORT_EMAIL } from '@/legal';
import { useMyEvents, useMyProfile } from '@/hooks/queries';
import { colors, spacing } from '@/theme';

/**
 * Profil sekmesi: kendi profil kartım, "Düzenlediklerim / Katıldıklarım" listesi ve ayarlar (profili
 * düzenle, engellenen kullanıcılar, yasal sayfalar, destek, hesabımı sil, çıkış). Aşağı çekince yenilenir.
 */
export default function ProfileScreen() {
  const { signOut } = useAuth();
  const profile = useMyProfile();
  const [role, setRole] = useState<MyEventsRole>('organizing');
  const myEvents = useMyEvents(role);

  // Profili ve etkinlik listesini sunucudan yeniden ister.
  const refresh = () => {
    profile.refetch();
    myEvents.refetch();
  };

  // Çıkış için onay penceresi açar; onaylanırsa oturumu kapatır.
  const confirmSignOut = () =>
    Alert.alert('Çıkış yap', 'Hesabından çıkmak istediğine emin misin?', [
      { text: 'Vazgeç', style: 'cancel' },
      { text: 'Çıkış yap', style: 'destructive', onPress: () => signOut() },
    ]);

  return (
    <ScrollView
      style={styles.screen}
      contentContainerStyle={styles.content}
      refreshControl={<RefreshControl refreshing={profile.isRefetching} onRefresh={refresh} tintColor={colors.primary} />}
    >
      {profile.data ? (
        <ProfileHeader profile={profile.data} />
      ) : profile.isError ? (
        <EmptyState emoji="⚠️" title="Profil yüklenemedi" message={errorMessage(profile.error)} actionLabel="Tekrar dene" onAction={refresh} />
      ) : (
        <ActivityIndicator color={colors.primary} />
      )}

      <View style={styles.tabs}>
        <Chip label="Düzenlediklerim" selected={role === 'organizing'} onPress={() => setRole('organizing')} />
        <Chip label="Katıldıklarım" selected={role === 'attending'} onPress={() => setRole('attending')} />
      </View>

      {myEvents.isLoading ? <ActivityIndicator color={colors.primary} /> : null}

      {myEvents.isError && !myEvents.data ? (
        <EmptyState
          emoji="⚠️"
          title="Etkinlikler yüklenemedi"
          message={errorMessage(myEvents.error)}
          actionLabel="Tekrar dene"
          onAction={() => myEvents.refetch()}
        />
      ) : null}

      {myEvents.data && myEvents.data.length === 0 ? (
        <EmptyState
          emoji={role === 'organizing' ? '🗓️' : '🎟️'}
          title={role === 'organizing' ? 'Henüz etkinlik düzenlemedin' : 'Henüz bir etkinliğe katılmadın'}
          message={
            role === 'organizing'
              ? 'İlk etkinliğini oluştur; katılımcıların puanları burada ortalamana yansır.'
              : 'Yakınımda sekmesinden sana uygun bir etkinlik bul.'
          }
        />
      ) : null}

      <View style={styles.list}>
        {(myEvents.data ?? []).map((event) => (
          <EventCard key={event.id} event={event} />
        ))}
      </View>

      <Card style={styles.settings}>
        <SettingsRow icon="✏️" label="Profili düzenle" onPress={() => router.push('/settings/profile')} />
        <SettingsRow icon="🚫" label="Engellenen kullanıcılar" onPress={() => router.push('/settings/blocked')} />
        <SettingsRow icon="🔒" label="Gizlilik politikası" onPress={() => openLegalPage('privacy')} />
        <SettingsRow icon="📜" label="Kullanım koşulları" onPress={() => openLegalPage('terms')} />
        <SettingsRow icon="✉️" label={`Destek: ${SUPPORT_EMAIL}`} onPress={() => contactSupport()} />
        <SettingsRow icon="🗑️" label="Hesabımı sil" danger onPress={() => router.push('/settings/delete-account')} last />
      </Card>

      <Button title="Çıkış yap" variant="ghost" onPress={confirmSignOut} style={styles.signOut} />
    </ScrollView>
  );
}

/**
 * Ayarlar listesindeki tek satır: simge, etiket ve sağda ok. danger ile etiket kırmızı yazılır (hesap
 * silme); last ile alt çizgi kaldırılır.
 */
function SettingsRow({
  icon,
  label,
  onPress,
  danger,
  last,
}: {
  icon: string;
  label: string;
  onPress: () => void;
  danger?: boolean;
  last?: boolean;
}) {
  return (
    <Pressable
      onPress={onPress}
      style={({ pressed }) => [styles.settingsRow, !last && styles.settingsDivider, pressed && styles.pressed]}
      accessibilityRole="button"
    >
      <Text style={styles.settingsIcon}>{icon}</Text>
      <Text style={[styles.settingsLabel, danger && styles.danger]} numberOfLines={1}>
        {label}
      </Text>
      <Text style={styles.chevron}>›</Text>
    </Pressable>
  );
}

const styles = StyleSheet.create({
  settings: { paddingVertical: 0, paddingHorizontal: spacing.md },
  settingsRow: { flexDirection: 'row', alignItems: 'center', gap: spacing.md, paddingVertical: spacing.md },
  settingsDivider: { borderBottomWidth: StyleSheet.hairlineWidth, borderBottomColor: colors.border },
  settingsIcon: { fontSize: 18, width: 24, textAlign: 'center' },
  settingsLabel: { flex: 1, fontSize: 15, color: colors.text },
  danger: { color: colors.danger },
  chevron: { fontSize: 20, color: colors.muted },
  pressed: { opacity: 0.6 },
  screen: { backgroundColor: colors.background },
  content: { padding: spacing.lg, gap: spacing.lg, paddingBottom: spacing.xxl },
  tabs: { flexDirection: 'row', gap: spacing.sm },
  list: { gap: spacing.md },
  signOut: { marginTop: spacing.lg },
});
