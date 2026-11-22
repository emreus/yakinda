import { useInfiniteQuery, useQuery, useQueryClient } from '@tanstack/react-query';
import { useCallback } from 'react';
import { categoriesApi, eventsApi, notificationsApi, usersApi, type MyEventsRole, type NearbyParams } from '@/api/endpoints';
import type { NearbyFilters } from '@/state/NearbyFilters';
import { roundCoords, type Coords } from './useCurrentLocation';

export const queryKeys = {
  categories: ['categories'] as const,
  nearbyAll: ['events', 'nearby'] as const,
  /**
   * Yakındakiler listesinin anahtarı; konum ya da filtre değişince anahtar da değişir ve liste yeniden istenir.
   */
  nearby: (params: NearbyParams | null) => ['events', 'nearby', params] as const,
  /** Tek etkinliğin detayının anahtarı. */
  event: (id: string) => ['event', id] as const,
  /**
   * Etkinliğin katılımcı listesinin anahtarı. Etkinlik anahtarıyla başladığı için etkinlik yenilenince o da yenilenir.
   */
  participants: (id: string) => ['event', id, 'participants'] as const,
  /** Etkinliğin yorum listesinin anahtarı. */
  ratings: (id: string) => ['event', id, 'ratings'] as const,
  me: ['me'] as const,
  /** Başka bir kullanıcının profilinin anahtarı. */
  user: (id: string) => ['user', id] as const,
  /** "Düzenlediklerim" ya da "Katıldıklarım" listesinin anahtarı. */
  myEvents: (role: MyEventsRole) => ['myEvents', role] as const,
  /** Tüm bildirim sorgularının ortak öneki (liste + okunmamış sayısı) */
  notifications: ['notifications'] as const,
  notificationList: ['notifications', 'list'] as const,
  unreadNotifications: ['notifications', 'unread'] as const,
};

/**
 * Kategorileri okur. Liste neredeyse hiç değişmediği için hiç bayatlamaz sayılır (uygulama açıkken bir kez istenir).
 */
export function useCategories() {
  return useQuery({
    queryKey: queryKeys.categories,
    queryFn: () => categoriesApi.list(),
    staleTime: Infinity,
  });
}

/**
 * Konum ve filtrelerden API parametrelerini üretir. Konum henüz yoksa null döner (sorgu çalışmaz).
 * Koordinat yaklaşık 100 metreye yuvarlanır; arama metni kırpılır, boşsa gönderilmez.
 */
export function buildNearbyParams(
  coords: Coords | undefined,
  filters: NearbyFilters,
  pageSize = 20,
): NearbyParams | null {
  if (!coords) return null;
  const rounded = roundCoords(coords);
  return {
    lat: rounded.latitude,
    lng: rounded.longitude,
    radiusKm: filters.radiusKm,
    categoryId: filters.categoryId,
    onlyAvailable: filters.onlyAvailable,
    sort: filters.sort,
    q: filters.search.trim() || null,
    pageSize,
  };
}

/** Sonsuz kaydırmalı "yakınımdaki etkinlikler" listesi. */
export function useNearbyEvents(params: NearbyParams | null) {
  return useInfiniteQuery({
    queryKey: queryKeys.nearby(params),
    enabled: params !== null,
    initialPageParam: 1,
    queryFn: ({ pageParam }) => eventsApi.nearby({ ...(params as NearbyParams), page: pageParam }),
    getNextPageParam: (lastPage) => (lastPage.hasMore ? lastPage.page + 1 : undefined),
  });
}

/** Etkinlik detayını önbellekli okur. */
export function useEventDetail(id: string) {
  return useQuery({ queryKey: queryKeys.event(id), queryFn: () => eventsApi.detail(id) });
}

/** Etkinliğin katılımcılarını okur; enabled false ise istek atılmaz. */
export function useParticipants(id: string, enabled = true) {
  return useQuery({ queryKey: queryKeys.participants(id), queryFn: () => eventsApi.participants(id), enabled });
}

/** Etkinliğe gelen puan ve yorumları okur. */
export function useRatings(id: string) {
  return useQuery({ queryKey: queryKeys.ratings(id), queryFn: () => eventsApi.ratings(id) });
}

/** Giriş yapmış kullanıcının kendi profilini okur. */
export function useMyProfile() {
  return useQuery({ queryKey: queryKeys.me, queryFn: () => usersApi.me() });
}

/** Başka bir kullanıcının profilini okur. */
export function useUserProfile(id: string) {
  return useQuery({ queryKey: queryKeys.user(id), queryFn: () => usersApi.get(id) });
}

/** Düzenlediğim ya da katıldığım etkinlikleri okur. */
export function useMyEvents(role: MyEventsRole) {
  return useQuery({ queryKey: queryKeys.myEvents(role), queryFn: () => usersApi.myEvents(role) });
}

/** Bildirim listesi, en yeni önce (sonsuz kaydırma). */
export function useNotifications() {
  return useInfiniteQuery({
    queryKey: queryKeys.notificationList,
    initialPageParam: 1,
    queryFn: ({ pageParam }) => notificationsApi.list(pageParam),
    getNextPageParam: (lastPage) => (lastPage.hasMore ? lastPage.page + 1 : undefined),
  });
}

/**
 * Sekmedeki rozet. Push olmasa da (Expo Go) yeni bildirimler 30 saniye içinde görünsün diye
 * periyodik olarak sorulur; istek tek bir indeksli COUNT olduğu için ucuz.
 */
export function useUnreadNotificationCount(enabled = true) {
  return useQuery({
    queryKey: queryKeys.unreadNotifications,
    queryFn: () => notificationsApi.unreadCount(),
    enabled,
    refetchInterval: 30_000,
    staleTime: 10_000,
  });
}

/** Bir etkinlikte değişiklik olduğunda ilgili tüm önbellekleri tazeler. */
export function useInvalidateEvent() {
  const queryClient = useQueryClient();
  return useCallback(
    (eventId?: string) => {
      if (eventId) queryClient.invalidateQueries({ queryKey: queryKeys.event(eventId) });
      queryClient.invalidateQueries({ queryKey: queryKeys.nearbyAll });
      queryClient.invalidateQueries({ queryKey: ['myEvents'] });
      queryClient.invalidateQueries({ queryKey: queryKeys.me });
    },
    [queryClient],
  );
}
