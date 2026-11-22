import { useEffect, useState } from 'react';
import { Image, StyleSheet, Text, View } from 'react-native';
import { mediaUrl } from '@/api/client';
import { colors } from '@/theme';

/** Profil fotoğrafı; yoksa (ya da yüklenemezse) adın baş harfi. */
export function Avatar({ name, url, size = 40 }: { name: string; url?: string | null; size?: number }) {
  const uri = mediaUrl(url);
  const [failed, setFailed] = useState(false);
  useEffect(() => setFailed(false), [uri]);

  const shape = { width: size, height: size, borderRadius: size / 2 };

  if (uri && !failed) {
    return (
      <Image
        source={{ uri }}
        style={[styles.image, shape]}
        onError={() => setFailed(true)}
        accessibilityLabel={`${name} profil fotoğrafı`}
      />
    );
  }

  return (
    <View style={[styles.fallback, shape]}>
      <Text style={[styles.initial, { fontSize: Math.round(size * 0.42) }]}>
        {(name.trim().charAt(0) || '?').toLocaleUpperCase('tr-TR')}
      </Text>
    </View>
  );
}

const styles = StyleSheet.create({
  image: { backgroundColor: colors.primaryLight },
  fallback: { backgroundColor: colors.primaryLight, alignItems: 'center', justifyContent: 'center' },
  initial: { fontWeight: '700', color: colors.primary },
});
