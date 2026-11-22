import { router } from 'expo-router';
import { Image, Pressable, StyleSheet, Text, View } from 'react-native';
import { mediaUrl } from '@/api/client';
import type { EventListItem } from '@/api/types';
import { colors, radius, spacing } from '@/theme';
import { formatDistance, formatEventBadge, formatEventDate } from '@/utils/format';
import { RatingBadge } from './StarRating';
import { Badge } from './ui';

/**
 * Liste kartı: solda kapak görseli (yoksa kategori emojisi), yanında başlık, tarih, mekan ve mesafe;
 * altta durum rozeti ile organizatörün adı ve puanı. Rozet iptal edilmiş etkinlikte "İptal edildi",
 * kaldırılmışta "Kaldırıldı", bitmişte "Bitti", başlamışta "Başladı", diğerlerinde kontenjan durumunu
 * yazar (formatEventBadge). Dokununca etkinlik detayına gider.
 */
export function EventCard({ event }: { event: EventListItem }) {
  const badge = formatEventBadge(event);
  const cover = mediaUrl(event.imageUrl);

  return (
    <Pressable
      onPress={() => router.push(`/event/${event.id}`)}
      style={({ pressed }) => [styles.card, pressed && { opacity: 0.9 }]}
    >
      {cover ? (
        <Image source={{ uri: cover }} style={styles.thumb} accessibilityIgnoresInvertColors />
      ) : (
        <View style={styles.emojiBox}>
          <Text style={styles.emoji}>{event.categoryEmoji}</Text>
        </View>
      )}

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
            <Badge label={badge.label} tone={badge.tone} />
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
  thumb: { width: 52, height: 52, borderRadius: radius.md, backgroundColor: colors.primaryLight },
  body: { flex: 1, gap: 4 },
  title: { fontSize: 16, fontWeight: '700', color: colors.text },
  meta: { fontSize: 13, color: colors.muted },
  footer: { flexDirection: 'row', justifyContent: 'space-between', alignItems: 'center', marginTop: 4, gap: spacing.sm },
  badges: { flexDirection: 'row', gap: spacing.xs, flexShrink: 1, flexWrap: 'wrap' },
  organizer: { alignItems: 'flex-end', maxWidth: '45%' },
  organizerName: { fontSize: 12, color: colors.muted },
});
