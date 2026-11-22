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
  login: (email: string, password: string) =>
    apiRequest<AuthResponse>('/api/auth/login', { method: 'POST', body: { email, password }, anonymous: true }),

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

  refresh: (refreshToken: string) =>
    apiRequest<AuthResponse>('/api/auth/refresh', { method: 'POST', body: { refreshToken }, anonymous: true }),

  /** Çıkışta refresh token'ı sunucuda da iptal eder (telefondan silmek tek başına yetmez). */
  logout: (refreshToken: string) =>
    apiRequest<void>('/api/auth/logout', { method: 'POST', body: { refreshToken }, anonymous: true }),
};

export const categoriesApi = {
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
  nearby: (params: NearbyParams) =>
    apiRequest<PagedResult<EventListItem>>('/api/events/nearby', { query: { ...params } }),

  detail: (id: string) => apiRequest<EventDetail>(`/api/events/${seg(id)}`),

  create: (input: CreateEventInput) =>
    apiRequest<{ id: string }>('/api/events', { method: 'POST', body: input }),

  update: (id: string, input: UpdateEventInput) =>
    apiRequest<void>(`/api/events/${seg(id)}`, { method: 'PUT', body: input }),

  cancel: (id: string) => apiRequest<void>(`/api/events/${seg(id)}/cancel`, { method: 'POST' }),

  join: (id: string) => apiRequest<{ status: ParticipantStatus }>(`/api/events/${seg(id)}/join`, { method: 'POST' }),

  leave: (id: string) => apiRequest<void>(`/api/events/${seg(id)}/leave`, { method: 'POST' }),

  participants: (id: string) => apiRequest<Participant[]>(`/api/events/${seg(id)}/participants`),

  approve: (id: string, userId: string) =>
    apiRequest<void>(`/api/events/${seg(id)}/participants/${seg(userId)}/approve`, { method: 'POST' }),

  reject: (id: string, userId: string) =>
    apiRequest<void>(`/api/events/${seg(id)}/participants/${seg(userId)}/reject`, { method: 'POST' }),

  rate: (id: string, score: number, comment: string | null) =>
    apiRequest<RateResult>(`/api/events/${seg(id)}/rating`, { method: 'PUT', body: { score, comment } }),

  ratings: (id: string, page = 1) =>
    apiRequest<PagedResult<Rating>>(`/api/events/${seg(id)}/ratings`, { query: { page, pageSize: 20 } }),
};

export type MyEventsRole = 'organizing' | 'attending';

export const usersApi = {
  me: () => apiRequest<UserProfile>('/api/users/me'),
  get: (id: string) => apiRequest<UserProfile>(`/api/users/${seg(id)}`),
  myEvents: (role: MyEventsRole) =>
    apiRequest<EventListItem[]>('/api/users/me/events', { query: { role, includePast: true } }),
};

export const notificationsApi = {
  list: (page = 1) =>
    apiRequest<PagedResult<AppNotification>>('/api/notifications', { query: { page, pageSize: 20 } }),
  unreadCount: () => apiRequest<{ count: number }>('/api/notifications/unread-count'),
  markRead: (id: string) => apiRequest<void>(`/api/notifications/${seg(id)}/read`, { method: 'POST' }),
  markAllRead: () => apiRequest<void>('/api/notifications/read-all', { method: 'POST' }),
};

export type PushPlatform = 'ios' | 'android';

export const devicesApi = {
  register: (token: string, platform: PushPlatform) =>
    apiRequest<void>('/api/devices', { method: 'POST', body: { token, platform } }),
  unregister: (token: string) => apiRequest<void>('/api/devices', { method: 'DELETE', query: { token } }),
};

export const safetyApi = {
  report: (targetType: ReportTargetType, targetId: string, reason: ReportReason, details: string | null) =>
    apiRequest<void>('/api/reports', { method: 'POST', body: { targetType, targetId, reason, details } }),
  block: (userId: string) => apiRequest<void>(`/api/users/${seg(userId)}/block`, { method: 'POST' }),
  unblock: (userId: string) => apiRequest<void>(`/api/users/${seg(userId)}/block`, { method: 'DELETE' }),
  blocks: () => apiRequest<BlockedUser[]>('/api/users/me/blocks'),
  /** Hesabı kalıcı olarak siler (şifre tekrar istenir). */
  deleteAccount: (password: string) => apiRequest<void>('/api/users/me/delete', { method: 'POST', body: { password } }),
};

export const chatApi = {
  /** En yeni önce. before: daha eski mesajlar; parametresiz: son mesajlar. */
  list: (eventId: string, params: { before?: string; limit?: number } = {}) =>
    apiRequest<ChatPage>(`/api/events/${seg(eventId)}/messages`, { query: { limit: 30, ...params } }),
  send: (eventId: string, body: string) =>
    apiRequest<ChatMessage>(`/api/events/${seg(eventId)}/messages`, { method: 'POST', body: { body } }),
  remove: (eventId: string, messageId: string) =>
    apiRequest<void>(`/api/events/${seg(eventId)}/messages/${seg(messageId)}`, { method: 'DELETE' }),
  markRead: (eventId: string) => apiRequest<void>(`/api/events/${seg(eventId)}/messages/read`, { method: 'POST' }),
};

/** Yüklenecek görsel: cihazdaki dosyanın adresi (seçici/kırpma sonrası JPEG). */
export interface LocalImage {
  uri: string;
}

function imageForm(image: LocalImage): FormData {
  const form = new FormData();
  // React Native'de dosya, { uri, name, type } nesnesiyle eklenir.
  form.append('file', { uri: image.uri, name: 'photo.jpg', type: 'image/jpeg' } as unknown as Blob);
  return form;
}

export const mediaApi = {
  setAvatar: (image: LocalImage) =>
    apiRequest<{ url: string }>('/api/users/me/avatar', { method: 'PUT', body: imageForm(image) }),
  deleteAvatar: () => apiRequest<void>('/api/users/me/avatar', { method: 'DELETE' }),
  setEventImage: (eventId: string, image: LocalImage) =>
    apiRequest<{ url: string }>(`/api/events/${seg(eventId)}/image`, { method: 'PUT', body: imageForm(image) }),
  deleteEventImage: (eventId: string) => apiRequest<void>(`/api/events/${seg(eventId)}/image`, { method: 'DELETE' }),
};

export const profileApi = {
  update: (input: { displayName: string; bio: string | null }) =>
    apiRequest<void>('/api/users/me', { method: 'PUT', body: input }),
};
