import { ScrollView, StyleSheet } from 'react-native';
import type { Category } from '@/api/types';
import { spacing } from '@/theme';
import { Chip } from './ui';

interface CategoryChipsProps {
  categories: Category[];
  selectedId: number | null;
  onSelect: (id: number | null) => void;
  /** "Tümü" seçeneği gösterilsin mi (filtrelerde evet, formda hayır) */
  allowAll?: boolean;
  /** Yatay kenar boşluğu (kaydırma kenardan kenara olsun diye içerikte verilir) */
  inset?: number;
}

export function CategoryChips({ categories, selectedId, onSelect, allowAll = true, inset = spacing.lg }: CategoryChipsProps) {
  return (
    <ScrollView
      horizontal
      showsHorizontalScrollIndicator={false}
      contentContainerStyle={[styles.row, { paddingHorizontal: inset }]}
    >
      {allowAll ? <Chip label="Tümü" selected={selectedId === null} onPress={() => onSelect(null)} /> : null}
      {categories.map((category) => (
        <Chip
          key={category.id}
          label={`${category.emoji} ${category.name}`}
          selected={selectedId === category.id}
          onPress={() => onSelect(allowAll && selectedId === category.id ? null : category.id)}
        />
      ))}
    </ScrollView>
  );
}

const styles = StyleSheet.create({
  row: { gap: spacing.sm, paddingVertical: spacing.xs },
});
