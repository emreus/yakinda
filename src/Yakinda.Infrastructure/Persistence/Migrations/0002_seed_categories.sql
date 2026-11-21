-- =====================================================================
-- 0002 - Başlangıç kategorileri
-- =====================================================================

INSERT INTO categories (id, slug, name, emoji, sort_order) VALUES
    (1,  'okey-kart',     'Okey & Kart',        '🃏', 10),
    (2,  'parti',         'Parti',              '🎉', 20),
    (3,  'sinema',        'Sinema',             '🎬', 30),
    (4,  'spor',          'Spor',               '⚽', 40),
    (5,  'yemek',         'Yemek & İçecek',     '🍽️', 50),
    (6,  'kahve-sohbet',  'Kahve & Sohbet',     '☕', 60),
    (7,  'konser-muzik',  'Konser & Müzik',     '🎵', 70),
    (8,  'oyun',          'Masa & Video Oyunu', '🎲', 80),
    (9,  'doga-gezi',     'Doğa & Gezi',        '🥾', 90),
    (10, 'diger',         'Diğer',              '✨', 100)
ON CONFLICT (id) DO NOTHING;
