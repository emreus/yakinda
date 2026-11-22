-- =====================================================================
-- Şifre sıfırlama: e-postaya 6 haneli kod gider. Kodun kendisi değil, kullanıcıya
-- bağlı SHA-256 özeti saklanır; 15 dakika geçerlidir, en fazla 5 yanlış deneme.
-- Yeni kod istenince öncekiler geçersiz olur.
-- =====================================================================

CREATE TABLE password_reset_codes (
    id          uuid PRIMARY KEY,
    user_id     uuid        NOT NULL REFERENCES users (id) ON DELETE CASCADE,
    code_hash   varchar(64) NOT NULL,
    created_at  timestamptz NOT NULL,
    expires_at  timestamptz NOT NULL,
    attempts    smallint    NOT NULL DEFAULT 0,
    used_at     timestamptz
);

CREATE INDEX ix_password_reset_codes_active ON password_reset_codes (user_id, created_at DESC) WHERE used_at IS NULL;
