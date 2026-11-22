import { apiRequest } from './client';
import { seg } from './path';
import type {
  AppNotification,
  AuthResponse,
  BlockedUser,
  ChatMessage,
  ChatPage,
  Category,
  CreateEventInput,
  EventDetail,
  EventListItem,
  PagedResult,
  Participant,
  ParticipantStatus,
  RateResult,
  ReportReason,
  ReportTargetType,
  UpdateEventInput,
  Rating,
  UserProfile,
} from './types';

export const authApi = {
  /** POST /api/auth/login: e-posta ve şifreyle giriş. */
  login: (email: string, password: string) =>
    apiRequest<AuthResponse>('/api/auth/login', { method: 'POST', body: { email, password }, anonymous: true }),

  /** POST /api/auth/register: yeni hesap; koşulların kabul edildiği bilgisiyle birlikte. */
  register: (email: string, password: string, displayName: string, acceptTerms: boolean) =>
    apiRequest<AuthResponse>('/api/auth/register', {
      method: 'POST',
      body: { email, password, displayName, acceptTerms },
      anonymous: true,
    }),

  /** E-postaya 6 haneli kod gönderir (hesap yoksa da aynı yanıt). */
  forgotPassword: (email: string) =>
    apiRequest<void>('/api/auth/forgot-password', { method: 'POST', body: { email }, anonymous: true }),

  /** Kod doğruysa şifreyi değiştirir ve giriş yapılmış oturum döner. */
  resetPassword: (email: string, code: string, newPassword: string) =>
    apiRequest<AuthResponse>('/api/auth/reset-password', {
      method: 'POST',
      body: { email, code, newPassword },
      anonymous: true,
    }),

  /** POST /api/auth/refresh: refresh token ile yeni oturum bilgisi alır. */
  refresh: (refreshToken: string) =>
    apiRequest<AuthResponse>('/api/auth/refresh', { method: 'POST', body: { refreshToken }, anonymous: true }),

  /** Çıkışta refresh token'ı sunucuda da iptal eder (telefondan silmek tek başına yetmez). */
  logout: (refreshToken: string) =>
    apiRequest<void>('/api/auth/logout', { method: 'POST', body: { refreshToken }, anonymous: true }),
};

export const categoriesApi = {
  /** GET /api/categories: kategori listesi (giriş gerektirmez). */
  list: () => apiRequest<Category[]>('/api/categories', { anonymous: true }),
};

export type NearbySort = 'distance' | 'time' | 'rating';

export type NearbyParams = {
  lat: number;
  lng: number;
  radiusKm: number;
  categoryId?: number | null;
  onlyAvailable?: boolean;
  sort?: NearbySort;
  /** Başlık, açıklama ya da mekan adında arama */
  q?: string | null;
  page?: number;
  pageSize?: number;
};

