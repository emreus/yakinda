import { useMutation, useQueryClient } from '@tanstack/react-query';
import { router, Stack, useLocalSearchParams } from 'expo-router';
import { useEffect, useState, type ReactNode } from 'react';
import { ActivityIndicator, Alert, Linking, Platform, Pressable, RefreshControl, ScrollView, StyleSheet, Text, View } from 'react-native';
import MapView, { Marker } from 'react-native-maps';
import { errorMessage } from '@/api/client';
import { eventsApi } from '@/api/endpoints';
import type { EventDetail, Participant } from '@/api/types';
import { RatingBadge, StarRating } from '@/components/StarRating';
import { Badge, Button, Card, EmptyState, FullScreenLoader, SectionTitle, TextField } from '@/components/ui';
import { queryKeys, useEventDetail, useInvalidateEvent, useParticipants, useRatings } from '@/hooks/queries';
import { colors, font, radius, spacing } from '@/theme';
import { formatDateTimeLong, formatEventDate, formatSpots, formatTime } from '@/utils/format';

export default function EventDetailScreen() {
  const { id } = useLocalSearchParams<{ id: string }>();
  const eventQuery = useEventDetail(id);
  const event = eventQuery.data;

  if (eventQuery.isLoading) return <FullScreenLoader />;

  if (eventQuery.isError || !event) {
    return (
      <EmptyState
        emoji="🤷"
        title="Etkinlik bulunamadı"
        message={eventQuery.error ? errorMessage(eventQuery.error) : undefined}
        actionLabel="Geri dön"
        onAction={() => router.back()}
      />
    );
  }

  return (
    <>
      <Stack.Screen options={{ title: event.categoryName }} />
      <ScrollView
        style={styles.screen}
        contentContainerStyle={styles.content}
        refreshControl={<RefreshControl refreshing={eventQuery.isRefetching} onRefresh={() => eventQuery.refetch()} />}
      >
        <Header event={event} />
        <ActionPanel event={event} />
        {event.isOrganizer ? <ParticipantsPanel event={event} /> : <AttendeesPreview event={event} />}
        <RatingPanel event={event} />
        <RatingsList event={event} />
      </ScrollView>
    </>
  );
}

// ------------------------------------------------------------------ Başlık + bilgiler

function Header({ event }: { event: EventDetail }) {
  const spots = formatSpots(event);

  const openDirections = () => {
    const { latitude, longitude } = event;
    const label = encodeURIComponent(event.placeName ?? event.title);
    const url =
      Platform.OS === 'ios'
        ? `http://maps.apple.com/?daddr=${latitude},${longitude}&q=${label}`
        : `geo:${latitude},${longitude}?q=${latitude},${longitude}(${label})`;
    Linking.openURL(url).catch(() =>
      Linking.openURL(`https://www.google.com/maps/dir/?api=1&destination=${latitude},${longitude}`),
    );
  };

  return (
    <View style={styles.section}>
      <View style={styles.titleRow}>
        <Text style={styles.emoji}>{event.categoryEmoji}</Text>
        <View style={styles.flex}>
          <Text style={font.title}>{event.title}</Text>
          <View style={styles.badgeRow}>
            {event.status === 'Cancelled' ? <Badge label="İptal edildi" tone="danger" /> : null}
            <Badge label={spots.label} tone={spots.tone === 'open' ? 'success' : spots.tone === 'full' ? 'danger' : 'neutral'} />
            {event.joinPolicy === 'ApprovalRequired' ? <Badge label="Organizatör onaylı" tone="primary" /> : null}
          </View>
        </View>
      </View>

      <Card style={styles.infoCard}>
        <InfoRow icon="📅" text={formatDateTimeLong(event.startsAt) + (event.endsAt ? ` – ${formatTime(event.endsAt)}` : '')} />
        <InfoRow icon="📍" text={[event.placeName, event.address].filter(Boolean).join(' · ') || 'Konum haritada'} />
        <InfoRow
          icon="👥"
          text={
            event.capacity == null
              ? `${event.participantCount} katılımcı (sınırsız)`
              : `${event.participantCount} / ${event.capacity} katılımcı`
          }
        />
      </Card>

      <View style={styles.mapBox}>
        <MapView
          style={StyleSheet.absoluteFill}
          initialRegion={{ latitude: event.latitude, longitude: event.longitude, latitudeDelta: 0.008, longitudeDelta: 0.008 }}
          scrollEnabled={false}
          zoomEnabled={false}
          rotateEnabled={false}
          pitchEnabled={false}
        >
          <Marker coordinate={{ latitude: event.latitude, longitude: event.longitude }} pinColor={colors.primary} />
        </MapView>
        <Pressable style={styles.directions} onPress={openDirections}>
          <Text style={styles.directionsText}>Yol tarifi</Text>
        </Pressable>
      </View>

      <Pressable onPress={() => router.push(`/user/${event.organizerId}`)}>
        <Card style={styles.organizerCard}>
          <View style={styles.avatar}>
            <Text style={styles.avatarText}>{event.organizerName.charAt(0).toLocaleUpperCase('tr-TR')}</Text>
          </View>
          <View style={styles.flex}>
            <Text style={font.small}>Organizatör</Text>
            <Text style={styles.organizerName}>{event.organizerName}</Text>
          </View>
          <RatingBadge value={event.organizerRating} count={event.organizerRatingCount} emptyLabel="Henüz puan yok" />
        </Card>
      </Pressable>

      {event.description ? <Text style={styles.description}>{event.description}</Text> : null}
    </View>
  );
}

