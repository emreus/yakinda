import { Pressable, StyleSheet, Text, View } from 'react-native';
import { colors, spacing } from '@/theme';
import { formatRating } from '@/utils/format';

interface StarRatingProps {
  value: number;
  /** Verilirse yıldızlar tıklanabilir olur. */
  onChange?: (value: number) => void;
  size?: number;
}

export function StarRating({ value, onChange, size = 28 }: StarRatingProps) {
  return (
    <View style={styles.row} accessibilityRole={onChange ? 'adjustable' : 'text'} accessibilityValue={{ min: 0, max: 5, now: value }}>
      {[1, 2, 3, 4, 5].map((star) => (
        <Pressable key={star} disabled={!onChange} onPress={() => onChange?.(star)} hitSlop={6}>
          <Text style={{ fontSize: size, lineHeight: size * 1.15, color: star <= Math.round(value) ? colors.star : colors.border }}>★</Text>
        </Pressable>
      ))}
    </View>
  );
}

/** "★ 4,6 (23)" ya da puan yoksa "Yeni" */
export function RatingBadge({ value, count, emptyLabel = 'Yeni' }: { value: number | null; count: number; emptyLabel?: string }) {
  if (value == null || count === 0) {
    return <Text style={styles.empty}>{emptyLabel}</Text>;
  }
  return (
    <Text style={styles.badge}>
      <Text style={styles.star}>★ </Text>
      {formatRating(value)} <Text style={styles.count}>({count})</Text>
    </Text>
  );
}

/** Profilde 5'ten 1'e yıldız dağılımı çubukları. */
export function RatingDistribution({ distribution }: { distribution: number[] }) {
  const total = distribution.reduce((sum, n) => sum + n, 0);
  return (
    <View style={styles.distribution}>
      {[5, 4, 3, 2, 1].map((star) => {
        const count = distribution[star - 1] ?? 0;
        const ratio = total === 0 ? 0 : count / total;
        return (
          <View key={star} style={styles.distRow}>
            <Text style={styles.distLabel}>{star}★</Text>
            <View style={styles.distTrack}>
              <View style={[styles.distFill, { width: `${Math.round(ratio * 100)}%` as `${number}%` }]} />
            </View>
            <Text style={styles.distCount}>{count}</Text>
          </View>
        );
      })}
    </View>
  );
}

const styles = StyleSheet.create({
  row: { flexDirection: 'row', gap: spacing.xs },
  badge: { fontSize: 14, fontWeight: '600', color: colors.text },
  star: { color: colors.star },
  count: { fontWeight: '400', color: colors.muted },
  empty: { fontSize: 13, color: colors.muted },
  distribution: { gap: 6 },
  distRow: { flexDirection: 'row', alignItems: 'center', gap: spacing.sm },
  distLabel: { width: 28, fontSize: 13, color: colors.muted },
  distTrack: { flex: 1, height: 8, borderRadius: 4, backgroundColor: colors.background, overflow: 'hidden' },
  distFill: { height: 8, borderRadius: 4, backgroundColor: colors.star },
  distCount: { width: 28, textAlign: 'right', fontSize: 13, color: colors.muted },
});
