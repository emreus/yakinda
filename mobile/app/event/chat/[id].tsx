import { router, Stack, useFocusEffect, useLocalSearchParams } from 'expo-router';
import { useCallback, useEffect, useRef, useState } from 'react';
import {
  ActivityIndicator,
  Alert,
  FlatList,
  KeyboardAvoidingView,
  Platform,
  Pressable,
  StyleSheet,
  Text,
  TextInput,
  View,
} from 'react-native';
import { errorMessage } from '@/api/client';
import { chatApi } from '@/api/endpoints';
import { isUuid } from '@/api/path';
import type { ChatMessage } from '@/api/types';
import { Avatar } from '@/components/Avatar';
import { EmptyState } from '@/components/ui';
import { useEventDetail } from '@/hooks/queries';
import { colors, font, radius, spacing } from '@/theme';
import { formatRelativeTime } from '@/utils/format';

const POLL_MS = 4000;

/** UUIDv7 id'ler zaman sıralı: metin karşılaştırması kronolojik sırayı verir. En yeni önce. */
function merge(existing: ChatMessage[], incoming: ChatMessage[]): ChatMessage[] {
  const byId = new Map(existing.map((m) => [m.id, m]));
  for (const message of incoming) byId.set(message.id, message);
  return [...byId.values()].sort((a, b) => (a.id < b.id ? 1 : a.id > b.id ? -1 : 0));
}

/**
 * Etkinlik sohbeti. Açıkken birkaç saniyede bir son mesajları sorar (yeni mesajlar ve silinenler
 * güncellenir); yukarı kaydırınca eski mesajlar yüklenir. Mesaja uzun basınca: kendi mesajını ya da
 * organizatörsen herhangi bir mesajı sil, başkasının mesajını şikayet et.
 */
export default function EventChatScreen() {
  const { id } = useLocalSearchParams<{ id: string }>();
  // Bu ekran bağlantıyla (bildirim, paylaşım) da açılır ve açılır açılmaz sunucuya istek atar.
  // Kimlik biçiminde olmayan bir değerle hiçbir istek gönderilmez.
  const validId = isUuid(id);
  const event = useEventDetail(id);
  const [messages, setMessages] = useState<ChatMessage[]>([]);
  const [hasMore, setHasMore] = useState(false);
  const [loading, setLoading] = useState(true);
  const [loadingOlder, setLoadingOlder] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [text, setText] = useState('');
  const [sending, setSending] = useState(false);
  const knownIds = useRef(new Set<string>());

  const isOrganizer = event.data?.isOrganizer ?? false;
  const readOnly = event.data ? event.data.status !== 'Active' : false;

  const refresh = useCallback(async () => {
    if (!validId) {
      setError('Sohbet bulunamadı.');
      setLoading(false);
      return;
    }
    try {
      const page = await chatApi.list(id);
      const fresh = page.items.filter((m) => !knownIds.current.has(m.id));
      page.items.forEach((m) => knownIds.current.add(m.id));
      setMessages((current) => merge(current, page.items));
      setHasMore((current) => (knownIds.current.size <= page.items.length ? page.hasMore : current));
      setError(null);
      // Başkasından yeni mesaj geldiyse sohbet bildirimini okundu yap (sonraki mesaj yeniden bildirir).
      if (fresh.some((m) => !m.isMine)) chatApi.markRead(id).catch(() => undefined);
    } catch (e) {
      setError(errorMessage(e));
    } finally {
      setLoading(false);
    }
  }, [id, validId]);

  useEffect(() => {
    if (validId) chatApi.markRead(id).catch(() => undefined);
  }, [id, validId]);

  // Ekran görünürken düzenli yenile
  useFocusEffect(
    useCallback(() => {
      refresh();
      const timer = setInterval(refresh, POLL_MS);
      return () => clearInterval(timer);
    }, [refresh]),
  );

  const loadOlder = async () => {
    const oldest = messages[messages.length - 1];
    if (!hasMore || loadingOlder || !oldest) return;
    setLoadingOlder(true);
    try {
      const page = await chatApi.list(id, { before: oldest.id });
      page.items.forEach((m) => knownIds.current.add(m.id));
      setMessages((current) => merge(current, page.items));
      setHasMore(page.hasMore);
    } catch (e) {
      Alert.alert('Mesajlar yüklenemedi', errorMessage(e));
    } finally {
      setLoadingOlder(false);
    }
  };

  const send = async () => {
    const body = text.trim();
    if (!body || sending) return;
    setSending(true);
    try {
      const message = await chatApi.send(id, body);
      knownIds.current.add(message.id);
      // Gönderme yanıtında fotoğraf adresi yok; kendi balonunda zaten gösterilmiyor.
      setMessages((current) => merge(current, [message]));
      setText('');
    } catch (e) {
      Alert.alert('Gönderilemedi', errorMessage(e));
    } finally {
      setSending(false);
    }
  };

  const openActions = (message: ChatMessage) => {
    if (message.isDeleted) return;
    const canDelete = message.isMine || isOrganizer;
    Alert.alert(message.authorName, message.body ?? '', [
      { text: 'Vazgeç', style: 'cancel' },
      ...(canDelete
        ? [
            {
              text: 'Sil',
              style: 'destructive' as const,
              onPress: async () => {
                try {
                  await chatApi.remove(id, message.id);
                  setMessages((current) => merge(current, [{ ...message, isDeleted: true, body: null }]));
                } catch (e) {
                  Alert.alert('Silinemedi', errorMessage(e));
                }
              },
            },
          ]
        : []),
      ...(!message.isMine
        ? [
            {
              text: 'Şikayet et',
              onPress: () =>
                router.push({
                  pathname: '/report',
                  params: { targetType: 'Message', targetId: message.id, label: message.body?.slice(0, 40) ?? '' },
                }),
            },
          ]
        : []),
    ]);
  };

  const title = <Stack.Screen options={{ title: event.data?.title ?? 'Sohbet' }} />;

  if (loading && messages.length === 0) {
    return (
      <>
        {title}
        <ActivityIndicator style={styles.spinner} color={colors.primary} />
      </>
    );
  }

  if (error && messages.length === 0) {
    return (
      <>
        {title}
        <EmptyState emoji="💬" title="Sohbet açılamadı" message={error} actionLabel="Geri dön" onAction={() => router.back()} />
      </>
    );
  }

  return (
    <>
      {title}
      <KeyboardAvoidingView
        style={styles.flex}
        behavior={Platform.OS === 'ios' ? 'padding' : undefined}
        keyboardVerticalOffset={Platform.OS === 'ios' ? 90 : 0}
      >
        <FlatList
          style={styles.list}
          contentContainerStyle={styles.listContent}
          data={messages}
          inverted
          keyExtractor={(item) => item.id}
          renderItem={({ item }) => <Bubble message={item} onLongPress={() => openActions(item)} />}
          onEndReached={loadOlder}
          onEndReachedThreshold={0.3}
          ListFooterComponent={loadingOlder ? <ActivityIndicator color={colors.primary} /> : undefined}
          ListEmptyComponent={
            <View style={styles.empty}>
              <Text style={styles.emptyText}>Henüz mesaj yok. Buluşma detaylarını konuşmak için ilk mesajı sen yaz.</Text>
            </View>
          }
        />

        {readOnly ? (
          <Text style={styles.readOnly}>Bu etkinlik iptal edildi; sohbet salt okunur.</Text>
        ) : (
          <View style={styles.inputBar}>
            <TextInput
              style={styles.input}
              value={text}
              onChangeText={setText}
              placeholder="Mesaj yaz…"
              placeholderTextColor={colors.muted}
              multiline
              maxLength={1000}
              accessibilityLabel="Mesaj"
            />
            <Pressable
              style={[styles.send, (!text.trim() || sending) && styles.sendDisabled]}
              onPress={send}
              disabled={!text.trim() || sending}
              accessibilityRole="button"
              accessibilityLabel="Gönder"
            >
              {sending ? <ActivityIndicator color={colors.white} /> : <Text style={styles.sendText}>➤</Text>}
            </Pressable>
          </View>
        )}
      </KeyboardAvoidingView>
    </>
  );
}