function InfoRow({ icon, text }: { icon: string; text: string }) {
  return (
    <View style={styles.infoRow}>
      <Text style={styles.infoIcon}>{icon}</Text>
      <Text style={[font.body, styles.flex]}>{text}</Text>
    </View>
  );
}

// ------------------------------------------------------------------ Katıl / Ayrıl / İptal

function ActionPanel({ event }: { event: EventDetail }) {
  const invalidate = useInvalidateEvent();
  const onError = (error: unknown) => Alert.alert('İşlem yapılamadı', errorMessage(error));

  const join = useMutation({
    mutationFn: () => eventsApi.join(event.id),
    onSuccess: (result) => {
      invalidate(event.id);
      if (result.status === 'Pending') Alert.alert('İstek gönderildi', 'Organizatör onayladığında katılımcı olacaksın.');
    },
    onError,
  });

  const leave = useMutation({ mutationFn: () => eventsApi.leave(event.id), onSuccess: () => invalidate(event.id), onError });
  const cancel = useMutation({ mutationFn: () => eventsApi.cancel(event.id), onSuccess: () => invalidate(event.id), onError });

  const confirm = (title: string, message: string, onConfirm: () => void) =>
    Alert.alert(title, message, [
      { text: 'Vazgeç', style: 'cancel' },
      { text: 'Evet', style: 'destructive', onPress: onConfirm },
    ]);

  const statusText =
    event.myParticipationStatus === 'Approved'
      ? '✅ Katılıyorsun'
      : event.myParticipationStatus === 'Pending'
        ? '⏳ Katılım isteğin onay bekliyor'
        : event.myParticipationStatus === 'Rejected'
          ? 'Katılım isteğin kabul edilmedi'
          : null;

  return (
    <View style={styles.actions}>
      {statusText ? <Text style={styles.statusText}>{statusText}</Text> : null}

      {event.canJoin ? (
        <Button
          title={event.joinPolicy === 'ApprovalRequired' ? 'Katılım isteği gönder' : 'Katıl'}
          onPress={() => join.mutate()}
          loading={join.isPending}
        />
      ) : null}

      {event.canLeave ? (
        <Button
          title={event.myParticipationStatus === 'Pending' ? 'İsteği geri çek' : 'Ayrıl'}
          variant="ghost"
          onPress={() => confirm('Etkinlikten ayrıl', 'Yerini başkası alabilir. Emin misin?', () => leave.mutate())}
          loading={leave.isPending}
        />
      ) : null}

      {event.canCancel ? (
        <Button
          title="Etkinliği iptal et"
          variant="danger"
          onPress={() => confirm('Etkinliği iptal et', 'Bu işlem geri alınamaz.', () => cancel.mutate())}
          loading={cancel.isPending}
        />
      ) : null}
    </View>
  );
}

// ------------------------------------------------------------------ Katılımcılar

