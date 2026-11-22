import { useMutation } from '@tanstack/react-query';
import { router } from 'expo-router';
import { useState } from 'react';
import { Alert } from 'react-native';
import { errorMessage } from '@/api/client';
import { eventsApi, mediaApi } from '@/api/endpoints';
import { EventForm, toFormErrors, type EventFormErrors, type EventFormValues } from '@/components/EventForm';
import { useInvalidateEvent } from '@/hooks/queries';

/**
 * Etkinlik oluşturma sekmesi: ortak formu oluşturma modunda gösterir. Başarılı oluşturmadan sonra form
 * sıfırlanır (anahtarı değiştirilerek yeniden kurulur) ve yeni etkinliğin detayına gidilir.
 */
export default function CreateEventScreen() {
  const invalidate = useInvalidateEvent();
  // Başarılı gönderimden sonra formu sıfırlamak için yeniden mount edilir.
  const [formKey, setFormKey] = useState(0);
  const [serverErrors, setServerErrors] = useState<EventFormErrors>({});

  // Oluşturma: önce etkinliği oluşturur, sonra seçildiyse kapak görselini yükler. Görsel yüklenemezse
  // etkinlik yine yayında kalır ve kullanıcıya haber verilir. Sunucu alan hatası dönerse forma aktarılır.
  const mutation = useMutation({
    mutationFn: async ({ cover, ...input }: EventFormValues) => {
      const { id } = await eventsApi.create(input);
      // Görsel, etkinlik oluştuktan sonra yüklenir; yüklenemezse etkinlik yine de yayında kalır.
      let coverFailed = false;
      if (cover.kind === 'set') {
        await mediaApi.setEventImage(id, cover.image).catch(() => {
          coverFailed = true;
        });
      }
      return { id, coverFailed };
    },
    onSuccess: ({ id, coverFailed }, input) => {
      invalidate();
      setServerErrors({});
      setFormKey((key) => key + 1);
      router.push(`/event/${id}`);
      if (coverFailed) {
        Alert.alert('Görsel yüklenemedi', 'Etkinlik yayınlandı. Görseli etkinliği düzenleyerek yeniden ekleyebilirsin.');
      } else if (input.repeatWeeks > 0) {
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
