-- =====================================================================
-- 0001 - İlk şema
-- Konum sorguları için PostGIS "geography" tipi kullanılır (metre cinsinden
-- mesafe hesabı + GiST index ile hızlı "yakınımdakiler" sorgusu).
-- =====================================================================

CREATE EXTENSION IF NOT EXISTS postgis;

-- ---------------------------------------------------------------------
-- Kullanıcılar
-- rating_sum / rating_count: kullanıcının ORGANİZE ETTİĞİ etkinliklere
-- verilen tüm puanların toplamı ve adedi. Ortalama = sum / count.
-- Her puan verildiğinde/değiştirildiğinde aynı transaction içinde güncellenir.
-- ---------------------------------------------------------------------
CREATE TABLE users (
    id              uuid PRIMARY KEY,
    email           varchar(256) NOT NULL,          -- uygulama tarafından küçük harfe çevrilir
    password_hash   text         NOT NULL,
    display_name    varchar(50)  NOT NULL,
    bio             varchar(500),
    rating_sum      bigint       NOT NULL DEFAULT 0,
    rating_count    integer      NOT NULL DEFAULT 0,
    created_at      timestamptz  NOT NULL DEFAULT now()
);

CREATE UNIQUE INDEX ux_users_email ON users (email);

-- ---------------------------------------------------------------------
-- Kategoriler (okey, parti, sinema ...)
-- ---------------------------------------------------------------------
CREATE TABLE categories (
    id          smallint PRIMARY KEY,
    slug        varchar(40) NOT NULL UNIQUE,
    name        varchar(60) NOT NULL,
    emoji       varchar(16) NOT NULL,
    sort_order  smallint    NOT NULL DEFAULT 0
);

-- ---------------------------------------------------------------------
-- Etkinlikler
-- capacity          : uygulama üzerinden katılabilecek kişi sayısı
--                     (organizatör hariç). NULL = sınırsız.
--                     Örn. "okeye 4. aranıyor" => capacity = 1
-- join_policy       : 0 = Open (anında katılım), 1 = ApprovalRequired
-- status            : 0 = Active, 1 = Cancelled
-- participant_count : onaylı katılımcı sayısı (denormalize)
-- rating_sum/count  : bu etkinliğe verilen puanlar (denormalize)
-- ---------------------------------------------------------------------
CREATE TABLE events (
    id                 uuid PRIMARY KEY,
    organizer_id       uuid          NOT NULL REFERENCES users (id),
    category_id        smallint      NOT NULL REFERENCES categories (id),
    title              varchar(120)  NOT NULL,
    description        varchar(2000),
    starts_at          timestamptz   NOT NULL,
    ends_at            timestamptz,
    location           geography(Point, 4326) NOT NULL,
    place_name         varchar(150),
    address            varchar(300),
    capacity           integer       CHECK (capacity IS NULL OR capacity > 0),
    join_policy        smallint      NOT NULL DEFAULT 0,
    status             smallint      NOT NULL DEFAULT 0,
    participant_count  integer       NOT NULL DEFAULT 0 CHECK (participant_count >= 0),
    rating_sum         bigint        NOT NULL DEFAULT 0,
    rating_count       integer       NOT NULL DEFAULT 0,
    created_at         timestamptz   NOT NULL DEFAULT now(),
    updated_at         timestamptz   NOT NULL DEFAULT now(),
    CONSTRAINT ck_events_time CHECK (ends_at IS NULL OR ends_at > starts_at),
    CONSTRAINT ck_events_capacity CHECK (capacity IS NULL OR participant_count <= capacity)
);

CREATE INDEX ix_events_location        ON events USING gist (location);
CREATE INDEX ix_events_active_starts   ON events (starts_at) WHERE status = 0;
CREATE INDEX ix_events_organizer       ON events (organizer_id, starts_at DESC);

-- ---------------------------------------------------------------------
-- Katılımcılar
-- status: 0 = Pending, 1 = Approved, 2 = Rejected, 3 = Left
-- ---------------------------------------------------------------------
CREATE TABLE event_participants (
    event_id    uuid        NOT NULL REFERENCES events (id) ON DELETE CASCADE,
    user_id     uuid        NOT NULL REFERENCES users (id),
    status      smallint    NOT NULL,
    created_at  timestamptz NOT NULL DEFAULT now(),
    updated_at  timestamptz NOT NULL DEFAULT now(),
    PRIMARY KEY (event_id, user_id)
);

CREATE INDEX ix_participants_user ON event_participants (user_id, status);

-- ---------------------------------------------------------------------
-- Puanlar (1-5). Bir kullanıcı bir etkinliğe tek puan verir, sonradan
-- (oylama penceresi açıkken) güncelleyebilir.
-- ---------------------------------------------------------------------
CREATE TABLE event_ratings (
    event_id    uuid        NOT NULL REFERENCES events (id) ON DELETE CASCADE,
    rater_id    uuid        NOT NULL REFERENCES users (id),
    score       smallint    NOT NULL CHECK (score BETWEEN 1 AND 5),
    comment     varchar(500),
    created_at  timestamptz NOT NULL DEFAULT now(),
    updated_at  timestamptz NOT NULL DEFAULT now(),
    PRIMARY KEY (event_id, rater_id)
);

-- ---------------------------------------------------------------------
-- Refresh token'lar (yalnızca SHA-256 hash'i saklanır)
-- ---------------------------------------------------------------------
CREATE TABLE refresh_tokens (
    id          uuid PRIMARY KEY,
    user_id     uuid         NOT NULL REFERENCES users (id) ON DELETE CASCADE,
    token_hash  varchar(128) NOT NULL UNIQUE,
    expires_at  timestamptz  NOT NULL,
    created_at  timestamptz  NOT NULL DEFAULT now(),
    revoked_at  timestamptz
);

CREATE INDEX ix_refresh_tokens_user ON refresh_tokens (user_id);
