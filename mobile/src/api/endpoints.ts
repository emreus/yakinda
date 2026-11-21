import { apiRequest } from './client';
import type {
  AuthResponse,
  Category,
  CreateEventInput,
  EventDetail,
  EventListItem,
  PagedResult,
  Participant,
  ParticipantStatus,
  RateResult,
  Rating,
  UserProfile,
} from './types';

export const authApi = {
  login: (email: string, password: string) =>
    apiRequest<AuthResponse>('/api/auth/login', { method: 'POST', body: { email, password }, anonymous: true }),

  register: (email: string, password: string, displayName: string) =>
    apiRequest<AuthResponse>('/api/auth/register', {
      method: 'POST',
      body: { email, password, displayName },
      anonymous: true,
    }),

  refresh: (refreshToken: string) =>
    apiRequest<AuthResponse>('/api/auth/refresh', { method: 'POST', body: { refreshToken }, anonymous: true }),
};

export const categoriesApi = {
  list: () => apiRequest<Category[]>('/api/categories', { anonymous: true }),
};

export type NearbySort = 'distance' | 'time';

export type NearbyParams = {
  lat: number;
  lng: number;
  radiusKm: number;
  categoryId?: number | null;
  onlyAvailable?: boolean;
  sort?: NearbySort;
  page?: number;
  pageSize?: number;
};

export const eventsApi = {
  nearby: (params: NearbyParams) =>
    apiRequest<PagedResult<EventListItem>>('/api/events/nearby', { query: { ...params } }),

  detail: (id: string) => apiRequest<EventDetail>(`/api/events/${id}`),

  create: (input: CreateEventInput) =>
    apiRequest<{ id: string }>('/api/events', { method: 'POST', body: input }),

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