function Bubble({ message, onLongPress }: { message: ChatMessage; onLongPress: () => void }) {
  const mine = message.isMine;
  return (
    <Pressable onLongPress={onLongPress} style={[styles.bubbleRow, mine && styles.bubbleRowMine]}>
      {!mine ? <Avatar name={message.authorName} url={message.authorAvatarUrl} size={28} /> : null}
      <View style={[styles.bubble, mine ? styles.bubbleMine : styles.bubbleOther]}>
        {!mine ? <Text style={styles.author}>{message.authorName}</Text> : null}
        <Text style={[styles.body, mine && styles.bodyMine, message.isDeleted && styles.deleted]}>
          {message.isDeleted ? 'Mesaj silindi' : message.body}
        </Text>
        <Text style={[styles.time, mine && styles.timeMine]}>{formatRelativeTime(message.createdAt)}</Text>
      </View>
    </Pressable>
  );
}

const styles = StyleSheet.create({
  flex: { flex: 1, backgroundColor: colors.background },
  spinner: { marginTop: spacing.xxl },
  list: { flex: 1 },
  listContent: { padding: spacing.md, gap: spacing.sm, flexGrow: 1 },
  empty: { flex: 1, justifyContent: 'center', padding: spacing.xl, transform: [{ scaleY: -1 }] },
  emptyText: { ...font.small, textAlign: 'center' },
  bubbleRow: { flexDirection: 'row', justifyContent: 'flex-start', alignItems: 'flex-end', gap: spacing.xs },
  bubbleRowMine: { justifyContent: 'flex-end' },
  bubble: { maxWidth: '80%', paddingHorizontal: spacing.md, paddingVertical: spacing.sm, borderRadius: radius.lg, gap: 2 },
  bubbleMine: { backgroundColor: colors.primary, borderBottomRightRadius: 4 },
  bubbleOther: { backgroundColor: colors.card, borderWidth: 1, borderColor: colors.border, borderBottomLeftRadius: 4 },
  author: { fontSize: 12, fontWeight: '700', color: colors.primary },
  body: { fontSize: 15, color: colors.text },
  bodyMine: { color: colors.white },
  deleted: { fontStyle: 'italic', opacity: 0.7 },
  time: { fontSize: 11, color: colors.muted, alignSelf: 'flex-end' },
  timeMine: { color: 'rgba(255,255,255,0.75)' },
  readOnly: { ...font.small, textAlign: 'center', padding: spacing.lg, borderTopWidth: 1, borderTopColor: colors.border },
  inputBar: {
    flexDirection: 'row',
    alignItems: 'flex-end',
    gap: spacing.sm,
    padding: spacing.sm,
    borderTopWidth: 1,
    borderTopColor: colors.border,
    backgroundColor: colors.card,
  },
  input: {
    flex: 1,
    maxHeight: 120,
    paddingHorizontal: spacing.md,
    paddingVertical: spacing.sm,
    borderRadius: radius.lg,
    borderWidth: 1,
    borderColor: colors.border,
    fontSize: 15,
    color: colors.text,
    backgroundColor: colors.background,
  },
  send: { width: 44, height: 44, borderRadius: 22, backgroundColor: colors.primary, alignItems: 'center', justifyContent: 'center' },
  sendDisabled: { opacity: 0.4 },
  sendText: { color: colors.white, fontSize: 18 },
});
