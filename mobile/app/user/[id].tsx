import { useMutation } from '@tanstack/react-query';
import { router, Stack, useLocalSearchParams } from 'expo-router';
import { Alert, Pressable, ScrollView, StyleSheet, Text, View } from 'react-native';
import { errorMessage } from '@/api/client';
import { safetyApi } from '@/api/endpoints';
import { useAuth } from '@/auth/AuthContext';
import { ProfileHeader } from '@/components/ProfileHeader';
import { EmptyState, FullScreenLoader } from '@/components/ui';
import { useInvalidateEvent, useUserProfile } from '@/hooks/queries';
import { goBackOrHome } from '@/navigation';
import { colors, spacing } from '@/theme';

/**
 * Başka bir kullanıcının profil ekranı: profil kartı ile şikayet et ve engelle bağlantıları. Kendi
 * profilime bakıyorsam bu bağlantılar gösterilmez.
 */
export default function UserProfileScreen() {
  const { id } = useLocalSearchParams<{ id: string }>();
  const { user } = useAuth();
  const profile = useUserProfile(id);
  const invalidate = useInvalidateEvent();

  // Kullanıcıyı engeller; başarıda listeleri yeniletir, bilgi verir ve önceki ekrana döner.
  const block = useMutation({
    mutationFn: () => safetyApi.block(id),
    onSuccess: () => {
      invalidate();
      Alert.alert('Engellendi', 'Bu kişiyle artık birbirinizin etkinliklerini görmeyeceksiniz.');
      goBackOrHome();
    },
    onError: (error) => Alert.alert('İşlem yapılamadı', errorMessage(error)),
  });

  if (profile.isLoading) return <FullScreenLoader />;

  if (profile.isError || !profile.data) {
    return (
      <EmptyState
        emoji="🤷"
        title="Kullanıcı bulunamadı"
        message={profile.error ? errorMessage(profile.error) : undefined}
        actionLabel="Geri dön"
        onAction={goBackOrHome}
      />
    );
  }

  const name = profile.data.displayName;
  const isMe = user?.id === id;

  return (
    <>
      <Stack.Screen options={{ title: name }} />
      <ScrollView style={styles.screen} contentContainerStyle={styles.content}>
        <ProfileHeader profile={profile.data} />

        {!isMe ? (
          <View style={styles.safetyRow}>
            <Pressable
              hitSlop={8}
              onPress={() => router.push({ pathname: '/report', params: { targetType: 'User', targetId: id, label: name } })}
            >
              <Text style={styles.safetyLink}>🚩 Şikayet et</Text>
            </Pressable>
            <Pressable
              hitSlop={8}
              onPress={() =>
                Alert.alert(
                  'Engelle',
                  `${name} adlı kullanıcıyı engellemek istiyor musun? Birbirinizin yaklaşan etkinliklerindeki katılımlarınız da sona erer.`,
                  [
                    { text: 'Vazgeç', style: 'cancel' },
                    { text: 'Engelle', style: 'destructive', onPress: () => block.mutate() },
                  ],
                )
              }
            >
              <Text style={styles.safetyLink}>🚫 Engelle</Text>
            </Pressable>
          </View>
        ) : null}
      </ScrollView>
    </>
  );
}

const styles = StyleSheet.create({
  screen: { backgroundColor: colors.background },
  content: { padding: spacing.lg, gap: spacing.lg },
  safetyRow: { flexDirection: 'row', justifyContent: 'center', gap: spacing.xl },
  safetyLink: { fontSize: 14, color: colors.muted },
});
