-- =====================================================================
-- Güvenlik sıkılaştırmaları
-- =====================================================================

-- Oturum sürümü. Şifre sıfırlandığında, kullanıcı yasaklandığında ya da hesap silindiğinde artırılır.
-- Erişim token'ı verildiği andaki sürümü taşır; her istekte karşılaştırılır. Böylece bu olaylardan
-- önce alınmış token'lar (normalde 30 dakika geçerli) anında geçersiz olur.
ALTER TABLE users ADD COLUMN token_version integer NOT NULL DEFAULT 0;

-- Hesap başına günlük şifre sıfırlama kodu sınırı için (kullanılmış kodlar dahil sayılır).
CREATE INDEX ix_password_reset_codes_user ON password_reset_codes (user_id, created_at DESC);

-- Aynı kişinin aynı etkinlik için kısa sürede tekrar bildirim üretmesini önleyen kontrol için.
CREATE INDEX ix_notifications_dedupe ON notifications (user_id, event_id, actor_id, type, created_at DESC);
