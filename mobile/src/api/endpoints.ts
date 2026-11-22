import { apiRequest } from './client';
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

  detail: (id: string) => apiRequest<EventDetail>(`/api/events/${id}`),

  create: (input: CreateEventInput) =>
    apiRequest<{ id: string }>('/api/events', { method: 'POST', body: input }),

  update: (id: string, input: UpdateEventInput) =>
    apiRequest<void>(`/api/events/${id}`, { method: 'PUT', body: input }),

  cancel: (id: string) => apiRequest<void>(`/api/events/${id}/cancel`, { method: 'POST' }),

  join: (id: string) => apiRequest<{ status: ParticipantStatus }>(`/api/events/${id}/join`, { method: 'POST' }),

  leave: (id: string) => apiRequest<void>(`/api/events/${id}/leave`, { method: 'POST' }),

  participants: (id: string) => apiRequest<Participant[]>(`/api/events/${id}/participants`),

  approve: (id: string, userId: string) =>
    apiRequest<void>(`/api/events/${id}/participants/${userId}/approve`, { method: 'POST' }),

  reject: (id: string, userId: string) =>
    apiRequest<void>(`/api/events/${id}/participants/${userId}/reject`, { method: 'POST' }),

  rate: (id: string, score: number, comment: string | null) =>
    apiRequest<RateResult>(`/api/events/${id}/rating`, { method: 'PUT', body: { score, comment } }),

  ratings: (id: string, page = 1) =>
    apiRequest<PagedResult<Rating>>(`/api/events/${id}/ratings`, { query: { page, pageSize: 20 } }),
};

export type MyEventsRole = 'organizing' | 'attending';

export const usersApi = {
  me: () => apiRequest<UserProfile>('/api/users/me'),
  get: (id: string) => apiRequest<UserProfile>(`/api/users/${id}`),
  myEvents: (role: MyEventsRole) =>
    apiRequest<EventListItem[]>('/api/users/me/events', { query: { role, includePast: true } }),
};

export const notificationsApi = {
  list: (page = 1) =>
    apiRequest<PagedResult<AppNotification>>('/api/notifications', { query: { page, pageSize: 20 } }),
  unreadCount: () => apiRequest<{ count: number }>('/api/notifications/unread-count'),
  markRead: (id: string) => apiRequest<void>(`/api/notifications/${id}/read`, { method: 'POST' }),
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
  block: (userId: string) => apiRequest<void>(`/api/users/${userId}/block`, { method: 'POST' }),
  unblock: (userId: string) => apiRequest<void>(`/api/users/${userId}/block`, { method: 'DELETE' }),
  blocks: () => apiRequest<BlockedUser[]>('/api/users/me/blocks'),
  /** Hesabı kalıcı olarak siler (şifre tekrar istenir). */
  deleteAccount: (password: string) => apiRequest<void>('/api/users/me/delete', { method: 'POST', body: { password } }),
};

export const chatApi = {
  /** En yeni önce. before: daha eski mesajlar; parametresiz: son mesajlar. */
  list: (eventId: string, params: { before?: string; limit?: number } = {}) =>
    apiRequest<ChatPage>(`/api/events/${eventId}/messages`, { query: { limit: 30, ...params } }),
  send: (eventId: string, body: string) =>
    apiRequest<ChatMessage>(`/api/events/${eventId}/messages`, { method: 'POST', body: { body } }),
  remove: (eventId: string, messageId: string) =>
    apiRequest<void>(`/api/events/${eventId}/messages/${messageId}`, { method: 'DELETE' }),
  markRead: (eventId: string) => apiRequest<void>(`/api/events/${eventId}/messages/read`, { method: 'POST' }),
};