function ParticipantsPanel({ event }: { event: EventDetail }) {
  const participants = useParticipants(event.id);
  const invalidate = useInvalidateEvent();
  const queryClient = useQueryClient();

  const review = useMutation({
    mutationFn: ({ userId, approve }: { userId: string; approve: boolean }) =>
      approve ? eventsApi.approve(event.id, userId) : eventsApi.reject(event.id, userId),
    onSuccess: () => {
      invalidate(event.id);
      queryClient.invalidateQueries({ queryKey: queryKeys.participants(event.id) });
    },
    onError: (error) => Alert.alert('İşlem yapılamadı', errorMessage(error)),
  });

  const list = participants.data ?? [];
  const pending = list.filter((p) => p.status === 'Pending');
  const approved = list.filter((p) => p.status === 'Approved');
  const notStarted = new Date(event.startsAt).getTime() > Date.now() && event.status === 'Active';

  return (
    <Card style={styles.panel}>
      <SectionTitle right={event.pendingCount > 0 ? <Badge label={`${event.pendingCount} bekleyen`} tone="warning" /> : undefined}>
        Katılımcılar
      </SectionTitle>

      {participants.isLoading ? <ActivityIndicator color={colors.primary} /> : null}
      {!participants.isLoading && list.length === 0 ? <Text style={font.small}>Henüz katılan yok.</Text> : null}

      {pending.map((p) => (
        <ParticipantRow key={p.userId} participant={p}>
          {notStarted ? (
            <View style={styles.reviewButtons}>
              <Button title="Onayla" onPress={() => review.mutate({ userId: p.userId, approve: true })} style={styles.smallButton} />
              <Button
                title="Reddet"
                variant="ghost"
                onPress={() => review.mutate({ userId: p.userId, approve: false })}
                style={styles.smallButton}
              />
            </View>
          ) : null}
        </ParticipantRow>
      ))}

      {approved.map((p) => (
        <ParticipantRow key={p.userId} participant={p} />
      ))}
    </Card>
  );
}

function AttendeesPreview({ event }: { event: EventDetail }) {
  const participants = useParticipants(event.id, event.participantCount > 0);
  if (event.participantCount === 0) return null;
  return (
    <Card style={styles.panel}>
      <SectionTitle>Katılanlar</SectionTitle>
      {(participants.data ?? []).map((p) => (
        <ParticipantRow key={p.userId} participant={p} />
      ))}
    </Card>
  );
}

function ParticipantRow({ participant, children }: { participant: Participant; children?: ReactNode }) {
  return (
    <View style={styles.participantRow}>
      <Pressable style={styles.participantInfo} onPress={() => router.push(`/user/${participant.userId}`)}>
        <View style={styles.avatarSmall}>
          <Text style={styles.avatarSmallText}>{participant.displayName.charAt(0).toLocaleUpperCase('tr-TR')}</Text>
        </View>
        <View style={styles.flex}>
          <Text style={font.body}>{participant.displayName}</Text>
          <RatingBadge value={participant.averageRating} count={participant.ratingCount} emptyLabel="Organizatör puanı yok" />
        </View>
      </Pressable>
      {children}
    </View>
  );
}

// ------------------------------------------------------------------ Oylama

function RatingPanel({ event }: { event: EventDetail }) {
  const invalidate = useInvalidateEvent();
  const queryClient = useQueryClient();
  const [score, setScore] = useState(event.myScore ?? 0);
  const [comment, setComment] = useState(event.myComment ?? '');

  useEffect(() => {
    setScore(event.myScore ?? 0);
    setComment(event.myComment ?? '');
  }, [event.myScore, event.myComment]);

  const rate = useMutation({
    mutationFn: () => eventsApi.rate(event.id, score, comment.trim() || null),
    onSuccess: () => {
      invalidate(event.id);
      queryClient.invalidateQueries({ queryKey: queryKeys.ratings(event.id) });
      Alert.alert('Teşekkürler!', 'Puanın organizatörün ortalamasına yansıdı.');
    },
    onError: (error) => Alert.alert('Puan verilemedi', errorMessage(error)),
  });

  const opensAt = new Date(event.ratingOpensAt).getTime();
  const isParticipant = event.myParticipationStatus === 'Approved';

  // Organizatör kendi etkinliğini oylayamaz, katılımcı olmayanlara panel gösterilmez.
  if (event.isOrganizer || !isParticipant || event.status === 'Cancelled') return null;

  return (
    <Card style={styles.panel}>
      <SectionTitle>{event.myScore ? 'Puanın' : 'Etkinliği puanla'}</SectionTitle>

      {event.canRate ? (
        <>
          <StarRating value={score} onChange={setScore} size={36} />
          <TextField
            label="Yorum (isteğe bağlı)"
            value={comment}
            onChangeText={setComment}
            placeholder="Nasıldı?"
            multiline
            maxLength={500}
          />
          <Button
            title={event.myScore ? 'Puanı güncelle' : 'Puanı gönder'}
            onPress={() => rate.mutate()}
            disabled={score < 1}
            loading={rate.isPending}
          />
        </>
      ) : Date.now() < opensAt ? (
        <Text style={font.small}>Oylama etkinlik başladığında ({formatEventDate(event.ratingOpensAt)}) açılır.</Text>
      ) : (
        <>
          {event.myScore ? <StarRating value={event.myScore} size={24} /> : null}
          <Text style={font.small}>Bu etkinliğin oylama süresi doldu.</Text>
        </>
      )}
    </Card>
  );
}