export const eventsApi = {
  /** GET /api/events/nearby: konuma ve filtrelere göre yakındaki etkinlikler, sayfalı. */
  nearby: (params: NearbyParams) =>
    apiRequest<PagedResult<EventListItem>>('/api/events/nearby', { query: { ...params } }),

  /** GET /api/events/{id}: etkinlik detayı. */
  detail: (id: string) => apiRequest<EventDetail>(`/api/events/${seg(id)}`),

  /** POST /api/events: etkinlik oluşturur; yeni etkinliğin kimliğini döner. */
  create: (input: CreateEventInput) =>
    apiRequest<{ id: string }>('/api/events', { method: 'POST', body: input }),

  /** PUT /api/events/{id}: etkinliği düzenler. */
  update: (id: string, input: UpdateEventInput) =>
    apiRequest<void>(`/api/events/${seg(id)}`, { method: 'PUT', body: input }),

  /** POST /api/events/{id}/cancel: etkinliği iptal eder. */
  cancel: (id: string) => apiRequest<void>(`/api/events/${seg(id)}/cancel`, { method: 'POST' }),

  /** POST /api/events/{id}/join: katılır; alınan durumu döner (onaylı, bekleyen ya da bekleme listesinde). */
  join: (id: string) => apiRequest<{ status: ParticipantStatus }>(`/api/events/${seg(id)}/join`, { method: 'POST' }),

  /** POST /api/events/{id}/leave: etkinlikten ya da bekleme listesinden ayrılır. */
  leave: (id: string) => apiRequest<void>(`/api/events/${seg(id)}/leave`, { method: 'POST' }),

  /** GET /api/events/{id}/participants: katılımcılar. */
  participants: (id: string) => apiRequest<Participant[]>(`/api/events/${seg(id)}/participants`),

  /** POST .../participants/{userId}/approve: organizatör isteği onaylar. */
  approve: (id: string, userId: string) =>
    apiRequest<void>(`/api/events/${seg(id)}/participants/${seg(userId)}/approve`, { method: 'POST' }),

  /** POST .../participants/{userId}/reject: organizatör isteği reddeder ya da katılımcıyı çıkarır. */
  reject: (id: string, userId: string) =>
    apiRequest<void>(`/api/events/${seg(id)}/participants/${seg(userId)}/reject`, { method: 'POST' }),

  /** PUT /api/events/{id}/rating: puan verir ya da önceki puanı günceller. */
  rate: (id: string, score: number, comment: string | null) =>
    apiRequest<RateResult>(`/api/events/${seg(id)}/rating`, { method: 'PUT', body: { score, comment } }),

  /** GET /api/events/{id}/ratings: puanlar ve yorumlar, sayfalı. */
  ratings: (id: string, page = 1) =>
    apiRequest<PagedResult<Rating>>(`/api/events/${seg(id)}/ratings`, { query: { page, pageSize: 20 } }),
};

export type MyEventsRole = 'organizing' | 'attending';

export const usersApi = {
  /** GET /api/users/me: kendi profilim. */
  me: () => apiRequest<UserProfile>('/api/users/me'),
  /** GET /api/users/{id}: başka bir kullanıcının profili. */
  get: (id: string) => apiRequest<UserProfile>(`/api/users/${seg(id)}`),
  /** GET /api/users/me/events: düzenlediğim ya da katıldığım etkinlikler (geçmişler dahil). */
  myEvents: (role: MyEventsRole) =>
    apiRequest<EventListItem[]>('/api/users/me/events', { query: { role, includePast: true } }),
};

export const notificationsApi = {
  /** GET /api/notifications: bildirim listesi, sayfalı. */
  list: (page = 1) =>
    apiRequest<PagedResult<AppNotification>>('/api/notifications', { query: { page, pageSize: 20 } }),
  /** GET /api/notifications/unread-count: okunmamış bildirim sayısı. */
  unreadCount: () => apiRequest<{ count: number }>('/api/notifications/unread-count'),
  /** POST /api/notifications/{id}/read: tek bildirimi okundu yapar. */
  markRead: (id: string) => apiRequest<void>(`/api/notifications/${seg(id)}/read`, { method: 'POST' }),
  /** POST /api/notifications/read-all: hepsini okundu yapar. */
  markAllRead: () => apiRequest<void>('/api/notifications/read-all', { method: 'POST' }),
};

export type PushPlatform = 'ios' | 'android';

export const devicesApi = {
  /** POST /api/devices: bu cihazın push token'ını kaydeder. */
  register: (token: string, platform: PushPlatform) =>
    apiRequest<void>('/api/devices', { method: 'POST', body: { token, platform } }),
  /** DELETE /api/devices?token=...: bu cihazın push token'ını siler (çıkışta). */
  unregister: (token: string) => apiRequest<void>('/api/devices', { method: 'DELETE', query: { token } }),
};

