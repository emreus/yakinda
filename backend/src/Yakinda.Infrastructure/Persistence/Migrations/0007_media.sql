-- Profil fotoğrafı ve etkinlik kapak görseli.
-- Dosyalar diskte (Media:RootPath) durur; burada yalnızca anahtarı tutulur: "avatars/<uuid>.jpg".
-- API bunu "/media/avatars/<uuid>.jpg" adresinden sunar.
ALTER TABLE users  ADD COLUMN avatar_key varchar(200);
ALTER TABLE events ADD COLUMN image_key  varchar(200);
