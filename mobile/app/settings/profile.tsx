import { useMutation, useQueryClient } from '@tanstack/react-query';
import { router, Stack } from 'expo-router';
import { useEffect, useState } from 'react';
import { ActivityIndicator, Alert, KeyboardAvoidingView, Platform, ScrollView, StyleSheet, Text, View } from 'react-native';
import { ApiError, errorMessage } from '@/api/client';
import { mediaApi, profileApi } from '@/api/endpoints';
import { Avatar } from '@/components/Avatar';
import { Button, EmptyState, FullScreenLoader, TextField } from '@/components/ui';
import { queryKeys, useMyProfile } from '@/hooks/queries';
import { pickImage } from '@/media/pickImage';
import { colors, font, spacing } from '@/theme';

const BIO_MAX = 300;

/** Profil fotoğrafı, görünen ad ve "hakkımda". Fotoğraf seçildiği anda yüklenir; ad ve yazı "Kaydet" ile. */
export default function EditProfileScreen() {
  const queryClient = useQueryClient();
  const profile = useMyProfile();
  const [displayName, setDisplayName] = useState('');
  const [bio, setBio] = useState('');
  const [errors, setErrors] = useState<{ displayName?: string; bio?: string }>({});

  useEffect(() => {
    if (!profile.data) return;
    setDisplayName(profile.data.displayName);
    setBio(profile.data.bio ?? '');
  }, [profile.data]);

  const refreshProfile = () => {
    queryClient.invalidateQueries({ queryKey: queryKeys.me });
    if (profile.data) queryClient.invalidateQueries({ queryKey: queryKeys.user(profile.data.id) });
  };

  const avatar = useMutation({
    mutationFn: async (action: 'change' | 'remove') => {
      if (action === 'remove') return mediaApi.deleteAvatar();
      const image = await pickImage('square');
      if (image) await mediaApi.setAvatar(image);
    },
    onSuccess: refreshProfile,
    onError: (error) => Alert.alert('Fotoğraf yüklenemedi', errorMessage(error)),
  });

  const save = useMutation({
    mutationFn: () => profileApi.update({ displayName: displayName.trim(), bio: bio.trim() || null }),
    onSuccess: () => {
      refreshProfile();
      // Etkinlik kartlarında ve sohbette ad değişsin
      queryClient.invalidateQueries({ queryKey: ['events'] });
      queryClient.invalidateQueries({ queryKey: ['event'] });
      queryClient.invalidateQueries({ queryKey: ['myEvents'] });
      router.back();
    },
    onError: (error) => {
      if (error instanceof ApiError && error.fieldErrors) {
        setErrors({ displayName: error.fieldErrors.displayName?.[0], bio: error.fieldErrors.bio?.[0] });
      }
      Alert.alert('Kaydedilemedi', errorMessage(error));
    },
  });

  const title = <Stack.Screen options={{ title: 'Profili düzenle' }} />;

  if (profile.isLoading) return <FullScreenLoader />;
  if (!profile.data) {
    return (
      <>
        {title}
        <EmptyState emoji="⚠️" title="Profil yüklenemedi" message={errorMessage(profile.error)} actionLabel="Tekrar dene" onAction={() => profile.refetch()} />
      </>
    );
  }

  return (
    <>
      {title}
      <KeyboardAvoidingView style={styles.flex} behavior={Platform.OS === 'ios' ? 'padding' : undefined} keyboardVerticalOffset={90}>
        <ScrollView style={styles.screen} contentContainerStyle={styles.content} keyboardShouldPersistTaps="handled">
          <View style={styles.avatarBlock}>
            <View>
              <Avatar name={displayName || profile.data.displayName} url={profile.data.avatarUrl} size={104} />
              {avatar.isPending ? (
                <View style={styles.avatarBusy}>
                  <ActivityIndicator color={colors.white} />
                </View>
              ) : null}
            </View>
            <View style={styles.avatarButtons}>
              <Button
                title={profile.data.avatarUrl ? 'Fotoğrafı değiştir' : 'Fotoğraf ekle'}
                variant="secondary"
                onPress={() => avatar.mutate('change')}
                disabled={avatar.isPending}
              />
              {profile.data.avatarUrl ? (
                <Button
                  title="Kaldır"
                  variant="ghost"
                  disabled={avatar.isPending}
                  onPress={() =>
                    Alert.alert('Profil fotoğrafı', 'Fotoğrafın kaldırılsın mı?', [
                      { text: 'Vazgeç', style: 'cancel' },
                      { text: 'Kaldır', style: 'destructive', onPress: () => avatar.mutate('remove') },
                    ])
                  }
                />
              ) : null}
            </View>
            <Text style={styles.hint}>Yüzünün göründüğü bir fotoğraf, katılım isteklerinin onaylanmasını kolaylaştırır.</Text>
          </View>

          <TextField label="Görünen ad" value={displayName} onChangeText={setDisplayName} maxLength={50} error={errors.displayName} />
          <TextField
            label="Hakkımda (isteğe bağlı)"
            value={bio}
            onChangeText={setBio}
            placeholder="Neleri seversin? Hangi oyunları oynarsın?"
            multiline
            maxLength={BIO_MAX}
            hint={`${bio.length}/${BIO_MAX}`}
            error={errors.bio}
          />

          <Button title="Kaydet" onPress={() => save.mutate()} loading={save.isPending} disabled={displayName.trim().length < 2} />
        </ScrollView>
      </KeyboardAvoidingView>
    </>
  );
}

const styles = StyleSheet.create({
  flex: { flex: 1 },
  screen: { flex: 1, backgroundColor: colors.background },
  content: { padding: spacing.lg, gap: spacing.lg },
  avatarBlock: { alignItems: 'center', gap: spacing.md },
  avatarBusy: {
    ...StyleSheet.absoluteFillObject,
    borderRadius: 52,
    backgroundColor: 'rgba(0,0,0,0.35)',
    alignItems: 'center',
    justifyContent: 'center',
  },
  avatarButtons: { flexDirection: 'row', gap: spacing.sm },
  hint: { ...font.small, textAlign: 'center' },
});
