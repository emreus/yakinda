import { router } from 'expo-router';
import { useEffect, useRef, useState } from 'react';
import {
  ActivityIndicator,
  FlatList,
  Linking,
  Pressable,
  RefreshControl,
  ScrollView,
  StyleSheet,
  Switch,
  Text,
  TextInput,
  View,
} from 'react-native';
import { errorMessage } from '@/api/client';
import { CategoryChips } from '@/components/CategoryChips';
import { EventCard } from '@/components/EventCard';
import { Chip, EmptyState } from '@/components/ui';
import { buildNearbyParams, useCategories, useNearbyEvents } from '@/hooks/queries';
import { useCurrentLocation } from '@/hooks/useCurrentLocation';
import { RADIUS_OPTIONS, useNearbyFilters } from '@/state/NearbyFilters';
import { colors, radius, spacing } from '@/theme';
import { uniqueBy } from '@/utils/list';

/**
 * Yakınımdaki etkinlikler listesi. Konumu alır, filtrelerle birlikte API parametrelerine çevirir ve
 * sonsuz kaydırmalı listeyi gösterir. Üstte arama kutusu, kategori, yarıçap, "sadece yeri olanlar" ve
 * sıralama seçenekleri bulunur. Konum izni yoksa ya da konum alınamazsa açıklayıcı bir durum gösterir.
 */
export default function NearbyScreen() {
  const location = useCurrentLocation();
  const { filters, updateFilters } = useNearbyFilters();
  const categories = useCategories();
  const params = buildNearbyParams(location.coords, filters);
  const query = useNearbyEvents(params);
  // Sayfalar arasında yeni etkinlik eklenirse aynı etkinlik iki sayfada gelebilir; liste tekrarsız kurulur.
  const events = uniqueBy(query.data?.pages.flatMap((page) => page.items) ?? [], (event) => event.id);

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
      <SearchBox value={filters.search} onChange={(search) => updateFilters({ search })} />
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
          <Text style={styles.sortLabel}>Sırala</Text>
          <Chip label="Yakınlık" selected={filters.sort === 'distance'} onPress={() => updateFilters({ sort: 'distance' })} />
          <Chip label="Zaman" selected={filters.sort === 'time'} onPress={() => updateFilters({ sort: 'time' })} />
          <Chip label="Puan" selected={filters.sort === 'rating'} onPress={() => updateFilters({ sort: 'rating' })} />
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
            location.reload();
            query.refetch();
          }}
          tintColor={colors.primary}
        />
      }
    />
  );
}

/** Liste kartları arasındaki boşluk. */
function Separator() {
  return <View style={{ height: spacing.md }} />;
}

const styles = StyleSheet.create({
  screen: { backgroundColor: colors.background },
  list: { padding: spacing.lg, paddingTop: spacing.sm },
  // Yatay chip satırları kenardan kenara kaysın diye liste padding'i geri alınır
  header: { marginHorizontal: -spacing.lg, marginBottom: spacing.md, gap: spacing.sm },
  chipRow: { gap: spacing.sm, paddingHorizontal: spacing.lg },
  // Anahtar ile sıralama seçenekleri telefon ekranında yan yana sığmaz: sığmadığında sıralama alt
  // satıra geçer, sığdığında (geniş ekran) aralarında en az columnGap kadar boşluk kalır.
  optionsRow: {
    flexDirection: 'row',
    flexWrap: 'wrap',
    justifyContent: 'space-between',
    alignItems: 'center',
    columnGap: spacing.lg,
    rowGap: spacing.sm,
    paddingHorizontal: spacing.lg,
  },
  switchRow: { flexDirection: 'row', alignItems: 'center', gap: spacing.sm, flexShrink: 1 },
  sortRow: { flexDirection: 'row', flexWrap: 'wrap', alignItems: 'center', gap: spacing.xs, flexShrink: 1 },
  optionText: { fontSize: 14, color: colors.text, flexShrink: 1 },
  sortLabel: { fontSize: 14, color: colors.muted, marginRight: spacing.xs },
  spinner: { marginTop: 48 },
  footer: { marginVertical: spacing.lg },
});

/** Yazmayı bırakınca (400 ms) aramayı uygular; her harfte istek atılmaz. */
function SearchBox({ value, onChange }: { value: string; onChange: (value: string) => void }) {
  const [text, setText] = useState(value);
  const onChangeRef = useRef(onChange);
  onChangeRef.current = onChange;

  useEffect(() => {
    const timer = setTimeout(() => {
      if (text !== value) onChangeRef.current(text);
    }, 400);
    return () => clearTimeout(timer);
  }, [text, value]);

  return (
    <View style={searchStyles.box}>
      <Text style={searchStyles.icon}>🔍</Text>
      <TextInput
        style={searchStyles.input}
        value={text}
        onChangeText={setText}
        placeholder="Etkinlik, mekan ara…"
        placeholderTextColor={colors.muted}
        returnKeyType="search"
        autoCorrect={false}
        clearButtonMode="while-editing"
        accessibilityLabel="Etkinlik ara"
      />
      {text.length > 0 ? (
        <Pressable onPress={() => setText('')} hitSlop={8} accessibilityLabel="Aramayı temizle">
          <Text style={searchStyles.clear}>✕</Text>
        </Pressable>
      ) : null}
    </View>
  );
}

const searchStyles = StyleSheet.create({
  box: {
    flexDirection: 'row',
    alignItems: 'center',
    gap: spacing.sm,
    marginHorizontal: spacing.lg,
    paddingHorizontal: spacing.md,
    borderRadius: radius.md,
    borderWidth: 1,
    borderColor: colors.border,
    backgroundColor: colors.card,
  },
  icon: { fontSize: 14 },
  input: { flex: 1, paddingVertical: spacing.sm, fontSize: 15, color: colors.text },
  clear: { fontSize: 14, color: colors.muted },
});
