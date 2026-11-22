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

/** "Bugün 20:30", "Yarın 14:00", "Cmt 22 Kas 19:00" */
export function formatEventDate(iso: string): string {
  const date = new Date(iso);
  const dayDiff = Math.round((startOfDay(date) - startOfDay(new Date())) / 86_400_000);
  const day =
    dayDiff === 0 ? 'Bugün' : dayDiff === 1 ? 'Yarın' : dayDiff === -1 ? 'Dün' : dayFormat.format(date);
  return `${day} ${timeFormat.format(date)}`;
}

/** Bildirimler için: "az önce", "5 dk önce", "3 sa önce", "Dün 14:05", "Cmt 22 Kas 19:00" */
export function formatRelativeTime(iso: string): string {
  const date = new Date(iso);
  const minutes = Math.floor((Date.now() - date.getTime()) / 60_000);
  if (minutes < 1) return 'az önce';
  if (minutes < 60) return `${minutes} dk önce`;
  if (startOfDay(date) === startOfDay(new Date())) return `${Math.floor(minutes / 60)} sa önce`;
  return formatEventDate(iso);
}

/** Uzun tarih ve saat: gün adı, gün, ay ve saat (ör. "Cumartesi 22 Kasım 19:00"). */
export function formatDateTimeLong(value: string | Date): string {
  return fullFormat.format(typeof value === 'string' ? new Date(value) : value);
}

/** Yalnızca saat ve dakika (ör. "19:00"). */
export function formatTime(iso: string): string {
  return timeFormat.format(new Date(iso));
}

/** "850 m", "3,2 km" */
export function formatDistance(meters: number | null | undefined): string {
  if (meters == null) return '';
  if (meters < 1000) return `${Math.max(10, Math.round(meters / 10) * 10)} m`;
  return `${(meters / 1000).toLocaleString(LOCALE, { maximumFractionDigits: 1 })} km`;
}

/** "4,6" */
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
