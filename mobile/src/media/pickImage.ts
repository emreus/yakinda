import { ImageManipulator, SaveFormat } from 'expo-image-manipulator';
import * as ImagePicker from 'expo-image-picker';
import { Alert, Linking } from 'react-native';
import type { LocalImage } from '@/api/endpoints';

/** square: profil fotoğrafı (1:1), wide: etkinlik kapağı (16:9) */
export type ImageShape = 'square' | 'wide';

const MAX_SIDE: Record<ImageShape, number> = { square: 800, wide: 1600 };

/**
 * Galeriden seçtirir ya da fotoğraf çektirir, kırptırır, küçültür ve JPEG'e çevirir.
 * Yeniden kodlama dosyadaki konum (GPS) gibi meta verileri de atar (sunucu ayrıca temizler).
 * Vazgeçilirse null döner.
 */
export async function pickImage(shape: ImageShape): Promise<LocalImage | null> {
  const source = await chooseSource();
  if (!source) return null;

  const options: ImagePicker.ImagePickerOptions = {
    mediaTypes: ['images'],
    allowsEditing: true,
    aspect: shape === 'square' ? [1, 1] : [16, 9],
    quality: 1,
  };

  let result: ImagePicker.ImagePickerResult;
  if (source === 'camera') {
    const permission = await ImagePicker.requestCameraPermissionsAsync();
    if (!permission.granted) {
      Alert.alert('Kamera izni gerekli', 'Fotoğraf çekebilmek için ayarlardan kamera iznini aç.', [
        { text: 'Vazgeç', style: 'cancel' },
        { text: 'Ayarları aç', onPress: () => Linking.openSettings() },
      ]);
      return null;
    }
    result = await ImagePicker.launchCameraAsync(options);
  } else {
    // Sistem fotoğraf seçicisi galeri izni istemez.
    result = await ImagePicker.launchImageLibraryAsync(options);
  }

  const asset = result.canceled ? undefined : result.assets[0];
  if (!asset) return null;
  return shrink(asset, MAX_SIDE[shape]);
}

/**
 * Kullanıcıya "Galeriden seç / Fotoğraf çek / Vazgeç" seçeneklerini sorar; seçimi Promise olarak döner
 * (vazgeçilirse null).
 */
function chooseSource(): Promise<'library' | 'camera' | null> {
  return new Promise((resolve) => {
    Alert.alert(
      'Fotoğraf ekle',
      undefined,
      [
        { text: 'Galeriden seç', onPress: () => resolve('library') },
        { text: 'Fotoğraf çek', onPress: () => resolve('camera') },
        { text: 'Vazgeç', style: 'cancel', onPress: () => resolve(null) },
      ],
      { cancelable: true, onDismiss: () => resolve(null) },
    );
  });
}

/**
 * Görseli en uzun kenarı maxSide olacak şekilde küçültür (zaten küçükse boyutuna dokunmaz) ve yüzde 80
 * kaliteyle JPEG olarak yeniden kaydeder. Yeniden kodlama, konum gibi meta verileri de atar.
 */
async function shrink(asset: ImagePicker.ImagePickerAsset, maxSide: number): Promise<LocalImage> {
  const context = ImageManipulator.manipulate(asset.uri);
  if (Math.max(asset.width, asset.height) > maxSide) {
    context.resize(asset.width >= asset.height ? { width: maxSide } : { height: maxSide });
  }
  const rendered = await context.renderAsync();
  const saved = await rendered.saveAsync({ compress: 0.8, format: SaveFormat.JPEG });
  return { uri: saved.uri };
}
