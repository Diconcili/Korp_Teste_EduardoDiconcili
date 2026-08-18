import { Injectable } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { AuthSessionService } from './auth-session.service';
import { Invoice, InvoiceItem, PagedResult } from './models';

@Injectable({ providedIn: 'root' })
export class InvoiceApiService {
  private readonly api = 'http://localhost:5102/api';
  constructor(
    private http: HttpClient,
    private auth: AuthSessionService,
  ) {}
  list(page: number, pageSize: number) {
    return this.http.get<PagedResult<Invoice>>(`${this.api}/invoices`, {
      params: { page, pageSize },
      headers: this.auth.headers(),
    });
  }
  create(items: InvoiceItem[]) {
    return this.http.post<Invoice>(
      `${this.api}/invoices`,
      { items, idempotencyKey: crypto.randomUUID() },
      { headers: this.auth.headers() },
    );
  }
  close(number: number) {
    return this.http.post(
      `${this.api}/invoices/${number}/print`,
      {},
      { headers: this.auth.headers() },
    );
  }
}
