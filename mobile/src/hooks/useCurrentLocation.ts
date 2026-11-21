import * as Location from 'expo-location';
import { useCallback, useEffect, useState } from 'react';

export interface Coords {
  latitude: number;
  longitude: number;
}

type LocationState =
  | { status: 'loading'; coords?: Coords }
  | { status: 'granted'; coords: Coords }
  | { status: 'denied'; coords?: undefined }
  | { status: 'error'; coords?: undefined };

/**
 * Konum izni ister ve kullanıcının konumunu döner. Önce son bilinen konumu
 * (anında) gösterir, ardından güncel konumu alır.
 */
export function useCurrentLocation() {
  const [state, setState] = useState<LocationState>({ status: 'loading' });

  const load = useCallback(async () => {
    setState((s) => ({ status: 'loading', coords: s.coords }));

    const { status } = await Location.requestForegroundPermissionsAsync();
    if (status !== 'granted') {
      setState({ status: 'denied' });
      return;
    }

    try {
      const last = await Location.getLastKnownPositionAsync();
      if (last) setState({ status: 'granted', coords: pick(last.coords) });

      const current = await Location.getCurrentPositionAsync({ accuracy: Location.Accuracy.Balanced });
      setState({ status: 'granted', coords: pick(current.coords) });
    } catch {
      setState((s) => (s.coords ? { status: 'granted', coords: s.coords } : { status: 'error' }));
    }
  }, []);

  useEffect(() => {
    load();
  }, [load]);

  return { ...state, reload: load };
}

function pick(coords: { latitude: number; longitude: number }): Coords {
  return { latitude: coords.latitude, longitude: coords.longitude };
}

/**
 * Sorgu anahtarı için ~100 m hassasiyete yuvarlar; böylece GPS'teki küçük
 * oynamalar her seferinde yeni bir API isteği tetiklemez.
 */
export function roundCoords(coords: Coords): Coords {
  return {
    latitude: Math.round(coords.latitude * 1000) / 1000,
    longitude: Math.round(coords.longitude * 1000) / 1000,
  };
}