export const safetyApi = {
  /** POST /api/reports: etkinliği, kullanıcıyı ya da mesajı şikayet eder. */
  report: (targetType: ReportTargetType, targetId: string, reason: ReportReason, details: string | null) =>
    apiRequest<void>('/api/reports', { method: 'POST', body: { targetType, targetId, reason, details } }),
  /** POST /api/users/{id}/block: kullanıcıyı engeller. */
  block: (userId: string) => apiRequest<void>(`/api/users/${seg(userId)}/block`, { method: 'POST' }),
  /** DELETE /api/users/{id}/block: engeli kaldırır. */
  unblock: (userId: string) => apiRequest<void>(`/api/users/${seg(userId)}/block`, { method: 'DELETE' }),
  /** GET /api/users/me/blocks: engellediğim kullanıcılar. */
  blocks: () => apiRequest<BlockedUser[]>('/api/users/me/blocks'),
  /** Hesabı kalıcı olarak siler (şifre tekrar istenir). */
  deleteAccount: (password: string) => apiRequest<void>('/api/users/me/delete', { method: 'POST', body: { password } }),
};

export const chatApi = {
  /** En yeni önce. before: daha eski mesajlar; parametresiz: son mesajlar. */
  list: (eventId: string, params: { before?: string; limit?: number } = {}) =>
    apiRequest<ChatPage>(`/api/events/${seg(eventId)}/messages`, { query: { limit: 30, ...params } }),
  /** POST /api/events/{id}/messages: sohbete mesaj yazar; kaydedilen mesajı döner. */
  send: (eventId: string, body: string) =>
    apiRequest<ChatMessage>(`/api/events/${seg(eventId)}/messages`, { method: 'POST', body: { body } }),
  /** DELETE /api/events/{id}/messages/{messageId}: mesajı siler. */
  remove: (eventId: string, messageId: string) =>
    apiRequest<void>(`/api/events/${seg(eventId)}/messages/${seg(messageId)}`, { method: 'DELETE' }),
  /** POST /api/events/{id}/messages/read: bu etkinliğin sohbet bildirimlerini okundu yapar. */
  markRead: (eventId: string) => apiRequest<void>(`/api/events/${seg(eventId)}/messages/read`, { method: 'POST' }),
};

/** Yüklenecek görsel: cihazdaki dosyanın adresi (seçici/kırpma sonrası JPEG). */
export interface LocalImage {
  uri: string;
}

/** Seçilen görseli multipart form verisine çevirir; sunucu dosyayı "file" alanında bekler. */
function imageForm(image: LocalImage): FormData {
  const form = new FormData();
  // React Native'de dosya, { uri, name, type } nesnesiyle eklenir.
  form.append('file', { uri: image.uri, name: 'photo.jpg', type: 'image/jpeg' } as unknown as Blob);
  return form;
}

export const mediaApi = {
  /** PUT /api/users/me/avatar: profil fotoğrafını yükler; yeni adresi döner. */
  setAvatar: (image: LocalImage) =>
    apiRequest<{ url: string }>('/api/users/me/avatar', { method: 'PUT', body: imageForm(image) }),
  /** DELETE /api/users/me/avatar: profil fotoğrafını kaldırır. */
  deleteAvatar: () => apiRequest<void>('/api/users/me/avatar', { method: 'DELETE' }),
  /** PUT /api/events/{id}/image: etkinliğin kapak görselini yükler; yeni adresi döner. */
  setEventImage: (eventId: string, image: LocalImage) =>
    apiRequest<{ url: string }>(`/api/events/${seg(eventId)}/image`, { method: 'PUT', body: imageForm(image) }),
  /** DELETE /api/events/{id}/image: kapak görselini kaldırır. */
  deleteEventImage: (eventId: string) => apiRequest<void>(`/api/events/${seg(eventId)}/image`, { method: 'DELETE' }),
};

export const profileApi = {
  /** PUT /api/users/me: görünen adı ve tanıtım yazısını günceller. */
  update: (input: { displayName: string; bio: string | null }) =>
    apiRequest<void>('/api/users/me', { method: 'PUT', body: input }),
};
