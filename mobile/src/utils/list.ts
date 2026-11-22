/**
 * Listedeki tekrar eden kayıtları atar; ilk görülen kalır ve sıra korunur. Sayfalı listelerde gerekir:
 * iki sayfa isteği arasında yeni kayıt eklenirse sıra kayar ve aynı kayıt bir sonraki sayfada yeniden
 * gelir; liste bileşeni aynı anahtarı iki kez görmemeli.
 */
export function uniqueBy<T>(items: readonly T[], keyOf: (item: T) => string): T[] {
  const seen = new Set<string>();
  const result: T[] = [];
  for (const item of items) {
    const key = keyOf(item);
    if (seen.has(key)) continue;
    seen.add(key);
    result.push(item);
  }
  return result;
}
