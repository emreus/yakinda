export const colors = {
  primary: '#5B3FD9',
  primaryLight: '#EEEAFE',
  text: '#16161D',
  muted: '#6B6B7B',
  border: '#E4E4EC',
  background: '#F6F6FA',
  card: '#FFFFFF',
  danger: '#E5484D',
  dangerLight: '#FDECEC',
  success: '#1F9D63',
  successLight: '#E6F6EE',
  warning: '#B97400',
  warningLight: '#FFF4DB',
  star: '#FFB400',
  white: '#FFFFFF',
} as const;

export const spacing = { xs: 4, sm: 8, md: 12, lg: 16, xl: 24, xxl: 32 } as const;

export const radius = { sm: 8, md: 12, lg: 16, pill: 999 } as const;

export const font = {
  title: { fontSize: 22, fontWeight: '700' as const, color: colors.text },
  subtitle: { fontSize: 17, fontWeight: '600' as const, color: colors.text },
  body: { fontSize: 15, color: colors.text },
  small: { fontSize: 13, color: colors.muted },
};
