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

  /**
   * Sunucunun döndüğü ProblemDetails'ten hata nesnesi kurar. Mesaj olarak sırasıyla detail, title ya da
   * duruma göre varsayılan metin kullanılır; code ve alan hataları (fieldErrors) ayrıca saklanır.
   */
  constructor(status: number, problem: ApiProblem | null) {
    super(problem?.detail ?? problem?.title ?? defaultMessage(status));
    this.name = 'ApiError';
    this.status = status;
    this.code = problem?.code;
    this.fieldErrors = problem?.errors;
  }
}

/**
 * Sunucu mesaj döndürmediğinde gösterilecek Türkçe metin: 0 = bağlantı kurulamadı, 500 ve üzeri = sunucu hatası.
 */
function defaultMessage(status: number): string {
  if (status === 0) return 'Sunucuya ulaşılamadı. İnternet bağlantını kontrol et.';
  if (status >= 500) return 'Sunucuda bir sorun oluştu, lütfen tekrar dene.';
  return 'Bir hata oluştu.';
}

/**
 * Kullanıcıya gösterilecek hata metni: ApiError ya da başka bir Error ise kendi mesajı, tanınmayan
 * bir değerse genel "Bir hata oluştu." metni.
 */
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

/**
 * AuthContext'in kendini HTTP katmanına tanıtması: apiRequest token'ı ve yenileme fonksiyonunu buradan alır.
 */
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

/**
 * Nesneden "?a=1&b=2" biçiminde sorgu metni üretir; boş, null ve undefined değerleri atlar, kalanları kodlar.
 */
function buildQuery(query?: Record<string, QueryValue>): string {
  if (!query) return '';
  const parts = Object.entries(query)
    .filter(([, value]) => value !== undefined && value !== null && value !== '')
    .map(([key, value]) => `${encodeURIComponent(key)}=${encodeURIComponent(String(value))}`);
  return parts.length > 0 ? `?${parts.join('&')}` : '';
}

/**
 * Her API isteğinin geçtiği tek nokta. Authorization başlığını ekler (anonymous değilse), gövdeyi JSON
 * olarak ya da FormData ise olduğu gibi gönderir. Ağ hatasında ApiError(0) fırlatır. 401 gelirse bir kez
 * token yeniler ve isteği tekrarlar. Başarısız yanıtta gövdedeki ProblemDetails ile ApiError fırlatır;
 * 204'te undefined, diğer durumda çözülmüş JSON döner.
 */
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
