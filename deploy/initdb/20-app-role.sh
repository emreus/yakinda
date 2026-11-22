#!/bin/bash
# Veritabanı ilk kez oluşturulurken (veri klasörü boşken) bir kez çalışır.
#
# Uygulama veritabanına süper kullanıcıyla (postgres) değil, yalnız kendi tablolarının sahibi olan "yakinda"
# rolüyle bağlanır. Süper kullanıcı sunucuda komut çalıştırabilir ve dosya okuyabilir; uygulamanın buna ihtiyacı
# yok. Böylece kodda bir gün SQL açığı çıksa bile etkisi uygulamanın kendi verisiyle sınırlı kalır.
# PostGIS eklentisini imajın kendi kurulum betiği (10_postgis.sh) süper kullanıcıyla zaten kurmuştur.
set -e

if [ -z "$APP_DB_PASSWORD" ]; then
  echo "APP_DB_PASSWORD tanımlı değil (deploy/.env)" >&2
  exit 1
fi

psql -v ON_ERROR_STOP=1 --username "$POSTGRES_USER" --dbname "$POSTGRES_DB" <<EOSQL
CREATE ROLE yakinda LOGIN PASSWORD '${APP_DB_PASSWORD}';
GRANT CONNECT, TEMPORARY ON DATABASE "${POSTGRES_DB}" TO yakinda;
-- Tabloları uygulama oluşturur (migration'lar açılışta çalışır): public şemasının sahibi olması yeterli.
ALTER SCHEMA public OWNER TO yakinda;
EOSQL
