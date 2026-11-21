import { useMutation } from '@tanstack/react-query';
import * as Location from 'expo-location';
import { router } from 'expo-router';
import { useEffect, useMemo, useState } from 'react';
import { Alert, KeyboardAvoidingView, Platform, Pressable, ScrollView, StyleSheet, Switch, Text, View } from 'react-native';
import MapView, { Marker } from 'react-native-maps';
import { ApiError, errorMessage } from '@/api/client';
import { eventsApi } from '@/api/endpoints';
import type { Category, CreateEventInput } from '@/api/types';
import { CategoryChips } from '@/components/CategoryChips';
import { DateTimeField } from '@/components/DateTimeField';
import { Button, Card, Chip, TextField } from '@/components/ui';
import { useCategories, useInvalidateEvent } from '@/hooks/queries';
import { useCurrentLocation, type Coords } from '@/hooks/useCurrentLocation';
import { colors, font, radius, spacing } from '@/theme';

/**
 * Hızlı şablonlar: uygulamanın kapsamını (okeye 4. aramaktan partiye) tek dokunuşla gösterir.
 * capacity = organizatör hariç aranan kişi sayısı.
 */
const TEMPLATES = [
  { key: 'okey', label: '🃏 Okeye 4.', slug: 'okey-kart', title: 'Okeye 4. aranıyor', capacity: 1, approval: false, hours: 3 },
  { key: 'sinema', label: '🎬 Sinema', slug: 'sinema', title: 'Sinemaya gidelim', capacity: 3, approval: false, hours: 3 },
  { key: 'mac', label: '⚽ Halı saha', slug: 'spor', title: 'Halı sahaya oyuncu aranıyor', capacity: 2, approval: false, hours: 2 },
  { key: 'kahve', label: '☕ Kahve', slug: 'kahve-sohbet', title: 'Kahve içip sohbet edelim', capacity: 4, approval: false, hours: 2 },
  { key: 'parti', label: '🎉 Parti', slug: 'parti', title: 'Ev partisi', capacity: 30, approval: true, hours: 5 },
] as const;

type FieldErrors = Partial<Record<'categoryId' | 'title' | 'startsAt' | 'endsAt' | 'location' | 'capacity' | 'description', string>>;

function nextFullHour(hoursAhead = 1): Date {
  const date = new Date();
  date.setMinutes(0, 0, 0);
  date.setHours(date.getHours() + hoursAhead);
  return date;
}

function addHours(date: Date, hours: number): Date {
  return new Date(date.getTime() + hours * 60 * 60 * 1000);
}

function formatAddress(address: Location.LocationGeocodedAddress | undefined): string {
  if (!address) return '';
  const parts = [address.name ?? address.street, address.district, address.city ?? address.subregion]
    .filter((part): part is string => !!part && part.trim().length > 0);
  return [...new Set(parts)].join(', ');
}

