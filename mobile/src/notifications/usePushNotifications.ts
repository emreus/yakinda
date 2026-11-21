import { useQueryClient } from '@tanstack/react-query';
import { router } from 'expo-router';
import { useEffect } from 'react';
import { notificationsApi } from '@/api/endpoints';
import { queryKeys } from '@/hooks/queries';
import { isPushAvailable, registerForPushAsync, subscribeToPush } from './push';

/**
 * Giriş yapılmışken push'u etkinleştirir: token'ı kaydeder, gelen bildirimde listeyi tazeler,
 * bildirime dokununca ilgili etkinliği açar ve bildirimi okundu yapar.
 * Expo Go'da hiçbir şey yapmaz (orada bildirimler uygulama içi listeden takip edilir).
 */
export function usePushNotifications(enabled: boolean) {
  const queryClient = useQueryClient();

  useEffect(() => {
    if (!enabled || !isPushAvailable()) return;

    let cancelled = false;
    let unsubscribe: (() => void) | undefined;

    registerForPushAsync().catch((error) => console.warn('[push] token kaydedilemedi', error));

    subscribeToPush({
      onReceive: () => {
        queryClient.invalidateQueries({ queryKey: queryKeys.notifications });
      },
      onOpen: ({ notificationId, eventId }) => {
        if (notificationId) {
          notificationsApi
            .markRead(notificationId)
            .catch(() => undefined)
            .finally(() => queryClient.invalidateQueries({ queryKey: queryKeys.notifications }));
        }
        if (eventId) router.push(`/event/${eventId}`);
      },
    })
      .then((remove) => {
        if (cancelled) remove();
        else unsubscribe = remove;
      })
      .catch((error) => console.warn('[push] dinleyiciler kurulamadı', error));

    return () => {
      cancelled = true;
      unsubscribe?.();
    };
  }, [enabled, queryClient]);
}
