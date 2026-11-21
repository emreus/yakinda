import { useMutation, useQueryClient, type InfiniteData } from '@tanstack/react-query';
import { router, useFocusEffect } from 'expo-router';
import { useCallback } from 'react';
import { ActivityIndicator, FlatList, Pressable, RefreshControl, StyleSheet, Text, View } from 'react-native';
import { errorMessage } from '@/api/client';
import { notificationsApi } from '@/api/endpoints';
import type { AppNotification, NotificationType, PagedResult } from '@/api/types';
import { EmptyState } from '@/components/ui';
import { queryKeys, useNotifications, useUnreadNotificationCount } from '@/hooks/queries';
import { colors, font, radius, spacing } from '@/theme';
import { formatRelativeTime } from '@/utils/format';

const ICONS: Record<NotificationType, string> = {
  JoinRequested: '🙋',
  ParticipantJoined: '🎉',
  RequestApproved: '✅',
  RequestRejected: '🚫',
  EventCancelled: '❌',
  EventReminder: '⏰',
  RatingPrompt: '⭐',
};

type NotificationPages = InfiniteData<PagedResult<AppNotification>>;

export default function NotificationsScreen() {
  const queryClient = useQueryClient();
  const query = useNotifications();
  const unread = useUnreadNotificationCount();
  const items = query.data?.pages.flatMap((page) => page.items) ?? [];
  const unreadCount = unread.data?.count ?? 0;

  const { refetch: refetchList } = query;
  const { refetch: refetchUnread } = unread;

  // Sekmeye her gelindiğinde tazele (push olmayan Expo Go'da yeni bildirimler böyle görünür)
  useFocusEffect(
    useCallback(() => {
      refetchList();
      refetchUnread();
    }, [refetchList, refetchUnread]),
  );

  /** Önbellekteki listeyi anında günceller; sunucu cevabını beklemeden okundu görünür. */
  const markLocallyRead = useCallback(
    (ids: string[] | 'all') => {
      queryClient.setQueryData<NotificationPages>(queryKeys.notificationList, (old) =>
        old
          ? {
              ...old,
              pages: old.pages.map((page) => ({
                ...page,
                items: page.items.map((n) => (ids === 'all' || ids.includes(n.id) ? { ...n, isRead: true } : n)),
              })),
            }
          : old,
      );
      queryClient.setQueryData<{ count: number }>(queryKeys.unreadNotifications, (old) =>
        old ? { count: ids === 'all' ? 0 : Math.max(0, old.count - ids.length) } : old,
      );
    },
    [queryClient],
  );

  const refreshAll = () => queryClient.invalidateQueries({ queryKey: queryKeys.notifications });

  const markRead = useMutation({
    mutationFn: (id: string) => notificationsApi.markRead(id),
    onMutate: (id) => markLocallyRead([id]),
    onSettled: refreshAll,
  });

  const markAllRead = useMutation({
    mutationFn: () => notificationsApi.markAllRead(),
    onMutate: () => markLocallyRead('all'),
    onSettled: refreshAll,
  });

  const open = (notification: AppNotification) => {
    if (!notification.isRead) markRead.mutate(notification.id);
    if (notification.eventId) router.push(`/event/${notification.eventId}`);
  };

  const header =
    unreadCount > 0 ? (
      <View style={styles.header}>
        <Text style={font.small}>{unreadCount} okunmamış bildirim</Text>
        <Pressable onPress={() => markAllRead.mutate()} hitSlop={8} disabled={markAllRead.isPending}>
          <Text style={styles.headerAction}>Tümünü okundu yap</Text>
        </Pressable>
      </View>
    ) : undefined;

  const empty = query.isLoading ? (
    <ActivityIndicator style={styles.spinner} color={colors.primary} />
  ) : query.isError ? (
    <EmptyState
      emoji="⚠️"
      title="Bildirimler yüklenemedi"
      message={errorMessage(query.error)}
      actionLabel="Tekrar dene"
      onAction={() => query.refetch()}
    />
  ) : (
    <EmptyState
      emoji="🔔"
      title="Henüz bildirimin yok"
      message="Katılım istekleri, onaylar, iptaller ve etkinlik hatırlatmaları burada görünür."
    />
  );

  return (
    <FlatList
      style={styles.screen}
      data={items}
      keyExtractor={(item) => item.id}
      renderItem={({ item }) => <NotificationRow notification={item} onPress={() => open(item)} />}
      ItemSeparatorComponent={Separator}
      ListHeaderComponent={header}
      ListEmptyComponent={empty}
      ListFooterComponent={query.isFetchingNextPage ? <ActivityIndicator style={styles.footer} color={colors.primary} /> : undefined}
      contentContainerStyle={styles.list}
      onEndReachedThreshold={0.4}
      onEndReached={() => {
        if (query.hasNextPage && !query.isFetchingNextPage) query.fetchNextPage();
      }}
      refreshControl={
        <RefreshControl
          refreshing={query.isRefetching && !query.isFetchingNextPage}
          onRefresh={() => {
            refreshAll();
          }}
          tintColor={colors.primary}
        />
      }
    />
  );
}

function NotificationRow({ notification, onPress }: { notification: AppNotification; onPress: () => void }) {
  const unread = !notification.isRead;
  return (
    <Pressable
      onPress={onPress}
      style={({ pressed }) => [styles.row, unread && styles.rowUnread, pressed && styles.rowPressed]}
      accessibilityRole="button"
      accessibilityLabel={`${notification.title}. ${notification.body}${unread ? '. Okunmadı' : ''}`}
    >
      <Text style={styles.icon}>{ICONS[notification.type] ?? '🔔'}</Text>
      <View style={styles.content}>
        <Text style={[styles.title, unread && styles.titleUnread]} numberOfLines={1}>
          {notification.title}
        </Text>
        <Text style={styles.body} numberOfLines={3}>
          {notification.body}
        </Text>
        <Text style={font.small}>{formatRelativeTime(notification.createdAt)}</Text>
      </View>
      {unread ? <View style={styles.dot} /> : null}
    </Pressable>
  );
}

function Separator() {
  return <View style={styles.separator} />;
}

const styles = StyleSheet.create({
  screen: { flex: 1, backgroundColor: colors.background },
  list: { padding: spacing.lg, flexGrow: 1 },
  header: {
    flexDirection: 'row',
    justifyContent: 'space-between',
    alignItems: 'center',
    marginBottom: spacing.md,
  },
  headerAction: { fontSize: 14, fontWeight: '600', color: colors.primary },
  spinner: { marginTop: spacing.xxl },
  footer: { marginVertical: spacing.lg },
  separator: { height: spacing.sm },
  row: {
    flexDirection: 'row',
    alignItems: 'flex-start',
    gap: spacing.md,
    padding: spacing.md,
    borderRadius: radius.md,
    backgroundColor: colors.card,
    borderWidth: 1,
    borderColor: colors.border,
  },
  rowUnread: { backgroundColor: colors.primaryLight, borderColor: colors.primaryLight },
  rowPressed: { opacity: 0.7 },
  icon: { fontSize: 22, lineHeight: 28 },
  content: { flex: 1, gap: 2 },
  title: { fontSize: 15, fontWeight: '500', color: colors.text },
  titleUnread: { fontWeight: '700' },
  body: { fontSize: 14, color: colors.text },
  dot: { width: 10, height: 10, borderRadius: 5, backgroundColor: colors.primary, marginTop: 9 },
});
