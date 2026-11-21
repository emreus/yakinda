import { createContext, useCallback, useContext, useMemo, useState, type ReactNode } from 'react';
import type { NearbySort } from '@/api/endpoints';

/** Liste ve harita sekmelerinin paylaştığı filtreler. */
export interface NearbyFilters {
  radiusKm: number;
  categoryId: number | null;
  onlyAvailable: boolean;
  sort: NearbySort;
}

export const RADIUS_OPTIONS = [1, 3, 5, 10, 25, 50] as const;

const defaultFilters: NearbyFilters = {
  radiusKm: 5,
  categoryId: null,
  onlyAvailable: false,
  sort: 'distance',
};

interface FiltersContextValue {
  filters: NearbyFilters;
  updateFilters: (patch: Partial<NearbyFilters>) => void;
}

const FiltersContext = createContext<FiltersContextValue | null>(null);

export function NearbyFiltersProvider({ children }: { children: ReactNode }) {
  const [filters, setFilters] = useState<NearbyFilters>(defaultFilters);
  const updateFilters = useCallback(
    (patch: Partial<NearbyFilters>) => setFilters((current) => ({ ...current, ...patch })),
    [],
  );
  const value = useMemo(() => ({ filters, updateFilters }), [filters, updateFilters]);
  return <FiltersContext.Provider value={value}>{children}</FiltersContext.Provider>;
}

export function useNearbyFilters(): FiltersContextValue {
  const context = useContext(FiltersContext);
  if (!context) throw new Error('useNearbyFilters, NearbyFiltersProvider içinde kullanılmalı.');
  return context;
}
