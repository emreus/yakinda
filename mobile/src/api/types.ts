// Backend DTO'larının TypeScript karşılıkları. Tarihler ISO-8601 (UTC) string olarak gelir.

export type JoinPolicy = 'Open' | 'ApprovalRequired';
export type EventStatus = 'Active' | 'Cancelled' | 'Removed';
export type ParticipantStatus = 'Pending' | 'Approved' | 'Rejected' | 'Left' | 'Waitlisted';

export interface AuthUser {
  id: string;
  email: string;
  displayName: string;
  averageRating: number | null;
  ratingCount: number;
}

export interface AuthResponse {
  accessToken: string;
  accessTokenExpiresAt: string;
  refreshToken: string;
  user: AuthUser;
}

export interface Category {
  id: number;
  slug: string;
  name: string;
  emoji: string;
}

export interface EventListItem {
  id: string;
  title: string;
  categoryId: number;
  categoryName: string;
  categoryEmoji: string;
  startsAt: string;
  endsAt: string | null;
  placeName: string | null;
  latitude: number;
  longitude: number;
  distanceMeters: number | null;
  capacity: number | null;
  participantCount: number;
  spotsLeft: number | null;
  joinPolicy: JoinPolicy;
  status: EventStatus;
  organizerId: string;
  organizerName: string;
  organizerRating: number | null;
  organizerRatingCount: number;
}

export interface EventDetail {
  id: string;
  title: string;
  description: string | null;
  categoryId: number;
  categoryName: string;
  categoryEmoji: string;
  startsAt: string;
  endsAt: string | null;
  placeName: string | null;
  address: string | null;
  latitude: number;
  longitude: number;
  capacity: number | null;
  participantCount: number;
  spotsLeft: number | null;
  joinPolicy: JoinPolicy;
  status: EventStatus;
  averageRating: number | null;
  ratingCount: number;
  organizerId: string;
  organizerName: string;
  organizerRating: number | null;
  organizerRatingCount: number;
  myParticipationStatus: ParticipantStatus | null;
  myScore: number | null;
  myComment: string | null;
  pendingCount: number;
  /** Bekleme listesindeki kişi sayısı */
  waitlistCount: number;
  /** Bekleme listesindeysem sıram (1'den başlar) */
  myWaitlistPosition: number | null;
  isOrganizer: boolean;
  canJoin: boolean;
  canLeave: boolean;
  canCancel: boolean;
  canEdit: boolean;
  canRate: boolean;
  ratingOpensAt: string;
  ratingClosesAt: string;
}

export interface Participant {
  userId: string;
  displayName: string;
  status: ParticipantStatus;
  requestedAt: string;
  averageRating: number | null;
  ratingCount: number;
}

export interface Rating {
  raterId: string;
  raterName: string;
  score: number;
  comment: string | null;
  updatedAt: string;
}

export interface RateResult {
  score: number;
  eventAverageRating: number | null;
  eventRatingCount: number;
}

export interface UserProfile {
  id: string;
  displayName: string;
  bio: string | null;
  createdAt: string;
  averageRating: number | null;
  ratingCount: number;
  organizedEventCount: number;
  joinedEventCount: number;
  /** [1★ adedi, 2★, 3★, 4★, 5★] */
  ratingDistribution: number[];
}

export type NotificationType =
  | 'JoinRequested'
  | 'ParticipantJoined'
  | 'RequestApproved'
  | 'RequestRejected'
  | 'EventCancelled'
  | 'EventReminder'
  | 'RatingPrompt'
  | 'EventUpdated'
  | 'WaitlistPromoted';

/** Uygulama içi bildirim. (Tarayıcıdaki global `Notification` tipiyle karışmasın diye bu ad.) */
export interface AppNotification {
  id: string;
  type: NotificationType;
  /** Dokununca açılacak etkinlik */
  eventId: string | null;
  title: string;
  body: string;
  createdAt: string;
  isRead: boolean;
}

export interface PagedResult<T> {
  items: T[];
  page: number;
  pageSize: number;
  hasMore: boolean;
}

export interface CreateEventInput {
  categoryId: number;
  title: string;
  description?: string | null;
  startsAt: string;
  endsAt?: string | null;
  latitude: number;
  longitude: number;
  placeName?: string | null;
  address?: string | null;
  capacity?: number | null;
  joinPolicy: JoinPolicy;
  /** 0: tek sefer. N: N hafta daha aynı gün ve saatte tekrarlanır */
  repeatWeeks?: number;
}

/** Düzenlemede katılım şekli ve tekrar değiştirilemez */
export type UpdateEventInput = Omit<CreateEventInput, 'joinPolicy' | 'repeatWeeks'>;

/** RFC 7807 ProblemDetails + backend'in eklediği "code" ve "errors" alanları */
export interface ApiProblem {
  title?: string;
  detail?: string;
  status?: number;
  code?: string;
  errors?: Record<string, string[]>;
}

export type ReportTargetType = 'Event' | 'User' | 'Message';
export type ReportReason = 'Spam' | 'Inappropriate' | 'Harassment' | 'Dangerous' | 'Fake' | 'Other';

export interface BlockedUser {
  userId: string;
  displayName: string;
  blockedAt: string;
}
