import { router } from 'expo-router';
import { ActivityIndicator, FlatList, Linking, RefreshControl, ScrollView, StyleSheet, Switch, Text, View } from 'react-native';
import { errorMessage } from '@/api/client';
import { CategoryChips } from '@/components/CategoryChips';
import { EventCard } from '@/components/EventCard';
import { Chip, EmptyState } from '@/components/ui';
import { buildNearbyParams, useCategories, useNearbyEvents } from '@/hooks/queries';
import { useCurrentLocation } from '@/hooks/useCurrentLocation';
import { RADIUS_OPTIONS, useNearbyFilters } from '@/state/NearbyFilters';
import { colors, spacing } from '@/theme';

export default function NearbyScreen() {
  const location = useCurrentLocation();
  const { filters, updateFilters } = useNearbyFilters();
  const categories = useCategories();
  const params = buildNearbyParams(location.coords, filters);
  const query = useNearbyEvents(params);
  const events = query.data?.pages.flatMap((page) => page.items) ?? [];

  if (location.status === 'denied') {
    return (
      <EmptyState
        emoji="📍"
        title="Konum izni gerekli"
        message="Yakınındaki etkinlikleri gösterebilmemiz için konum iznine ihtiyacımız var."
        actionLabel="Ayarları aç"
        onAction={() => Linking.openSettings()}
      />
    );
  }

  if (location.status === 'error') {
    return (
      <EmptyState
        emoji="🛰️"
        title="Konum alınamadı"
        message="GPS'in açık olduğundan emin ol."
        actionLabel="Tekrar dene"
        onAction={location.reload}
      />
    );
  }

  const header = (
    <View style={styles.header}>
      <CategoryChips
        categories={categories.data ?? []}
        selectedId={filters.categoryId}
        onSelect={(categoryId) => updateFilters({ categoryId })}
      />

      <ScrollView horizontal showsHorizontalScrollIndicator={false} contentContainerStyle={styles.chipRow}>
        {RADIUS_OPTIONS.map((km) => (
          <Chip key={km} label={`${km} km`} selected={filters.radiusKm === km} onPress={() => updateFilters({ radiusKm: km })} />
        ))}
      </ScrollView>

      <View style={styles.optionsRow}>
        <View style={styles.switchRow}>
          <Switch
            value={filters.onlyAvailable}
            onValueChange={(onlyAvailable) => updateFilters({ onlyAvailable })}
            trackColor={{ true: colors.primary, false: colors.border }}
          />
          <Text style={styles.optionText}>Sadece yeri olanlar</Text>
        </View>
        <View style={styles.sortRow}>
          <Chip label="Yakınlık" selected={filters.sort === 'distance'} onPress={() => updateFilters({ sort: 'distance' })} />
          <Chip label="Zaman" selected={filters.sort === 'time'} onPress={() => updateFilters({ sort: 'time' })} />
        </View>
      </View>
    </View>
  );

  const empty =
    query.isLoading || !params ? (
      <ActivityIndicator style={styles.spinner} color={colors.primary} />
    ) : query.isError ? (
      <EmptyState
        emoji="⚠️"
        title="Etkinlikler yüklenemedi"
        message={errorMessage(query.error)}
        actionLabel="Tekrar dene"
        onAction={() => query.refetch()}
      />
    ) : (
      <EmptyState
        emoji="🌙"
        title="Yakınında etkinlik yok"
        message={`${filters.radiusKm} km içinde şu an bir şey görünmüyor. Yarıçapı büyüt ya da ilk etkinliği sen başlat!`}
        actionLabel="Etkinlik oluştur"
        onAction={() => router.push('/create')}
      />
    );

  return (
    <FlatList
      style={styles.screen}
      data={events}
      keyExtractor={(item) => item.id}
      renderItem={({ item }) => <EventCard event={item} />}
      ItemSeparatorComponent={Separator}
      ListHeaderComponent={header}
      ListEmptyComponent={empty}
      ListFooterComponent={query.isFetchingNextPage ? <ActivityIndicator style={styles.footer} color={colors.primary} /> : null}
      contentContainerStyle={styles.list}
      onEndReachedThreshold={0.4}
      onEndReached={() => {
        if (query.hasNextPage && !query.isFetchingNextPage) query.fetchNextPage();
      }}
      refreshControl={
        <RefreshControl
          refreshing={query.isRefetching && !query.isFetchingNextPage}
          onRefresh={() => {
            location.reload();
            query.refetch();
          }}
          tintColor={colors.primary}
        />
      }
    />
  );
}

function Separator() {
  return <View style={{ height: spacing.md }} />;
}

const styles = StyleSheet.create({
  screen: { backgroundColor: colors.background },
  list: { padding: spacing.lg, paddingTop: spacing.sm },
  // Yatay chip satırları kenardan kenara kaysın diye liste padding'i geri alınır
  header: { marginHorizontal: -spacing.lg, marginBottom: spacing.md, gap: spacing.sm },
  chipRow: { gap: spacing.sm, paddingHorizontal: spacing.lg },
  optionsRow: {
    flexDirection: 'row',
    justifyContent: 'space-between',
    alignItems: 'center',
    paddingHorizontal: spacing.lg,
  },
  switchRow: { flexDirection: 'row', alignItems: 'center', gap: spacing.sm },
  sortRow: { flexDirection: 'row', gap: spacing.xs },
  optionText: { fontSize: 14, color: colors.text },
  spinner: { marginTop: 48 },
  footer: { marginVertical: spacing.lg },
});
