import { StyleSheet, Text, View } from 'react-native';
import type { UserProfile } from '@/api/types';
import { colors, font, spacing } from '@/theme';
import { formatRating } from '@/utils/format';
import { RatingDistribution, StarRating } from './StarRating';
import { Card } from './ui';

const memberSince = new Intl.DateTimeFormat('tr-TR', { month: 'long', year: 'numeric' });

/** Kullanıcının organizatör puanını ve istatistiklerini gösteren kart. */
export function ProfileHeader({ profile }: { profile: UserProfile }) {
  return (
    <Card style={styles.card}>
      <View style={styles.top}>
        <View style={styles.avatar}>
          <Text style={styles.avatarText}>{profile.displayName.charAt(0).toLocaleUpperCase('tr-TR')}</Text>
        </View>
        <View style={styles.flex}>
          <Text style={font.title}>{profile.displayName}</Text>
          <Text style={font.small}>{memberSince.format(new Date(profile.createdAt))} tarihinden beri üye</Text>
        </View>
      </View>

      {profile.bio ? <Text style={font.body}>{profile.bio}</Text> : null}

      <View style={styles.ratingBox}>
        <View style={styles.ratingSummary}>
          <Text style={styles.bigRating}>{profile.ratingCount > 0 ? formatRating(profile.averageRating) : '–'}</Text>
          <StarRating value={profile.averageRating ?? 0} size={16} />
          <Text style={font.small}>
            {profile.ratingCount > 0 ? `${profile.ratingCount} oy` : 'Henüz oy yok'}
          </Text>
        </View>
        <View style={styles.flex}>
          <RatingDistribution distribution={profile.ratingDistribution} />
        </View>
      </View>
      <Text style={styles.caption}>Organizatör puanı: düzenlediği etkinliklere katılımcıların verdiği tüm oyların ortalaması.</Text>

      <View style={styles.stats}>
        <Stat value={profile.organizedEventCount} label="Düzenlediği" />
        <Stat value={profile.joinedEventCount} label="Katıldığı" />
      </View>
    </Card>
  );
}

function Stat({ value, label }: { value: number; label: string }) {
  return (
    <View style={styles.stat}>
      <Text style={styles.statValue}>{value}</Text>
      <Text style={font.small}>{label}</Text>
    </View>
  );
}

const styles = StyleSheet.create({
  flex: { flex: 1 },
  card: { gap: spacing.lg },
  top: { flexDirection: 'row', alignItems: 'center', gap: spacing.md },
  avatar: {
    width: 60,
    height: 60,
    borderRadius: 30,
    backgroundColor: colors.primaryLight,
    alignItems: 'center',
    justifyContent: 'center',
  },
  avatarText: { fontSize: 26, fontWeight: '700', color: colors.primary },
  ratingBox: { flexDirection: 'row', alignItems: 'center', gap: spacing.lg },
  ratingSummary: { alignItems: 'center', gap: 2, minWidth: 90 },
  bigRating: { fontSize: 40, fontWeight: '800', color: colors.text },
  caption: { fontSize: 12, color: colors.muted },
  stats: { flexDirection: 'row', borderTopWidth: StyleSheet.hairlineWidth, borderTopColor: colors.border, paddingTop: spacing.md },
  stat: { flex: 1, alignItems: 'center' },
  statValue: { fontSize: 20, fontWeight: '700', color: colors.text },
});
