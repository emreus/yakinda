import type { EventListItem } from '@/api/types';

const LOCALE = 'tr-TR';
const timeFormat = new Intl.DateTimeFormat(LOCALE, { hour: '2-digit', minute: '2-digit' });
const dayFormat = new Intl.DateTimeFormat(LOCALE, { weekday: 'short', day: 'numeric', month: 'short' });
const fullFormat = new Intl.DateTimeFormat(LOCALE, {
  weekday: 'long',
  day: 'numeric',
  month: 'long',
  hour: '2-digit',
  minute: '2-digit',
});

/** Tarihin, o günün gece yarısına denk gelen zaman damgasını döner (gün karşılaştırmaları için). */
function startOfDay(date: Date): number {
  return new Date(date.getFullYear(), date.getMonth(), date.getDate()).getTime();
}

/** "Bugün 20:30", "Yarın 14:00", "22 Kas Cmt 19:00" */
export function formatEventDate(iso: string): string {
  const date = new Date(iso);
  const dayDiff = Math.round((startOfDay(date) - startOfDay(new Date())) / 86_400_000);
  const day =
    dayDiff === 0 ? 'Bugün' : dayDiff === 1 ? 'Yarın' : dayDiff === -1 ? 'Dün' : dayFormat.format(date);
  return `${day} ${timeFormat.format(date)}`;
}

/** Bildirimler için: "az önce", "5 dk önce", "3 sa önce", "Dün 14:05", "22 Kas Cmt 19:00" */
export function formatRelativeTime(iso: string): string {
  const date = new Date(iso);
  const minutes = Math.floor((Date.now() - date.getTime()) / 60_000);
  if (minutes < 1) return 'az önce';
  if (minutes < 60) return `${minutes} dk önce`;
  if (startOfDay(date) === startOfDay(new Date())) return `${Math.floor(minutes / 60)} sa önce`;
  return formatEventDate(iso);
}

/** Uzun tarih ve saat: gün, ay, gün adı ve saat (ör. "22 Kasım Cumartesi 19:00"). */
export function formatDateTimeLong(value: string | Date): string {
  return fullFormat.format(typeof value === 'string' ? new Date(value) : value);
}

/** Yalnızca saat ve dakika (ör. "19:00"). */
export function formatTime(iso: string): string {
  return timeFormat.format(new Date(iso));
}

const HOUR_MS = 60 * 60 * 1000;

/**
 * Etkinliğin başlangıcı ve (varsa) bitişi. Bitiş aynı gündeyse ya da en çok 12 saat sonraysa yalnızca
 * saati yazılır ("22 Kasım Cumartesi 19:00 – 21:00"); daha uzun etkinlikte bitişin günü de yazılır ki
 * birkaç gün süren etkinlik aynı gün bitiyor gibi görünmesin.
 */
export function formatEventRange(startsAt: string, endsAt: string | null | undefined): string {
  const start = new Date(startsAt);
  if (!endsAt) return formatDateTimeLong(start);
  const end = new Date(endsAt);
  const short = startOfDay(start) === startOfDay(end) || end.getTime() - start.getTime() <= 12 * HOUR_MS;
  return `${formatDateTimeLong(start)} – ${short ? timeFormat.format(end) : formatDateTimeLong(end)}`;
}

/** Etkinliğin bitmiş sayıldığı an: bitiş saati, girilmemişse başlangıç + 3 saat (backend'deki kuralla aynı). */
export function effectiveEndsAt(event: Pick<EventListItem, 'startsAt' | 'endsAt'>): number {
  return event.endsAt ? new Date(event.endsAt).getTime() : new Date(event.startsAt).getTime() + 3 * HOUR_MS;
}

/**
 * Mesafeyi yazıya çevirir: 1 km'den azsa 10 metreye yuvarlanmış metre ("850 m"), değilse tek ondalıklı
 * kilometre ("3,2 km"); değer yoksa boş metin döner.
 */
export function formatDistance(meters: number | null | undefined): string {
  if (meters == null) return '';
  if (meters < 1000) return `${Math.max(10, Math.round(meters / 10) * 10)} m`;
  return `${(meters / 1000).toLocaleString(LOCALE, { maximumFractionDigits: 1 })} km`;
}

/** Puanı tek ondalıkla yazar ("4,6"); değer yoksa "–" döner. */
export function formatRating(value: number | null | undefined): string {
  if (value == null) return '–';
  return value.toLocaleString(LOCALE, { minimumFractionDigits: 1, maximumFractionDigits: 1 });
}

/** Kontenjan durumu: "1 kişi aranıyor", "Kontenjan dolu", "12 katılımcı" */
export function formatSpots(event: Pick<EventListItem, 'capacity' | 'participantCount' | 'spotsLeft'>): {
  label: string;
  tone: 'open' | 'full' | 'neutral';
} {
  if (event.capacity == null) {
    return { label: event.participantCount > 0 ? `${event.participantCount} katılımcı` : 'Sınırsız katılım', tone: 'neutral' };
  }
  const left = event.spotsLeft ?? Math.max(0, event.capacity - event.participantCount);
  if (left <= 0) return { label: 'Kontenjan dolu', tone: 'full' };
  return { label: `${left} kişi aranıyor`, tone: 'open' };
}

/**
 * Kartta ve detayda gösterilen durum rozeti. Öncelik sırası: iptal edildi, kaldırıldı, bitti, başladı;
 * hiçbiri değilse kontenjan durumu (formatSpots). Başlamış etkinliğe katılım alınmadığı için "kişi
 * aranıyor" yazılmaz.
 */
export function formatEventBadge(
  event: Pick<EventListItem, 'status' | 'startsAt' | 'endsAt' | 'capacity' | 'participantCount' | 'spotsLeft'>,
  now: number = Date.now(),
): { label: string; tone: 'success' | 'danger' | 'neutral' } {
  if (event.status === 'Cancelled') return { label: 'İptal edildi', tone: 'danger' };
  if (event.status === 'Removed') return { label: 'Kaldırıldı', tone: 'danger' };
  if (effectiveEndsAt(event) < now) return { label: 'Bitti', tone: 'neutral' };
  if (new Date(event.startsAt).getTime() <= now) return { label: 'Başladı', tone: 'neutral' };

  const spots = formatSpots(event);
  return { label: spots.label, tone: spots.tone === 'open' ? 'success' : spots.tone === 'full' ? 'danger' : 'neutral' };
}
