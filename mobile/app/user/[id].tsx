import { router, Stack, useLocalSearchParams } from 'expo-router';
import { ScrollView, StyleSheet } from 'react-native';
import { errorMessage } from '@/api/client';
import { ProfileHeader } from '@/components/ProfileHeader';
import { EmptyState, FullScreenLoader } from '@/components/ui';
import { useUserProfile } from '@/hooks/queries';
import { colors, spacing } from '@/theme';

export default function UserProfileScreen() {
  const { id } = useLocalSearchParams<{ id: string }>();
  const profile = useUserProfile(id);

  if (profile.isLoading) return <FullScreenLoader />;

  if (profile.isError || !profile.data) {
    return (
      <EmptyState
        emoji="🤷"
        title="Kullanıcı bulunamadı"
        message={profile.error ? errorMessage(profile.error) : undefined}
        actionLabel="Geri dön"
        onAction={() => router.back()}
      />
    );
  }

  return (
    <>
      <Stack.Screen options={{ title: profile.data.displayName }} />
      <ScrollView style={styles.screen} contentContainerStyle={styles.content}>
        <ProfileHeader profile={profile.data} />
      </ScrollView>
    </>
  );
}

const styles = StyleSheet.create({
  screen: { backgroundColor: colors.background },
  content: { padding: spacing.lg },
});
