#!/usr/bin/env sh
# Veritabanını ve yüklenen görselleri yedekler: deploy/backups/ altına yazar, 14 günden eskileri siler.
# Elle:  ./backup.sh
# Her gece 03:30 (crontab -e):  30 3 * * * /home/<kullanıcı>/yakinda/deploy/backup.sh >> /home/<kullanıcı>/yakinda-backup.log 2>&1
# Yedekleri sunucu dışına da kopyala (ör. rclone ile bir bulut depolamaya); sunucu giderse yedek de gitmesin.
set -eu
cd "$(dirname "$0")"
compose="docker compose -f docker-compose.prod.yml"
stamp=$(date +%Y%m%d-%H%M)
mkdir -p backups
chmod 700 backups # yedekler kişisel veri içerir; yalnız bu kullanıcı okuyabilsin
umask 077

$compose exec -T db pg_dump -U postgres -Fc yakinda > "backups/db-$stamp.dump"
$compose exec -T api tar -C /data -czf - media > "backups/media-$stamp.tar.gz"
find backups -type f -mtime +14 -delete

echo "$(date '+%F %T') yedek alındı: backups/db-$stamp.dump, backups/media-$stamp.tar.gz"
