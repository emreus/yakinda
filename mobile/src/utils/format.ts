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

export function formatDateTimeLong(value: string | Date): string {
  return fullFormat.format(typeof value === 'string' ? new Date(value) : value);
}

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
