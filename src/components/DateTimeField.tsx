import DateTimePicker, { DateTimePickerAndroid, type DateTimePickerEvent } from '@react-native-community/datetimepicker';
import { Platform, Pressable, StyleSheet, Text, View } from 'react-native';
import { colors, radius, spacing } from '@/theme';
import { formatDateTimeLong } from '@/utils/format';

interface DateTimeFieldProps {
  label: string;
  value: Date;
  onChange: (value: Date) => void;
  minimumDate?: Date;
}

/**
 * iOS: yerleşik kompakt tarih+saat seçici.
 * Android: "datetime" modu olmadığı için önce tarih, ardından saat diyaloğu açılır.
 */
export function DateTimeField({ label, value, onChange, minimumDate }: DateTimeFieldProps) {
  if (Platform.OS === 'ios') {
    return (
      <View style={styles.iosRow}>
        <Text style={styles.label}>{label}</Text>
        <DateTimePicker
          value={value}
          mode="datetime"
          display="compact"
          locale="tr-TR"
          minimumDate={minimumDate}
          minuteInterval={5}
          onChange={(_event: DateTimePickerEvent, date?: Date) => {
            if (date) onChange(date);
          }}
        />
      </View>
    );
  }

  const openAndroidPicker = () => {
    DateTimePickerAndroid.open({
      value,
      mode: 'date',
      minimumDate,
      onChange: (dateEvent: DateTimePickerEvent, pickedDate?: Date) => {
        if (dateEvent.type !== 'set' || !pickedDate) return;
        DateTimePickerAndroid.open({
          value: pickedDate,
          mode: 'time',
          is24Hour: true,
          onChange: (timeEvent: DateTimePickerEvent, pickedTime?: Date) => {
            if (timeEvent.type !== 'set' || !pickedTime) return;
            const merged = new Date(pickedDate);
            merged.setHours(pickedTime.getHours(), pickedTime.getMinutes(), 0, 0);
            onChange(merged);
          },
        });
      },
    });
  };

  return (
    <Pressable onPress={openAndroidPicker} style={styles.androidField}>
      <Text style={styles.label}>{label}</Text>
      <Text style={styles.value}>{formatDateTimeLong(value)}</Text>
    </Pressable>
  );
}

const styles = StyleSheet.create({
  iosRow: { flexDirection: 'row', alignItems: 'center', justifyContent: 'space-between', paddingVertical: spacing.xs },
  label: { fontSize: 14, fontWeight: '600', color: colors.text },
  androidField: {
    gap: 4,
    backgroundColor: colors.card,
    borderWidth: 1,
    borderColor: colors.border,
    borderRadius: radius.md,
    padding: spacing.md,
  },
  value: { fontSize: 16, color: colors.primary },
});
