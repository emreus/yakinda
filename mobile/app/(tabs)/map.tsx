import { router } from 'expo-router';
import { useRef, useState } from 'react';
import { ActivityIndicator, Linking, Pressable, StyleSheet, Text, View } from 'react-native';
import MapView, { Marker, type Region } from 'react-native-maps';
import { Button, EmptyState, FullScreenLoader } from '@/components/ui';
import { buildNearbyParams, useNearbyEvents } from '@/hooks/queries';
import { useCurrentLocation, type Coords } from '@/hooks/useCurrentLocation';
import { useNearbyFilters } from '@/state/NearbyFilters';
import { colors, radius, spacing } from '@/theme';
import { formatEventDate, formatSpots } from '@/utils/format';

/** Yarıçapın tamamı ekrana sığacak kadar zoom. */
function regionFor(center: Coords, radiusKm: number): Region {
  const latitudeDelta = (radiusKm / 111) * 2.4;
  const longitudeDelta = latitudeDelta / Math.max(0.2, Math.cos((center.latitude * Math.PI) / 180));
  return { ...center, latitudeDelta, longitudeDelta };
}

/** Kaba mesafe (km) – "bu bölgede ara" butonunu göstermek için yeterli. */
function approxDistanceKm(a: Coords, b: Coords): number {
  const dLat = (a.latitude - b.latitude) * 111;
  const dLng = (a.longitude - b.longitude) * 111 * Math.cos((a.latitude * Math.PI) / 180);
  return Math.sqrt(dLat * dLat + dLng * dLng);
}

export default function MapScreen() {
  const location = useCurrentLocation();
  const { filters } = useNearbyFilters();
  const mapRef = useRef<MapView>(null);
  const [searchCenter, setSearchCenter] = useState<Coords | null>(null);
  const [visibleRegion, setVisibleRegion] = useState<Region | null>(null);

  const center = searchCenter ?? location.coords;
  const query = useNearbyEvents(buildNearbyParams(center, filters, 50));
  const events = query.data?.pages.flatMap((page) => page.items) ?? [];

  if (location.status === 'denied') {
    return (
      <EmptyState
        emoji="🗺️"
        title="Konum izni gerekli"
        message="Haritada yakınındaki etkinlikleri göstermek için konum iznine ihtiyacımız var."
        actionLabel="Ayarları aç"
        onAction={() => Linking.openSettings()}
      />
    );
  }

  if (!location.coords || !center) return <FullScreenLoader />;

  const movedAway =
    visibleRegion !== null && approxDistanceKm(visibleRegion, center) > Math.max(0.5, filters.radiusKm * 0.3);

  return (
    <View style={styles.container}>
      <MapView
        ref={mapRef}
        style={StyleSheet.absoluteFill}
        initialRegion={regionFor(location.coords, filters.radiusKm)}
        showsUserLocation
        showsMyLocationButton={false}
        onRegionChangeComplete={setVisibleRegion}
      >
        {events.map((event) => (
          <Marker
            key={event.id}
            coordinate={{ latitude: event.latitude, longitude: event.longitude }}
            title={`${event.categoryEmoji} ${event.title}`}
            description={`${formatEventDate(event.startsAt)} · ${formatSpots(event).label}`}
            pinColor={colors.primary}
            onCalloutPress={() => router.push(`/event/${event.id}`)}
          />
        ))}
      </MapView>

      {movedAway && visibleRegion ? (
        <View style={styles.searchHereWrap} pointerEvents="box-none">
          <Button
            title="Bu bölgede ara"
            onPress={() => setSearchCenter({ latitude: visibleRegion.latitude, longitude: visibleRegion.longitude })}
            style={styles.searchHere}
          />
        </View>
      ) : null}

      {query.isFetching ? (
        <View style={styles.loading}>
          <ActivityIndicator color={colors.primary} />
        </View>
      ) : null}

      <View style={styles.info}>
        <Text style={styles.infoText}>
          {events.length} etkinlik · {filters.radiusKm} km
        </Text>
      </View>

      <Pressable
        style={styles.myLocation}
        onPress={() => {
          setSearchCenter(null);
          if (location.coords) mapRef.current?.animateToRegion(regionFor(location.coords, filters.radiusKm), 400);
        }}
      >
        <Text style={styles.myLocationIcon}>📍</Text>
      </Pressable>
    </View>
  );
}

const styles = StyleSheet.create({
  container: { flex: 1 },
  searchHereWrap: { position: 'absolute', top: spacing.lg, left: 0, right: 0, alignItems: 'center' },
  searchHere: { paddingHorizontal: spacing.xl, borderRadius: radius.pill, minHeight: 40 },
  loading: {
    position: 'absolute',
    top: spacing.lg,
    right: spacing.lg,
    backgroundColor: colors.card,
    borderRadius: radius.pill,
    padding: spacing.sm,
  },
  info: {
    position: 'absolute',
    bottom: spacing.lg,
    left: spacing.lg,
    backgroundColor: colors.card,
    borderRadius: radius.pill,
    paddingHorizontal: spacing.md,
    paddingVertical: spacing.sm,
  },
  infoText: { fontSize: 13, fontWeight: '600', color: colors.text },
  myLocation: {
    position: 'absolute',
    bottom: spacing.lg,
    right: spacing.lg,
    width: 48,
    height: 48,
    borderRadius: 24,
    backgroundColor: colors.card,
    alignItems: 'center',
    justifyContent: 'center',
    shadowColor: '#000',
    shadowOpacity: 0.15,
    shadowRadius: 6,
    shadowOffset: { width: 0, height: 2 },
    elevation: 3,
  },
  myLocationIcon: { fontSize: 22 },
});
