import { Component, OnInit } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { HttpClient, HttpHeaders } from '@angular/common/http';
import { MatButtonModule } from '@angular/material/button';
import { MatCardModule } from '@angular/material/card';
import { MatChipsModule } from '@angular/material/chips';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { MatSelectModule } from '@angular/material/select';

@Component({ selector: 'app-root', imports: [CommonModule, FormsModule, MatButtonModule, MatCardModule, MatChipsModule, MatFormFieldModule, MatInputModule, MatSelectModule], templateUrl: './app.component.html', styleUrl: './app.component.scss' })
export class AppComponent implements OnInit {
  readonly stockApi = 'http://localhost:5101/api';
  readonly billingApi = 'http://localhost:5102/api';
  products: any[] = []; invoices: any[] = []; product = { code: '', description: '', balance: 0 }; items: any[] = [];
  productCodeInvalid = false; productDescriptionInvalid = false;
  selectedId = 0; quantity = 1; username = ''; password = ''; code = ''; challenge = ''; token = '';
  message = ''; notificationKind: 'success' | 'error' | 'info' = 'info'; closingError = ''; processing = false; expandedInvoiceNumber: number | null = null;
  private notificationTimer?: ReturnType<typeof setTimeout>;
  activePanel: 'menu' | 'products' | 'new-invoice' | 'invoices' = 'menu';
  get availableProducts() { return this.products.filter(product => product.balance > 0); }
  get outOfStockProducts() { return this.products.filter(product => product.balance === 0); }
  constructor(private http: HttpClient) {}
  ngOnInit() { this.loadProducts(); this.restoreSession(); }
  restoreSession() { const saved = sessionStorage.getItem('korp.session'); if (!saved) return; try { const session = JSON.parse(saved); if (typeof session.token === 'string' && session.token && session.expiresAt && new Date(session.expiresAt) > new Date()) { this.token = session.token; this.loadInvoices(); return; } } catch { /* Sessões corrompidas são descartadas localmente. */ } sessionStorage.removeItem('korp.session'); }
  loadProducts() { this.http.get<any[]>(`${this.stockApi}/products`).subscribe({ next: products => this.products = products, error: () => this.notify('Não foi possível conectar ao serviço de estoque.', 'error') }); }
  addProduct() {
    this.productCodeInvalid = !/^\d+$/.test(this.product.code.trim());
    this.productDescriptionInvalid = !/^[\p{L}]+(?:\s+[\p{L}]+)*$/u.test(this.product.description.trim());
    if (this.productCodeInvalid || this.productDescriptionInvalid || this.product.balance <= 0) {
      const reasons = [this.productCodeInvalid ? 'o código deve conter apenas números' : '', this.productDescriptionInvalid ? 'a descrição deve conter apenas letras' : '', this.product.balance <= 0 ? 'o saldo deve ser maior que zero' : ''].filter(Boolean);
      this.notify(`Não foi possível salvar o produto: ${reasons.join('; ')}.`, 'error');
      return;
    }
    this.http.post<any>(`${this.stockApi}/products`, this.product, { headers: this.auth() }).subscribe({ next: result => { this.product = { code: '', description: '', balance: 0 }; this.productCodeInvalid = false; this.productDescriptionInvalid = false; this.notify(result.message || 'Produto cadastrado com sucesso.', 'success'); this.loadProducts(); }, error: error => { if (this.handleUnauthorized(error)) return; const reason = error.error?.message || error.error?.errors?.product?.[0] || 'ocorreu um erro inesperado'; this.notify(`Não foi possível salvar o produto: ${reason}`, 'error'); } });
  }
  login() { this.http.post<any>(`${this.billingApi}/auth/login`, { username: this.username, password: this.password }).subscribe({ next: result => { this.challenge = result.challenge; this.notify('Senha validada. Informe o código do seu autenticador.', 'info'); }, error: error => this.notify(error.status === 429 ? 'Muitas tentativas. Aguarde alguns minutos.' : 'Credenciais inválidas.', 'error') }); }
  validateMfa() { this.http.post<any>(`${this.billingApi}/auth/mfa`, { challenge: this.challenge, code: this.code }).subscribe({ next: result => { this.token = result.token; sessionStorage.setItem('korp.session', JSON.stringify({ token: result.token, expiresAt: result.expiresAt })); this.notify('Acesso protegido liberado.', 'success'); this.loadInvoices(); }, error: error => { if (error.status === 429) this.challenge = ''; this.notify(error.status === 429 ? 'Limite de tentativas atingido. Inicie o login novamente em alguns minutos.' : 'Código MFA inválido ou expirado.', 'error'); } }); }
  logout() { this.http.delete(`${this.billingApi}/auth/session`, { headers: this.auth() }).subscribe({ next: () => this.finishLogout(true), error: () => this.finishLogout(false) }); }
  finishLogout(serverSessionRemoved: boolean) { sessionStorage.removeItem('korp.session'); this.token = ''; this.challenge = ''; this.code = ''; this.activePanel = 'menu'; this.notify(serverSessionRemoved ? 'Sessão encerrada com segurança.' : 'A sessão local foi encerrada, mas não foi possível invalidar o token no servidor.', serverSessionRemoved ? 'success' : 'error'); }
  sanitizeProductCode(value: string) { const sanitized = value.replace(/\D/g, ''); this.productCodeInvalid = value !== sanitized; this.product.code = sanitized; }
  sanitizeProductDescription(value: string) { const sanitized = value.replace(/[^\p{L}\s]/gu, ''); this.productDescriptionInvalid = value !== sanitized; this.product.description = sanitized; }
  addItem() { if (!this.selectedId) { this.notify('Selecione um produto para adicionar à nota.', 'error'); return; } if (!Number.isInteger(this.quantity) || this.quantity <= 0) { this.notify('Informe uma quantidade válida, maior que zero, para adicionar o produto à nota.', 'error'); return; } const existing = this.items.find(item => item.productId === this.selectedId); if (existing) existing.quantity += this.quantity; else this.items.push({ productId: this.selectedId, quantity: this.quantity }); this.selectedId = 0; this.quantity = 1; this.notify(existing ? 'Quantidade do item atualizada na nota.' : 'Item adicionado à nota.', 'success'); }
  removeItem(productId: number) { this.items = this.items.filter(item => item.productId !== productId); this.notify('Item removido da nota.', 'success'); }
  createInvoice() { this.http.post<any>(`${this.billingApi}/invoices`, { items: this.items, idempotencyKey: crypto.randomUUID() }, { headers: this.auth() }).subscribe({ next: invoice => { this.items = []; this.notify(`Nota fiscal #${invoice.number} criada com sucesso.`, 'success'); this.loadInvoices(); }, error: error => this.notify(`Não foi possível criar a nota: ${error.error?.message || error.error?.detail || 'ocorreu um erro inesperado'}`, 'error') }); }
  loadInvoices() { this.http.get<any[]>(`${this.billingApi}/invoices`, { headers: this.auth() }).subscribe({ next: invoices => this.invoices = invoices, error: error => { if (!this.handleUnauthorized(error)) this.notify('Não foi possível carregar as notas fiscais. Verifique se o serviço de faturamento está ativo.', 'error'); } }); }
  toggleInvoice(number: number) { this.expandedInvoiceNumber = this.expandedInvoiceNumber === number ? null : number; }
  invoiceAction(invoice: any) { if (invoice.status === 'Aberta') this.print(invoice); else this.printDocument(invoice); }
  print(invoice: any) { this.processing = true; this.closingError = ''; this.http.post(`${this.billingApi}/invoices/${invoice.number}/print`, {}, { headers: this.auth() }).subscribe({ next: () => { this.processing = false; this.notify(`Nota fiscal #${invoice.number} fechada com sucesso.`, 'success'); this.loadInvoices(); this.loadProducts(); }, error: error => { this.processing = false; this.closingError = error.error?.detail || error.error?.message || 'Não foi possível fechar a nota. Tente novamente.'; } }); }
  printDocument(invoice: any) {
    const printWindow = window.open('', '_blank', 'width=800,height=900');
    if (!printWindow) { this.notify('Não foi possível abrir a impressão. Verifique se o navegador bloqueou a janela pop-up.', 'error'); return; }
    const rows = invoice.items.map((item: any) => `<tr><td>${this.escapeHtml(this.productName(item.productId))}</td><td>${item.quantity}</td></tr>`).join('');
    const createdAt = invoice.createdAt ? new Date(invoice.createdAt).toLocaleString('pt-BR') : 'Não informado';
    printWindow.document.write(`<!doctype html><html lang="pt-BR"><head><meta charset="utf-8"><title>Nota fiscal #${invoice.number}</title><style>body{font-family:Arial,sans-serif;color:#172033;margin:40px}header{border-bottom:2px solid #1d4a5c;margin-bottom:28px;padding-bottom:16px}h1{margin:0;color:#1d4a5c}p{margin:6px 0}table{border-collapse:collapse;width:100%;margin-top:24px}th,td{border:1px solid #cbd5e1;padding:10px;text-align:left}th{background:#edf3f7}@media print{body{margin:20px}}</style></head><body><header><h1>KORP</h1><p>Documento de nota fiscal</p></header><p><strong>Número:</strong> #${invoice.number}</p><p><strong>Status:</strong> ${this.escapeHtml(invoice.status)}</p><p><strong>Criada em:</strong> ${createdAt}</p><table><thead><tr><th>Produto</th><th>Quantidade</th></tr></thead><tbody>${rows}</tbody></table></body></html>`);
    printWindow.document.close();
    printWindow.focus();
    window.setTimeout(() => printWindow.print(), 250);
  }
  notify(message: string, kind: 'success' | 'error' | 'info') { window.clearTimeout(this.notificationTimer); this.message = message; this.notificationKind = kind; this.notificationTimer = window.setTimeout(() => this.message = '', 5000); }
  escapeHtml(value: string) { return value.replace(/[&<>'"]/g, character => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', "'": '&#39;', '"': '&quot;' })[character]!); }
  auth() { return new HttpHeaders({ Authorization: `Bearer ${this.token}` }); }
  handleUnauthorized(error: any) { if (error.status !== 401) return false; sessionStorage.removeItem('korp.session'); this.token = ''; this.activePanel = 'menu'; this.notify('Sua sessão expirou. Entre novamente para continuar.', 'error'); return true; }
  stockBalanceClass(balance: number) { if (balance === 0) return 'stock-empty'; if (balance <= 5) return 'stock-critical'; if (balance <= 10) return 'stock-warning'; return 'stock-available'; }
  productName(id: number) { return this.products.find(product => product.id === id)?.description || `Produto #${id}`; }
}
