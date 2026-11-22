import { isRunningInExpoGo } from 'expo';
import Constants from 'expo-constants';
import type { NotificationResponse } from 'expo-notifications';
import { Platform } from 'react-native';
import { devicesApi } from '@/api/endpoints';

/**
 * Push bildirimleri (telefonun bildirim çubuğu / kilit ekranı).
 *
 * Expo Go'da (SDK 53+) push çalışmaz; development build ya da mağaza sürümü gerekir.
 * Bu yüzden expo-notifications modülü sadece gerektiğinde ve Expo Go dışında yüklenir:
 * Expo Go'da modül hiç yüklenmez, uyarı da basılmaz. Uygulama içi bildirim listesi
 * push'tan bağımsızdır ve her yerde çalışır.
 */

type NotificationsModule = typeof import('expo-notifications');

let modulePromise: Promise<NotificationsModule> | null = null;
let registeredToken: string | null = null;

/**
 * expo-notifications modülünü ilk ihtiyaçta, bir kez yükler. Modül Expo Go'da yüklenince hata verdiği
 * için dosyanın başında değil burada içe aktarılır.
 */
function loadNotifications(): Promise<NotificationsModule> {
  modulePromise ??= import('expo-notifications');
  return modulePromise;
}

/** Push bu ortamda çalışır mı? Web'de ve Expo Go'da false döner. */
export function isPushAvailable(): boolean {
  return Platform.OS !== 'web' && !isRunningInExpoGo();
}

/** `npx eas init` çalıştırılınca app.json'a yazılan proje kimliği. Yoksa push token alınamaz. */
function easProjectId(): string | undefined {
  const fromExtra = Constants.expoConfig?.extra?.eas?.projectId;
  if (typeof fromExtra === 'string' && fromExtra) return fromExtra;
  return Constants.easConfig?.projectId ?? undefined;
}

/**
 * İzin ister, Expo push token'ını alır ve backend'e kaydeder.
 * İzin verilmezse ya da push kullanılamıyorsa sessizce çıkar.
 */
export async function registerForPushAsync(): Promise<void> {
  if (!isPushAvailable()) return;

  const projectId = easProjectId();
  if (!projectId) {
    if (__DEV__) console.info('[push] EAS projectId yok, push kapalı. Kurulum için README → Bildirimler.');
    return;
  }

  const Notifications = await loadNotifications();

  if (Platform.OS === 'android') {
    // Android 8+ bildirimleri bir kanala bağlı gösterir; backend mesajları "default" kanalına yollar.
    await Notifications.setNotificationChannelAsync('default', {
      name: 'Bildirimler',
      importance: Notifications.AndroidImportance.HIGH,
    });
  }

  let permission = await Notifications.getPermissionsAsync();
  if (!permission.granted) permission = await Notifications.requestPermissionsAsync();
  if (!permission.granted) return;

  const { data: token } = await Notifications.getExpoPushTokenAsync({ projectId });
  await devicesApi.register(token, Platform.OS === 'ios' ? 'ios' : 'android');
  registeredToken = token;
}

/** Çıkış yaparken: bu cihaza artık bu hesabın bildirimleri gitmesin. */
export async function unregisterPushAsync(): Promise<void> {
  const token = registeredToken;
  registeredToken = null;
  if (token) await devicesApi.unregister(token);
}

export interface OpenedNotification {
  notificationId: string | null;
  eventId: string | null;
  /** Bildirim türü (ör. "ChatMessage" → sohbet ekranı açılır) */
  type: string | null;
}

interface PushHandlers {
  /** Uygulama açıkken bir bildirim geldi. */
  onReceive: () => void;
  /** Kullanıcı bildirime dokundu (uygulama kapalıyken dokunulduysa açılışta bir kez çağrılır). */
  onOpen: (opened: OpenedNotification) => void;
}

/**
 * Dokunulan bildirimin taşıdığı verilerden bildirim kimliğini, etkinlik kimliğini ve türü okur; metin
 * olmayan değerleri null sayar.
 */
function readOpened(response: NotificationResponse): OpenedNotification {
  const data = response.notification.request.content.data ?? {};
  return {
    notificationId: typeof data.notificationId === 'string' ? data.notificationId : null,
    eventId: typeof data.eventId === 'string' ? data.eventId : null,
    type: typeof data.type === 'string' ? data.type : null,
  };
}

/** Bildirim dinleyicilerini kurar; dönen fonksiyon dinleyicileri kaldırır. */
export async function subscribeToPush(handlers: PushHandlers): Promise<() => void> {
  if (!isPushAvailable()) return () => {};

  const Notifications = await loadNotifications();

  // Uygulama ön plandayken gelen bildirim de banner olarak gösterilsin.
  Notifications.setNotificationHandler({
    handleNotification: async () => ({
      shouldShowBanner: true,
      shouldShowList: true,
      shouldPlaySound: true,
      shouldSetBadge: false,
    }),
  });

  const received = Notifications.addNotificationReceivedListener(() => handlers.onReceive());
  const responded = Notifications.addNotificationResponseReceivedListener((response) =>
    handlers.onOpen(readOpened(response)),
  );

  // Uygulama kapalıyken bildirime dokunulup açıldıysa
  const last = await Notifications.getLastNotificationResponseAsync();
  if (last) {
    await Notifications.clearLastNotificationResponseAsync();
    handlers.onOpen(readOpened(last));
  }

  return () => {
    received.remove();
    responded.remove();
  };
}