function RatingsList({ event }: { event: EventDetail }) {
  const ratings = useRatings(event.id);
  if (event.ratingCount === 0) return null;

  return (
    <Card style={styles.panel}>
      <SectionTitle right={<RatingBadge value={event.averageRating} count={event.ratingCount} />}>Değerlendirmeler</SectionTitle>
      {(ratings.data?.items ?? []).map((rating) => (
        <View key={rating.raterId} style={styles.ratingRow}>
          <View style={styles.ratingHeader}>
            <Text style={styles.raterName}>{rating.raterName}</Text>
            <StarRating value={rating.score} size={14} />
          </View>
          {rating.comment ? <Text style={font.body}>{rating.comment}</Text> : null}
        </View>
      ))}
    </Card>
  );
}

const styles = StyleSheet.create({
  flex: { flex: 1 },
  screen: { backgroundColor: colors.background },
  content: { padding: spacing.lg, gap: spacing.lg, paddingBottom: spacing.xxl },
  section: { gap: spacing.md },
  titleRow: { flexDirection: 'row', gap: spacing.md, alignItems: 'flex-start' },
  emoji: { fontSize: 40 },
  badgeRow: { flexDirection: 'row', flexWrap: 'wrap', gap: spacing.xs, marginTop: spacing.sm },
  infoCard: { gap: spacing.sm },
  infoRow: { flexDirection: 'row', gap: spacing.sm, alignItems: 'flex-start' },
  infoIcon: { fontSize: 16, width: 24 },
  mapBox: { height: 160, borderRadius: radius.lg, overflow: 'hidden' },
  directions: {
    position: 'absolute',
    right: spacing.sm,
    bottom: spacing.sm,
    backgroundColor: colors.card,
    borderRadius: radius.pill,
    paddingHorizontal: spacing.md,
    paddingVertical: spacing.sm,
  },
  directionsText: { color: colors.primary, fontWeight: '600' },
  organizerCard: { flexDirection: 'row', alignItems: 'center', gap: spacing.md },
  avatar: {
    width: 44,
    height: 44,
    borderRadius: 22,
    backgroundColor: colors.primaryLight,
    alignItems: 'center',
    justifyContent: 'center',
  },
  avatarText: { fontSize: 18, fontWeight: '700', color: colors.primary },
  organizerName: { fontSize: 16, fontWeight: '600', color: colors.text },
  description: { fontSize: 15, lineHeight: 22, color: colors.text },
  actions: { gap: spacing.sm },
  statusText: { fontSize: 15, fontWeight: '600', color: colors.text, textAlign: 'center' },
  panel: { gap: spacing.md },
  participantRow: { flexDirection: 'row', alignItems: 'center', justifyContent: 'space-between', gap: spacing.sm },
  participantInfo: { flexDirection: 'row', alignItems: 'center', gap: spacing.sm, flex: 1 },
  avatarSmall: {
    width: 34,
    height: 34,
    borderRadius: 17,
    backgroundColor: colors.background,
    alignItems: 'center',
    justifyContent: 'center',
  },
  avatarSmallText: { fontWeight: '700', color: colors.muted },
  reviewButtons: { flexDirection: 'row', gap: spacing.xs },
  smallButton: { minHeight: 36, paddingHorizontal: spacing.md },
  ratingRow: { gap: 4, paddingTop: spacing.sm, borderTopWidth: StyleSheet.hairlineWidth, borderTopColor: colors.border },
  ratingHeader: { flexDirection: 'row', justifyContent: 'space-between', alignItems: 'center' },
  raterName: { fontWeight: '600', color: colors.text },
});
