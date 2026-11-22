-- =====================================================================
-- Güvenlik ve mağaza zorunlulukları
--  * users: kullanım koşulları onayı, yönetici, yasaklama, hesap silme.
--    Hesap silinince satır silinmez, kişisel veriler temizlenip anonimleştirilir:
--    geçmiş etkinliklerin ve verilen oyların sayıları (organizatör ortalamaları) bozulmasın.
--  * user_blocks: kullanıcı engelleme (iki yönlü etki: birbirinin etkinliğini görmez, katılamaz).
--  * reports: şikayetler; yönetici inceleyip içeriği kaldırır ya da kullanıcıyı yasaklar.
-- =====================================================================

ALTER TABLE users
    ADD COLUMN terms_accepted_at timestamptz,
    ADD COLUMN is_admin          boolean     NOT NULL DEFAULT false,
    ADD COLUMN banned_at         timestamptz,
    ADD COLUMN deleted_at        timestamptz;

CREATE TABLE user_blocks (
    blocker_id  uuid        NOT NULL REFERENCES users (id) ON DELETE CASCADE,
    blocked_id  uuid        NOT NULL REFERENCES users (id) ON DELETE CASCADE,
    created_at  timestamptz NOT NULL DEFAULT now(),
    PRIMARY KEY (blocker_id, blocked_id),
    CONSTRAINT ck_user_blocks_self CHECK (blocker_id <> blocked_id)
);

-- "Beni engelleyenler" araması için (PK zaten blocker_id ile başlıyor)
CREATE INDEX ix_user_blocks_blocked ON user_blocks (blocked_id);

CREATE TABLE reports (
    id               uuid PRIMARY KEY,
    reporter_id      uuid         NOT NULL REFERENCES users (id) ON DELETE CASCADE,
    target_type      smallint     NOT NULL,          -- 1 etkinlik, 2 kullanıcı, 3 mesaj
    target_id        uuid         NOT NULL,
    reason           smallint     NOT NULL,
    details          varchar(500),
    status           smallint     NOT NULL DEFAULT 0, -- 0 açık, 1 işlem yapıldı, 2 reddedildi
    created_at       timestamptz  NOT NULL DEFAULT now(),
    resolved_at      timestamptz,
    resolved_by      uuid         REFERENCES users (id) ON DELETE SET NULL,
    resolution_note  varchar(500)
);

-- Aynı kişi aynı içeriği tekrar tekrar şikayet edemesin (açık şikayet varken)
CREATE UNIQUE INDEX ux_reports_open_per_reporter ON reports (reporter_id, target_type, target_id) WHERE status = 0;
-- Yönetici kuyruğu
CREATE INDEX ix_reports_open ON reports (created_at) WHERE status = 0;
