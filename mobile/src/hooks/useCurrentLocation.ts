import * as Location from 'expo-location';
import { useCallback, useEffect, useState } from 'react';
import { AppState } from 'react-native';

// Güncel konum bu sürede gelmezse (kapalı alan, GPS kapalı) beklemek bırakılır: son bilinen konum varsa
// onunla devam edilir, yoksa "konum alınamadı" durumu gösterilir.
const LOCATION_TIMEOUT_MS = 15_000;

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
 * (anında) gösterir, ardından güncel konumu alır. İzin reddedildiyse, kullanıcı izni telefonun
 * ayarlarından verip uygulamaya döndüğünde konum kendiliğinden yeniden yüklenir.
 */
export function useCurrentLocation() {
  const [state, setState] = useState<LocationState>({ status: 'loading' });

  // Konumu yükler: izin ister (reddedilirse "denied"), önce son bilinen konumu hemen gösterir, sonra güncel
  // konumu alır. Hata olursa ya da konum 15 saniyede gelmezse eldeki konum korunur; hiç konum yoksa
  // "error" durumuna geçer (ekran "Tekrar dene" gösterir).
  const load = useCallback(async () => {
    setState((s) => ({ status: 'loading', coords: s.coords }));

    try {
      const { status } = await Location.requestForegroundPermissionsAsync();
      if (status !== 'granted') {
        setState({ status: 'denied' });
        return;
      }

      const last = await Location.getLastKnownPositionAsync();
      if (last) setState({ status: 'granted', coords: pick(last.coords) });

      const current = await withTimeout(
        Location.getCurrentPositionAsync({ accuracy: Location.Accuracy.Balanced }),
        LOCATION_TIMEOUT_MS,
      );
      setState({ status: 'granted', coords: pick(current.coords) });
    } catch {
      setState((s) => (s.coords ? { status: 'granted', coords: s.coords } : { status: 'error' }));
    }
  }, []);

  useEffect(() => {
    load();
  }, [load]);

  // İzin reddedildiyse ekranda "Ayarları aç" çıkar. Kullanıcı izni ayarlardan verip geri döndüğünde
  // izin (yeniden sormadan) kontrol edilir; verilmişse konum yüklenir. Bu olmadan uygulama, kapatılıp
  // açılana kadar "Konum izni gerekli" ekranında kalır.
  useEffect(() => {
    if (state.status !== 'denied') return;
    const subscription = AppState.addEventListener('change', (next) => {
      if (next !== 'active') return;
      Location.getForegroundPermissionsAsync()
        .then(({ status }) => {
          if (status === 'granted') load();
        })
        .catch(() => undefined);
    });
    return () => subscription.remove();
  }, [state.status, load]);

  return { ...state, reload: load };
}

/** Promise verilen sürede sonuçlanmazsa hata fırlatır; sonuçlanırsa değeri (ya da hatasını) aynen iletir. */
function withTimeout<T>(promise: Promise<T>, ms: number): Promise<T> {
  return new Promise<T>((resolve, reject) => {
    const timer = setTimeout(() => reject(new Error('timeout')), ms);
    promise.then(
      (value) => {
        clearTimeout(timer);
        resolve(value);
      },
      (error: unknown) => {
        clearTimeout(timer);
        reject(error);
      },
    );
  });
}

/** Konum nesnesinden yalnızca enlem ve boylamı alır. */
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
