import { useMutation } from '@tanstack/react-query';
import { router, Stack, useLocalSearchParams } from 'expo-router';
import { useState } from 'react';
import { Alert } from 'react-native';
import { errorMessage } from '@/api/client';
import { eventsApi } from '@/api/endpoints';
import type { UpdateEventInput } from '@/api/types';
import { EventForm, toFormErrors, type EventFormErrors } from '@/components/EventForm';
import { EmptyState, FullScreenLoader } from '@/components/ui';
import { useEventDetail, useInvalidateEvent } from '@/hooks/queries';

/** Organizatör, başlamamış etkinliğini düzenler. Saat ya da yer değişirse katılımcılara bildirim gider. */
export default function EditEventScreen() {
  const { id } = useLocalSearchParams<{ id: string }>();
  const event = useEventDetail(id);
  const invalidate = useInvalidateEvent();
  const [serverErrors, setServerErrors] = useState<EventFormErrors>({});

  const mutation = useMutation({
    mutationFn: (input: UpdateEventInput) => eventsApi.update(id, input),
    onSuccess: () => {
      invalidate(id);
      router.back();
    },
    onError: (error) => {
      setServerErrors(toFormErrors(error));
      Alert.alert('Kaydedilemedi', errorMessage(error));
    },
  });

  const title = <Stack.Screen options={{ title: 'Etkinliği düzenle' }} />;

  if (event.isLoading) return <FullScreenLoader />;

  if (!event.data) {
    return (
      <>
        {title}
        <EmptyState emoji="⚠️" title="Etkinlik yüklenemedi" message={errorMessage(event.error)} />
      </>
    );
  }

  const e = event.data;
  if (!e.canEdit) {
    return (
      <>
        {title}
        <EmptyState emoji="🔒" title="Bu etkinlik düzenlenemez" message="Sadece başlamamış ve iptal edilmemiş etkinliklerini düzenleyebilirsin." />
      </>
    );
  }

  return (
    <>
      {title}
      <EventForm
        mode="edit"
        initial={{
          categoryId: e.categoryId,
          title: e.title,
          description: e.description,
          startsAt: new Date(e.startsAt),
          endsAt: e.endsAt ? new Date(e.endsAt) : null,
          latitude: e.latitude,
          longitude: e.longitude,
          placeName: e.placeName,
          address: e.address,
          capacity: e.capacity,
          approvalRequired: e.joinPolicy === 'ApprovalRequired',
        }}
        minCapacity={e.participantCount}
        submitLabel="Değişiklikleri kaydet"
        submitting={mutation.isPending}
        serverErrors={serverErrors}
        onSubmit={({ joinPolicy: _joinPolicy, repeatWeeks: _repeatWeeks, ...input }) => mutation.mutate(input)}
      />
    </>
  );
}
