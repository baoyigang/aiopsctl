export interface Space {
  id: string;
  name: string;
}
export interface Meta {
  product: string;
  version: string;
  mode: string;
  now: string;
  revision: number;
  spaces: Space[];
  sourceStatus: { id: string; label: string; kind: string; state: string; detail: string }[];
}
export interface WindowInfo {
  from: string;
  to: string;
  label: string;
  timezone: string;
}
export interface Business {
  id: string;
  spaceId: string;
  name: string;
  owner: string;
  description: string;
  service: string;
  environment: string;
  targetAvailability: number;
  updatedAt: string;
}
export type BusinessInput = Omit<Business, 'id' | 'updatedAt'>;
export interface BusinessSummary extends Business {
  availability: number | null;
  chartCount: number;
  quotaMinutes: number;
  consumedMinutes: number;
  remainingMinutes: number;
  consumedPercent: number;
  eventCount: number;
  mttrMinutes?: number | null;
  mtbfMinutes?: number | null;
}
export interface Overview {
  revision: number;
  window: WindowInfo;
  items: BusinessSummary[];
}
export interface Indicator {
  id: string;
  spaceId: string;
  name: string;
  sourceId: string;
  sourceName: string;
  metricName: string;
  unit: string;
  owner: string;
  visibility: 'space' | 'public';
  enabled: boolean;
  businessIds: string[];
  behavior: 'normal' | 'empty';
  updatedAt: string;
  updatedBy: string;
}
export type IndicatorInput = Omit<Indicator, 'id' | 'updatedAt' | 'updatedBy'>;
export interface PageResult<T> {
  revision: number;
  items: T[];
  total: number;
  page: number;
  pageSize: number;
}
export interface ChartResult {
  indicatorId: string;
  name: string;
  unit: string;
  sourceId: string;
  points: { timestamp: string; value: number | null; comparisonValue?: number | null }[];
  stats: {
    sum: number | null;
    max: number | null;
    min: number | null;
    average: number | null;
    lastUpdated: string | null;
    comparisonDeltaPercent: number | null;
  };
  emptyReason?: string;
  error?: string;
  warnings?: string[];
  intervalSeconds?: number;
}
export interface Dashboard {
  businessId: string;
  window: WindowInfo;
  aggregation: string;
  comparison: string;
  charts: ChartResult[];
  mode: string;
}
export interface BusinessDetail {
  revision: number;
  data: Business;
  indicators: Indicator[];
}
export interface IncidentEvent {
  id: string;
  spaceId: string;
  businessId: string;
  title: string;
  level: 'P1' | 'P2' | 'P3';
  startedAt: string;
  endedAt: string | null;
  description: string;
}
export type IncidentInput = Omit<IncidentEvent, 'id'>;
export interface Stability {
  revision: number;
  window: WindowInfo;
  summary: {
    businessCount: number;
    belowTargetCount: number;
    eventCount: number;
    consumedMinutes: number;
    quotaMinutes: number;
  };
  items: BusinessSummary[];
  events: IncidentEvent[];
}
export interface StatusMatrix {
  days: string[];
  spaces: {
    id: string;
    name: string;
    businesses: {
      id: string;
      name: string;
      latestStatus: string;
      days: { date: string; status: string; availability: number | null; eventCount: number }[];
    }[];
  }[];
  timezone: string;
  windowLabel: string;
}
export type ChartKind = 'line' | 'bar' | 'funnel';
export interface ScreenPanel {
  indicatorId: string;
  kind: ChartKind;
  aggregation: 'avg' | 'sum' | 'raw';
}
export interface Screen {
  id: string;
  spaceId: string;
  name: string;
  businessId: string;
  columns: number;
  panels: ScreenPanel[];
  updatedAt: string;
  updatedBy: string;
}
export type ScreenInput = Omit<Screen, 'id' | 'updatedAt' | 'updatedBy'>;
export interface ScreenExport {
  format: 'hengchuan-screen';
  version: 1;
  screen: Omit<ScreenInput, 'spaceId'>;
}
export interface ScreenPreview {
  revision: number;
  screen: Screen;
  dashboard: Dashboard;
}
export interface EntityResult<T> {
  revision: number;
  data: T;
}
