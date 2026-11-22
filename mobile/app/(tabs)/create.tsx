import { useMutation } from '@tanstack/react-query';
import { router } from 'expo-router';
import { useState } from 'react';
import { Alert } from 'react-native';
import { errorMessage } from '@/api/client';
import { eventsApi } from '@/api/endpoints';
import { EventForm, toFormErrors, type EventFormErrors, type EventFormValues } from '@/components/EventForm';
import { useInvalidateEvent } from '@/hooks/queries';

export default function CreateEventScreen() {
  const invalidate = useInvalidateEvent();
  // Başarılı gönderimden sonra formu sıfırlamak için yeniden mount edilir.
  const [formKey, setFormKey] = useState(0);
  const [serverErrors, setServerErrors] = useState<EventFormErrors>({});

  const mutation = useMutation({
    mutationFn: (input: EventFormValues) => eventsApi.create(input),
    onSuccess: ({ id }, input) => {
      invalidate();
      setServerErrors({});
      setFormKey((key) => key + 1);
      router.push(`/event/${id}`);
      if (input.repeatWeeks > 0) {
        Alert.alert('Etkinlikler oluşturuldu', `Her hafta aynı saatte, toplam ${input.repeatWeeks + 1} etkinlik yayınlandı.`);
      }
    },
    onError: (error) => {
      setServerErrors(toFormErrors(error));
      Alert.alert('Etkinlik oluşturulamadı', errorMessage(error));
    },
  });

  return (
    <EventForm
      key={formKey}
      mode="create"
      submitLabel="Etkinliği yayınla"
      submitting={mutation.isPending}
      serverErrors={serverErrors}
      onSubmit={(values) => mutation.mutate(values)}
    />
  );
}
