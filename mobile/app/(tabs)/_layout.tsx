import { Redirect, Tabs } from 'expo-router';
import { Text } from 'react-native';
import { useAuth } from '@/auth/AuthContext';
import { FullScreenLoader } from '@/components/ui';
import { NearbyFiltersProvider } from '@/state/NearbyFilters';
import { colors } from '@/theme';

function TabIcon({ emoji, focused }: { emoji: string; focused: boolean }) {
  return <Text style={{ fontSize: 20, opacity: focused ? 1 : 0.5 }}>{emoji}</Text>;
}

export default function TabsLayout() {
  const { status } = useAuth();

  if (status === 'loading') return <FullScreenLoader />;
  if (status === 'signedOut') return <Redirect href="/login" />;

  return (
    <NearbyFiltersProvider>
      <Tabs
        screenOptions={{
          tabBarActiveTintColor: colors.primary,
          tabBarInactiveTintColor: colors.muted,
          headerTitleStyle: { fontWeight: '700', color: colors.text },
        }}
      >
        <Tabs.Screen
          name="index"
          options={{ title: 'Yakınımda', tabBarIcon: ({ focused }) => <TabIcon emoji="📍" focused={focused} /> }}
        />
        <Tabs.Screen
          name="map"
          options={{ title: 'Harita', tabBarIcon: ({ focused }) => <TabIcon emoji="🗺️" focused={focused} /> }}
        />
        <Tabs.Screen
          name="create"
          options={{ title: 'Oluştur', tabBarIcon: ({ focused }) => <TabIcon emoji="➕" focused={focused} /> }}
        />
        <Tabs.Screen
          name="profile"
          options={{ title: 'Profil', tabBarIcon: ({ focused }) => <TabIcon emoji="👤" focused={focused} /> }}
        />
      </Tabs>
    </NearbyFiltersProvider>
  );
}