export default function CreateEventScreen() {
  const categories = useCategories();
  const currentLocation = useCurrentLocation();
  const invalidate = useInvalidateEvent();

  const [categoryId, setCategoryId] = useState<number | null>(null);
  const [title, setTitle] = useState('');
  const [description, setDescription] = useState('');
  const [startsAt, setStartsAt] = useState(() => nextFullHour(1));
  const [hasEnd, setHasEnd] = useState(false);
  const [endsAt, setEndsAt] = useState(() => nextFullHour(4));
  const [point, setPoint] = useState<Coords | null>(null);
  const [placeName, setPlaceName] = useState('');
  const [address, setAddress] = useState('');
  const [addressTouched, setAddressTouched] = useState(false);
  const [unlimited, setUnlimited] = useState(false);
  const [capacity, setCapacity] = useState(1);
  const [approvalRequired, setApprovalRequired] = useState(false);
  const [errors, setErrors] = useState<FieldErrors>({});

  // Konum gelince seçili nokta yoksa kullanıcının konumunu varsayılan yap.
  useEffect(() => {
    if (!point && currentLocation.coords) setPoint(currentLocation.coords);
  }, [currentLocation.coords, point]);

  // Seçilen noktanın adresini otomatik doldur (kullanıcı elle yazmadıysa).
  useEffect(() => {
    if (!point || addressTouched) return;
    let cancelled = false;
    const timer = setTimeout(async () => {
      try {
        const [result] = await Location.reverseGeocodeAsync(point);
        if (!cancelled) setAddress(formatAddress(result));
      } catch {
        // adres bulunamazsa sorun değil, alan opsiyonel
      }
    }, 600);
    return () => {
      cancelled = true;
      clearTimeout(timer);
    };
  }, [point, addressTouched]);

  const categoryBySlug = useMemo(() => {
    const map = new Map<string, Category>();
    for (const c of categories.data ?? []) map.set(c.slug, c);
    return map;
  }, [categories.data]);

  const applyTemplate = (template: (typeof TEMPLATES)[number]) => {
    const category = categoryBySlug.get(template.slug);
    if (category) setCategoryId(category.id);
    setTitle(template.title);
    setUnlimited(false);
    setCapacity(template.capacity);
    setApprovalRequired(template.approval);
    setHasEnd(true);
    setEndsAt(addHours(startsAt, template.hours));
  };

  const resetForm = () => {
    setCategoryId(null);
    setTitle('');
    setDescription('');
    setStartsAt(nextFullHour(1));
    setHasEnd(false);
    setEndsAt(nextFullHour(4));
    setPlaceName('');
    setAddress('');
    setAddressTouched(false);
    setUnlimited(false);
    setCapacity(1);
    setApprovalRequired(false);
    setErrors({});
    if (currentLocation.coords) setPoint(currentLocation.coords);
  };

  const mutation = useMutation({
    mutationFn: (input: CreateEventInput) => eventsApi.create(input),
    onSuccess: ({ id }) => {
      invalidate();
      resetForm();
      router.push(`/event/${id}`);
    },
    onError: (error) => {
      if (error instanceof ApiError && error.fieldErrors) {
        const fe = error.fieldErrors;
        setErrors({
          categoryId: fe.categoryId?.[0],
          title: fe.title?.[0],
          startsAt: fe.startsAt?.[0],
          endsAt: fe.endsAt?.[0],
          capacity: fe.capacity?.[0],
          description: fe.description?.[0],
        });
      }
      Alert.alert('Etkinlik oluşturulamadı', errorMessage(error));
    },
  });

  const submit = () => {
    const next: FieldErrors = {};
    if (categoryId === null) next.categoryId = 'Bir kategori seç.';
    if (title.trim().length === 0) next.title = 'Başlık gerekli.';
    if (startsAt.getTime() <= Date.now()) next.startsAt = 'Başlangıç gelecekte olmalı.';
    if (hasEnd && endsAt.getTime() <= startsAt.getTime()) next.endsAt = 'Bitiş, başlangıçtan sonra olmalı.';
    if (!point) next.location = 'Haritadan bir konum seç.';
    setErrors(next);
    if (Object.keys(next).length > 0 || categoryId === null || !point) return;

    mutation.mutate({
      categoryId,
      title: title.trim(),
      description: description.trim() || null,
      startsAt: startsAt.toISOString(),
      endsAt: hasEnd ? endsAt.toISOString() : null,
      latitude: point.latitude,
      longitude: point.longitude,
      placeName: placeName.trim() || null,
      address: address.trim() || null,
      capacity: unlimited ? null : capacity,
      joinPolicy: approvalRequired ? 'ApprovalRequired' : 'Open',
    });
  };

  return (
    <KeyboardAvoidingView style={styles.flex} behavior={Platform.OS === 'ios' ? 'padding' : undefined} keyboardVerticalOffset={90}>
      <ScrollView style={styles.screen} contentContainerStyle={styles.content} keyboardShouldPersistTaps="handled">
        {/* Hızlı başlangıç */}
        <View>
          <Text style={styles.sectionLabel}>Hızlı başla</Text>
          <ScrollView horizontal showsHorizontalScrollIndicator={false} contentContainerStyle={styles.chipRow} style={styles.bleed}>
            {TEMPLATES.map((template) => (
              <Chip key={template.key} label={template.label} onPress={() => applyTemplate(template)} />
            ))}
          </ScrollView>
        </View>

        {/* Kategori */}
        <View>
          <Text style={styles.sectionLabel}>Kategori</Text>
          <View style={styles.bleed}>
            <CategoryChips
              categories={categories.data ?? []}
              selectedId={categoryId}
              onSelect={setCategoryId}
              allowAll={false}
            />
          </View>
          {errors.categoryId ? <Text style={styles.error}>{errors.categoryId}</Text> : null}
        </View>

        <TextField
          label="Başlık"
          value={title}
          onChangeText={setTitle}
          placeholder="Örn. Okeye 4. aranıyor"
          maxLength={120}
          error={errors.title}
        />

        <TextField
          label="Açıklama (isteğe bağlı)"
          value={description}
          onChangeText={setDescription}
          placeholder="Ne yapacağız, kimler var, ne getirmeli?"
          multiline
          maxLength={2000}
          error={errors.description}
        />

        {/* Zaman */}
        <Card style={styles.cardGap}>
          <DateTimeField
            label="Başlangıç"
            value={startsAt}
            minimumDate={new Date()}
            onChange={(date) => {
              setStartsAt(date);
              if (hasEnd && endsAt.getTime() <= date.getTime()) setEndsAt(addHours(date, 3));
            }}
          />
          {errors.startsAt ? <Text style={styles.error}>{errors.startsAt}</Text> : null}

          <View style={styles.switchRow}>
            <Text style={font.body}>Bitiş saati ekle</Text>
            <Switch
              value={hasEnd}
              onValueChange={(value) => {
                setHasEnd(value);
                if (value && endsAt.getTime() <= startsAt.getTime()) setEndsAt(addHours(startsAt, 3));
              }}
              trackColor={{ true: colors.primary, false: colors.border }}
            />
          </View>
          {hasEnd ? <DateTimeField label="Bitiş" value={endsAt} minimumDate={startsAt} onChange={setEndsAt} /> : null}
          {errors.endsAt ? <Text style={styles.error}>{errors.endsAt}</Text> : null}
        </Card>

        {/* Konum */}
        <View>
          <Text style={styles.sectionLabel}>Konum</Text>
          <Text style={styles.hint}>Haritaya dokunarak ya da iğneyi sürükleyerek yeri seç.</Text>
          <View style={styles.mapBox}>
            {point ? (
              <MapView
                style={StyleSheet.absoluteFill}
                initialRegion={{ ...point, latitudeDelta: 0.01, longitudeDelta: 0.01 }}
                showsUserLocation
                onPress={(e) => setPoint(e.nativeEvent.coordinate)}
              >
                <Marker
                  coordinate={point}
                  draggable
                  pinColor={colors.primary}
                  onDragEnd={(e) => setPoint(e.nativeEvent.coordinate)}
                />
              </MapView>
            ) : (
              <Text style={styles.mapPlaceholder}>Konum alınıyor…</Text>
            )}
          </View>
          {errors.location ? <Text style={styles.error}>{errors.location}</Text> : null}
        </View>

        <TextField label="Mekan adı (isteğe bağlı)" value={placeName} onChangeText={setPlaceName} placeholder="Örn. Moda Çay Bahçesi" maxLength={150} />
        <TextField
          label="Adres"
          value={address}
          onChangeText={(text) => {
            setAddressTouched(true);
            setAddress(text);
          }}
          placeholder="Otomatik doldurulur"
          maxLength={300}
        />

        {/* Katılım */}
        <Card style={styles.cardGap}>
          <Text style={font.subtitle}>Kaç kişi arıyorsun?</Text>
          <Text style={styles.hint}>Sen hariç, uygulamadan katılabilecek kişi sayısı.</Text>

          <View style={styles.stepperRow}>
            <View style={[styles.stepper, unlimited && styles.disabled]}>
              <Pressable
                style={styles.stepButton}
                disabled={unlimited || capacity <= 1}
                onPress={() => setCapacity((c) => Math.max(1, c - 1))}
              >
                <Text style={styles.stepText}>−</Text>
              </Pressable>
              <Text style={styles.stepValue}>{unlimited ? '∞' : capacity}</Text>
              <Pressable style={styles.stepButton} disabled={unlimited} onPress={() => setCapacity((c) => Math.min(10_000, c + 1))}>
                <Text style={styles.stepText}>+</Text>
              </Pressable>
            </View>
            <View style={styles.inlineSwitch}>
              <Text style={font.body}>Sınırsız</Text>
              <Switch value={unlimited} onValueChange={setUnlimited} trackColor={{ true: colors.primary, false: colors.border }} />
            </View>
          </View>
          {errors.capacity ? <Text style={styles.error}>{errors.capacity}</Text> : null}

          <View style={styles.switchRow}>
            <View style={styles.flex}>
              <Text style={font.body}>Katılımcıları ben onaylayayım</Text>
              <Text style={styles.hint}>Kapalıysa katıl diyen herkes anında katılır.</Text>
            </View>
            <Switch value={approvalRequired} onValueChange={setApprovalRequired} trackColor={{ true: colors.primary, false: colors.border }} />
          </View>
        </Card>

        <Button title="Etkinliği yayınla" onPress={submit} loading={mutation.isPending} />
      </ScrollView>
    </KeyboardAvoidingView>
  );
}

