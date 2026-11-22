import Constants from 'expo-constants';
import type { ApiProblem } from './types';

const API_PORT = 5080;

/**
 * API adresi şu sırayla belirlenir:
 * 1. EXPO_PUBLIC_API_URL tanımlıysa o (canlı ortam ya da özel durumlar için).
 * 2. Geliştirmede Expo'nun uygulamayı yüklediği bilgisayarın adresi (hostUri, ör. "192.168.1.34:8081").
 *    Backend aynı bilgisayarda 5080'de çalıştığı için iOS simülatör, Android emülatör ve aynı
 *    Wi-Fi'daki telefon; .env, 10.0.2.2 ya da adb reverse gerekmeden backend'e ulaşır.
 * 3. İkisi de yoksa localhost.
 */
function resolveApiUrl(): string {
  const fromEnv = process.env.EXPO_PUBLIC_API_URL?.trim();
  if (fromEnv) return fromEnv;

  const devHost = Constants.expoConfig?.hostUri?.split(':')[0];
  if (devHost) return `http://${devHost}:${API_PORT}`;

  return `http://localhost:${API_PORT}`;
}

export const API_URL = resolveApiUrl().replace(/\/$/, '');

/** API'nin döndüğü görsel adresi ("/media/...") tam adrese çevrilir; zaten tam adresse olduğu gibi kalır. */
export function mediaUrl(path: string | null | undefined): string | null {
  if (!path) return null;
  return /^https?:\/\//.test(path) ? path : `${API_URL}${path}`;
}

export class ApiError extends Error {
  readonly status: number;
  readonly code?: string;
  readonly fieldErrors?: Record<string, string[]>;

  constructor(status: number, problem: ApiProblem | null) {
    super(problem?.detail ?? problem?.title ?? defaultMessage(status));
    this.name = 'ApiError';
    this.status = status;
    this.code = problem?.code;
    this.fieldErrors = problem?.errors;
  }
}

function defaultMessage(status: number): string {
  if (status === 0) return 'Sunucuya ulaşılamadı. İnternet bağlantını kontrol et.';
  if (status >= 500) return 'Sunucuda bir sorun oluştu, lütfen tekrar dene.';
  return 'Bir hata oluştu.';
}

/** Kullanıcıya gösterilecek hata metni. */
export function errorMessage(error: unknown): string {
  if (error instanceof ApiError) return error.message;
  if (error instanceof Error) return error.message;
  return 'Bir hata oluştu.';
}

/**
 * AuthContext ile HTTP katmanı arasındaki köprü. Access token bellekte tutulur;
 * 401 alınırsa bir kez refresh denenir ve istek tekrarlanır.
 */
export interface AuthBridge {
  getAccessToken(): string | null;
  refreshAccessToken(): Promise<boolean>;
}

let authBridge: AuthBridge | null = null;

export function setAuthBridge(bridge: AuthBridge | null): void {
  authBridge = bridge;
}

type QueryValue = string | number | boolean | null | undefined;

export interface RequestOptions {
  method?: 'GET' | 'POST' | 'PUT' | 'DELETE';
  /** JSON olarak gönderilir; FormData ise (dosya yükleme) olduğu gibi multipart gider. */
  body?: unknown;
  query?: Record<string, QueryValue>;
  /** true ise Authorization başlığı eklenmez ve 401'de refresh denenmez. */
  anonymous?: boolean;
}

function buildQuery(query?: Record<string, QueryValue>): string {
  if (!query) return '';
  const parts = Object.entries(query)
    .filter(([, value]) => value !== undefined && value !== null && value !== '')
    .map(([key, value]) => `${encodeURIComponent(key)}=${encodeURIComponent(String(value))}`);
  return parts.length > 0 ? `?${parts.join('&')}` : '';
}

export async function apiRequest<T>(path: string, options: RequestOptions = {}, isRetry = false): Promise<T> {
  const headers: Record<string, string> = { Accept: 'application/json' };
  const isForm = typeof FormData !== 'undefined' && options.body instanceof FormData;
  // FormData'da Content-Type'ı (boundary ile) fetch kendisi koyar.
  if (options.body !== undefined && !isForm) headers['Content-Type'] = 'application/json';

  const token = options.anonymous ? null : authBridge?.getAccessToken();
  if (token) headers.Authorization = `Bearer ${token}`;

  let response: Response;
  try {
    response = await fetch(`${API_URL}${path}${buildQuery(options.query)}`, {
      method: options.method ?? 'GET',
      headers,
      body: options.body === undefined ? undefined : isForm ? (options.body as FormData) : JSON.stringify(options.body),
    });
  } catch {
    throw new ApiError(0, null);
  }

  if (response.status === 401 && !options.anonymous && !isRetry && authBridge) {
    const refreshed = await authBridge.refreshAccessToken();
    if (refreshed) return apiRequest<T>(path, options, true);
  }

  if (!response.ok) {
    let problem: ApiProblem | null = null;
    try {
      problem = (await response.json()) as ApiProblem;
    } catch {
      // gövde boş ya da JSON değil
    }
    throw new ApiError(response.status, problem);
  }

  if (response.status === 204) return undefined as T;
  const text = await response.text();
  return (text ? JSON.parse(text) : undefined) as T;
}
