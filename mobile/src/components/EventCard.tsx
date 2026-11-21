import { router } from 'expo-router';
import { Pressable, StyleSheet, Text, View } from 'react-native';
import type { EventListItem } from '@/api/types';
import { colors, radius, spacing } from '@/theme';
import { formatDistance, formatEventDate, formatSpots } from '@/utils/format';
import { RatingBadge } from './StarRating';
import { Badge } from './ui';

export function EventCard({ event }: { event: EventListItem }) {
  const spots = formatSpots(event);
  // Backend kuralıyla aynı: bitiş yoksa başlangıç + 3 saat
  const effectiveEnd = event.endsAt
    ? new Date(event.endsAt).getTime()
    : new Date(event.startsAt).getTime() + 3 * 60 * 60 * 1000;
  const isPast = effectiveEnd < Date.now();

  return (
    <Pressable
      onPress={() => router.push(`/event/${event.id}`)}
      style={({ pressed }) => [styles.card, pressed && { opacity: 0.9 }]}
    >
      <View style={styles.emojiBox}>
        <Text style={styles.emoji}>{event.categoryEmoji}</Text>
      </View>

      <View style={styles.body}>
        <Text style={styles.title} numberOfLines={2}>
          {event.title}
        </Text>

        <Text style={styles.meta} numberOfLines={1}>
          {formatEventDate(event.startsAt)}
          {event.placeName ? ` · ${event.placeName}` : ''}
          {event.distanceMeters != null ? ` · ${formatDistance(event.distanceMeters)}` : ''}
        </Text>

        <View style={styles.footer}>
          <View style={styles.badges}>
            {event.status === 'Cancelled' ? (
              <Badge label="İptal edildi" tone="danger" />
            ) : isPast ? (
              <Badge label="Bitti" tone="neutral" />
            ) : (
              <Badge label={spots.label} tone={spots.tone === 'open' ? 'success' : spots.tone === 'full' ? 'danger' : 'neutral'} />
            )}
            {event.joinPolicy === 'ApprovalRequired' ? <Badge label="Onaylı" tone="primary" /> : null}
          </View>

          <View style={styles.organizer}>
            <Text style={styles.organizerName} numberOfLines={1}>
              {event.organizerName}
            </Text>
            <RatingBadge value={event.organizerRating} count={event.organizerRatingCount} />
          </View>
        </View>
      </View>
    </Pressable>
  );
}

const styles = StyleSheet.create({
  card: {
    flexDirection: 'row',
    gap: spacing.md,
    backgroundColor: colors.card,
    borderRadius: radius.lg,
    padding: spacing.md,
    borderWidth: StyleSheet.hairlineWidth,
    borderColor: colors.border,
  },
  emojiBox: {
    width: 52,
    height: 52,
    borderRadius: radius.md,
    backgroundColor: colors.primaryLight,
    alignItems: 'center',
    justifyContent: 'center',
  },
  emoji: { fontSize: 26 },
  body: { flex: 1, gap: 4 },
  title: { fontSize: 16, fontWeight: '700', color: colors.text },
  meta: { fontSize: 13, color: colors.muted },
  footer: { flexDirection: 'row', justifyContent: 'space-between', alignItems: 'center', marginTop: 4, gap: spacing.sm },
  badges: { flexDirection: 'row', gap: spacing.xs, flexShrink: 1, flexWrap: 'wrap' },
  organizer: { alignItems: 'flex-end', maxWidth: '45%' },
  organizerName: { fontSize: 12, color: colors.muted },
});