const styles = StyleSheet.create({
  flex: { flex: 1 },
  screen: { backgroundColor: colors.background },
  content: { padding: spacing.lg, gap: spacing.lg, paddingBottom: spacing.xxl },
  bleed: { marginHorizontal: -spacing.lg },
  chipRow: { gap: spacing.sm, paddingHorizontal: spacing.lg, paddingVertical: spacing.xs },
  sectionLabel: { fontSize: 14, fontWeight: '600', color: colors.text, marginBottom: spacing.xs },
  hint: { fontSize: 13, color: colors.muted, marginBottom: spacing.xs },
  error: { fontSize: 13, color: colors.danger, marginTop: spacing.xs },
  cardGap: { gap: spacing.md },
  switchRow: { flexDirection: 'row', alignItems: 'center', justifyContent: 'space-between', gap: spacing.md },
  mapBox: {
    height: 220,
    borderRadius: radius.lg,
    overflow: 'hidden',
    backgroundColor: colors.border,
    alignItems: 'center',
    justifyContent: 'center',
  },
  mapPlaceholder: { color: colors.muted },
  stepperRow: { flexDirection: 'row', alignItems: 'center', justifyContent: 'space-between' },
  stepper: {
    flexDirection: 'row',
    alignItems: 'center',
    borderWidth: 1,
    borderColor: colors.border,
    borderRadius: radius.md,
    overflow: 'hidden',
  },
  stepButton: { width: 44, height: 44, alignItems: 'center', justifyContent: 'center', backgroundColor: colors.primaryLight },
  stepText: { fontSize: 22, color: colors.primary, fontWeight: '600' },
  stepValue: { minWidth: 56, textAlign: 'center', fontSize: 18, fontWeight: '700', color: colors.text },
  inlineSwitch: { flexDirection: 'row', alignItems: 'center', gap: spacing.sm },
  disabled: { opacity: 0.4 },
});
