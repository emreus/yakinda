import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { Stack } from 'expo-router';
import { ActivityIndicator, Alert, FlatList, StyleSheet, Text, View } from 'react-native';
import { errorMessage } from '@/api/client';
import { safetyApi } from '@/api/endpoints';
import { Button, Card, EmptyState } from '@/components/ui';
import { useInvalidateEvent } from '@/hooks/queries';
import { colors, font, spacing } from '@/theme';
import { formatRelativeTime } from '@/utils/format';

const BLOCKS_KEY = ['blocks'] as const;

/** Engellediğim kullanıcılar; engel kaldırılınca birbirinizin etkinliklerini yeniden görürsünüz. */
export default function BlockedUsersScreen() {
  const queryClient = useQueryClient();
  const invalidateEvents = useInvalidateEvent();
  const blocks = useQuery({ queryKey: BLOCKS_KEY, queryFn: () => safetyApi.blocks() });

  // Engeli kaldırır; başarıda engel listesini ve etkinlik listelerini yeniletir (o kişinin etkinlikleri
  // yeniden görünür).
  const unblock = useMutation({
    mutationFn: (userId: string) => safetyApi.unblock(userId),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: BLOCKS_KEY });
      invalidateEvents();
    },
    onError: (error) => Alert.alert('İşlem yapılamadı', errorMessage(error)),
  });

  return (
    <>
      <Stack.Screen options={{ title: 'Engellenen kullanıcılar' }} />
      <FlatList
        style={styles.screen}
        contentContainerStyle={styles.content}
        data={blocks.data ?? []}
        keyExtractor={(item) => item.userId}
        ListEmptyComponent={
          blocks.isLoading ? (
            <ActivityIndicator color={colors.primary} />
          ) : (
            <EmptyState emoji="🕊️" title="Kimseyi engellemedin" message="Engellediğin kişiler burada görünür." />
          )
        }
        renderItem={({ item }) => (
          <Card style={styles.row}>
            <View style={styles.flex}>
              <Text style={font.subtitle}>{item.displayName}</Text>
              <Text style={font.small}>{formatRelativeTime(item.blockedAt)} engellendi</Text>
            </View>
            <Button
              title="Engeli kaldır"
              variant="secondary"
              onPress={() => unblock.mutate(item.userId)}
              loading={unblock.isPending && unblock.variables === item.userId}
            />
          </Card>
        )}
      />
    </>
  );
}

const styles = StyleSheet.create({
  screen: { backgroundColor: colors.background },
  content: { padding: spacing.lg, gap: spacing.md, flexGrow: 1 },
  row: { flexDirection: 'row', alignItems: 'center', gap: spacing.md },
  flex: { flex: 1 },
});
