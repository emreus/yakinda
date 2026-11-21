import { useState } from 'react';
import { ActivityIndicator, Alert, RefreshControl, ScrollView, StyleSheet, View } from 'react-native';
import { errorMessage } from '@/api/client';
import type { MyEventsRole } from '@/api/endpoints';
import { useAuth } from '@/auth/AuthContext';
import { EventCard } from '@/components/EventCard';
import { ProfileHeader } from '@/components/ProfileHeader';
import { Button, Chip, EmptyState } from '@/components/ui';
import { useMyEvents, useMyProfile } from '@/hooks/queries';
import { colors, spacing } from '@/theme';

export default function ProfileScreen() {
  const { signOut } = useAuth();
  const profile = useMyProfile();
  const [role, setRole] = useState<MyEventsRole>('organizing');
  const myEvents = useMyEvents(role);

  const refresh = () => {
    profile.refetch();
    myEvents.refetch();
  };

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

      <Button title="Çıkış yap" variant="ghost" onPress={confirmSignOut} style={styles.signOut} />
    </ScrollView>
  );
}

const styles = StyleSheet.create({
  screen: { backgroundColor: colors.background },
  content: { padding: spacing.lg, gap: spacing.lg, paddingBottom: spacing.xxl },
  tabs: { flexDirection: 'row', gap: spacing.sm },
  list: { gap: spacing.md },
  signOut: { marginTop: spacing.lg },
});
