-- =====================================================================
-- Etkinlik sohbeti: organizatör ve onaylı katılımcılar arasında.
-- id'ler UUIDv7 (zaman sıralı) olduğu için sayfalama doğrudan id üzerinden yapılır.
-- Silinen mesajlar (yazarı, organizatör ya da yönetici) yumuşak silinir: "Mesaj silindi" görünür.
-- =====================================================================

CREATE TABLE event_messages (
    id          uuid PRIMARY KEY,
    event_id    uuid          NOT NULL REFERENCES events (id) ON DELETE CASCADE,
    author_id   uuid          NOT NULL REFERENCES users (id),
    body        varchar(1000) NOT NULL,
    created_at  timestamptz   NOT NULL,
    deleted_at  timestamptz
);

CREATE INDEX ix_event_messages_event ON event_messages (event_id, id DESC);
