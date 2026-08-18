export interface Product {
  id: number;
  code: string;
  description: string;
  balance: number;
}
export interface InvoiceItem {
  productId: number;
  quantity: number;
}
export interface Invoice {
  number: number;
  status: string;
  items: InvoiceItem[];
  createdAt: string;
}
export interface PagedResult<T> {
  items: T[];
  total: number;
  page: number;
  pageSize: number;
}
export interface InvoiceFilters {
  status: 'Todos' | 'Aberta' | 'Fechada';
  sortBy: 'number' | 'date';
  sortDirection: 'asc' | 'desc';
  productId: number | null;
}
export interface LoginChallenge {
  challenge: string;
  expiresInSeconds: number;
  mfaRequired: boolean;
}
export interface SessionResult {
  token: string;
  expiresAt: string;
}
export interface ProductResult {
  message?: string;
}
