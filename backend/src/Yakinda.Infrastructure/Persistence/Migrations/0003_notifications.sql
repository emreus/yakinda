-- =====================================================================
-- Bildirimler
--  * notifications: uygulama içi bildirim listesi. Aynı zamanda push için
--    "outbox": bildirim, ona yol açan işlemle (katılım, onay, iptal) aynı
--    transaction'da yazılır; PushDispatcher commit'ten sonra bunları okuyup
--    Expo Push servisine gönderir. Böylece işlem geri alınırsa bildirim de gitmez,
--    API kapanıp açılsa da bekleyen push'lar kaybolmaz.
--  * device_push_tokens: kullanıcının telefonlarındaki Expo push token'ları.
--  * events.reminder_sent_at / rating_prompt_sent_at: zamanlanmış bildirimlerin
--    (başlamadan önce hatırlatma, bitince "puanla") bir kez gönderildiğini işaretler.
-- =====================================================================

CREATE TABLE notifications (
    id                 uuid PRIMARY KEY,
    user_id            uuid         NOT NULL REFERENCES users (id) ON DELETE CASCADE,
    type               smallint     NOT NULL,
    event_id           uuid         REFERENCES events (id) ON DELETE CASCADE,
    actor_id           uuid         REFERENCES users (id) ON DELETE SET NULL,
    title              varchar(200) NOT NULL,
    body               varchar(500) NOT NULL,
    created_at         timestamptz  NOT NULL DEFAULT now(),
    read_at            timestamptz,
    -- Push outbox: NULL = henüz işlenmedi. Gönderildi, gönderilecek cihaz yok ya da
    -- denemeler tükendi durumlarında doldurulur.
    push_processed_at  timestamptz,
    push_attempts      smallint     NOT NULL DEFAULT 0
);

-- Bildirim listesi (en yeni önce) ve okunmamış sayısı
CREATE INDEX ix_notifications_user          ON notifications (user_id, created_at DESC);
CREATE INDEX ix_notifications_unread        ON notifications (user_id) WHERE read_at IS NULL;
-- PushDispatcher'ın bekleyenleri bulması
CREATE INDEX ix_notifications_push_pending  ON notifications (created_at) WHERE push_processed_at IS NULL;

CREATE TABLE device_push_tokens (
    token         varchar(255) PRIMARY KEY,
    user_id       uuid         NOT NULL REFERENCES users (id) ON DELETE CASCADE,
    platform      varchar(10)  NOT NULL CHECK (platform IN ('ios', 'android')),
    created_at    timestamptz  NOT NULL DEFAULT now(),
    last_seen_at  timestamptz  NOT NULL DEFAULT now()
);

CREATE INDEX ix_device_push_tokens_user ON device_push_tokens (user_id);

ALTER TABLE events
    ADD COLUMN reminder_sent_at       timestamptz,
    ADD COLUMN rating_prompt_sent_at  timestamptz;

-- ScheduledNotificationService her dakika "puanlama bildirimi gitmemiş, bitmiş etkinlikleri" arar.
-- (Hatırlatma araması mevcut ix_events_active_starts indeksini kullanır.)
CREATE INDEX ix_events_rating_prompt_pending ON events (starts_at)
    WHERE status = 0 AND rating_prompt_sent_at IS NULL;
