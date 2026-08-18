import { Injectable } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { AuthSessionService } from './auth-session.service';
import { Product, ProductResult } from './models';

@Injectable({ providedIn: 'root' })
export class StockApiService {
  private readonly api = 'http://localhost:5101/api';
  constructor(
    private http: HttpClient,
    private auth: AuthSessionService,
  ) {}
  list() {
    return this.http.get<Product[]>(`${this.api}/products`);
  }
  create(product: Omit<Product, 'id'>) {
    return this.http.post<ProductResult>(`${this.api}/products`, product, {
      headers: this.auth.headers(),
    });
  }
}
