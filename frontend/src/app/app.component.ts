import { CommonModule } from '@angular/common';
import { Component, OnInit } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { MatCardModule } from '@angular/material/card';
import { MatChipsModule } from '@angular/material/chips';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { MatSelectModule } from '@angular/material/select';
import { AuthSessionService } from './auth-session.service';
import { InvoiceApiService } from './invoice-api.service';
import { InvoicePrinterService } from './invoice-printer.service';
import { Invoice, InvoiceFilters, InvoiceItem, Product } from './models';
import { StockApiService } from './stock-api.service';

@Component({
  selector: 'app-root',
  imports: [
    CommonModule,
    FormsModule,
    MatButtonModule,
    MatCardModule,
    MatChipsModule,
    MatFormFieldModule,
    MatInputModule,
    MatSelectModule,
  ],
  templateUrl: './app.component.html',
  styleUrl: './app.component.scss',
})
export class AppComponent implements OnInit {
  products: Product[] = [];
  invoices: Invoice[] = [];
  product = { code: '', description: '', balance: 0 };
  items: InvoiceItem[] = [];
  productCodeInvalid = false;
  productDescriptionInvalid = false;
  selectedId = 0;
  quantity = 1;
  username = '';
  password = '';
  code = '';
  challenge = '';
  message = '';
  notificationKind: 'success' | 'error' | 'info' = 'info';
  closingError = '';
  processing = false;
  expandedInvoiceNumber: number | null = null;
  invoicePage = 1;
  readonly invoicePageSize = 10;
  invoiceTotal = 0;
  invoiceFilters: InvoiceFilters = {
    status: 'Todos',
    sortBy: 'date',
    sortDirection: 'desc',
    productId: null,
  };
  activePanel: 'menu' | 'products' | 'new-invoice' | 'invoices' = 'menu';
  private notificationTimer?: ReturnType<typeof setTimeout>;

  constructor(
    private authSession: AuthSessionService,
    private stockApi: StockApiService,
    private invoiceApi: InvoiceApiService,
    private invoicePrinter: InvoicePrinterService,
  ) {}

  get token() {
    return this.authSession.token;
  }
  get availableProducts() {
    return this.products.filter((product) => product.balance > 0);
  }
  get outOfStockProducts() {
    return this.products.filter((product) => product.balance === 0);
  }
  get invoiceTotalPages() {
    return Math.max(1, Math.ceil(this.invoiceTotal / this.invoicePageSize));
  }

  ngOnInit() {
    this.loadProducts();
    if (this.authSession.restore()) this.loadInvoices();
  }
  loadProducts() {
    this.stockApi.list().subscribe({
      next: (products) => (this.products = products),
      error: () =>
        this.notify(
          'Não foi possível conectar ao serviço de estoque.',
          'error',
        ),
    });
  }

  addProduct() {
    this.productCodeInvalid = !/^\d+$/.test(this.product.code.trim());
    this.productDescriptionInvalid = !/^[\p{L}]+(?:\s+[\p{L}]+)*$/u.test(
      this.product.description.trim(),
    );
    if (
      this.productCodeInvalid ||
      this.productDescriptionInvalid ||
      this.product.balance <= 0
    ) {
      const reasons = [
        this.productCodeInvalid ? 'o código deve conter apenas números' : '',
        this.productDescriptionInvalid
          ? 'a descrição deve conter apenas letras'
          : '',
        this.product.balance <= 0 ? 'o saldo deve ser maior que zero' : '',
      ].filter(Boolean);
      this.notify(
        `Não foi possível salvar o produto: ${reasons.join('; ')}.`,
        'error',
      );
      return;
    }
    this.stockApi.create(this.product).subscribe({
      next: (result) => {
        this.product = { code: '', description: '', balance: 0 };
        this.productCodeInvalid = false;
        this.productDescriptionInvalid = false;
        this.notify(
          result.message || 'Produto cadastrado com sucesso.',
          'success',
        );
        this.loadProducts();
      },
      error: (error) => {
        if (this.handleUnauthorized(error)) return;
        const reason =
          error.error?.message ||
          error.error?.errors?.product?.[0] ||
          'ocorreu um erro inesperado';
        this.notify(`Não foi possível salvar o produto: ${reason}`, 'error');
      },
    });
  }

  login() {
    this.authSession.login(this.username, this.password).subscribe({
      next: (result) => {
        this.challenge = result.challenge;
        this.notify(
          'Senha validada. Informe o código do seu autenticador.',
          'info',
        );
      },
      error: (error) =>
        this.notify(
          error.status === 429
            ? 'Muitas tentativas. Aguarde alguns minutos.'
            : 'Credenciais inválidas.',
          'error',
        ),
    });
  }
  validateMfa() {
    this.authSession.validateMfa(this.challenge, this.code).subscribe({
      next: () => {
        this.notify('Acesso protegido liberado.', 'success');
        this.loadInvoices();
      },
      error: (error) => {
        if (error.status === 429) this.challenge = '';
        this.notify(
          error.status === 429
            ? 'Limite de tentativas atingido. Inicie o login novamente em alguns minutos.'
            : 'Código MFA inválido ou expirado.',
          'error',
        );
      },
    });
  }
  logout() {
    this.authSession.logout().subscribe({
      next: () => this.finishLogout(true),
      error: () => this.finishLogout(false),
    });
  }
  finishLogout(serverSessionRemoved: boolean) {
    this.authSession.clear();
    this.challenge = '';
    this.code = '';
    this.invoices = [];
    this.activePanel = 'menu';
    this.notify(
      serverSessionRemoved
        ? 'Sessão encerrada com segurança.'
        : 'A sessão local foi encerrada, mas não foi possível invalidar o token no servidor.',
      serverSessionRemoved ? 'success' : 'error',
    );
  }
  sanitizeProductCode(value: string) {
    const sanitized = value.replace(/\D/g, '');
    this.productCodeInvalid = value !== sanitized;
    this.product.code = sanitized;
  }
  sanitizeProductDescription(value: string) {
    const sanitized = value.replace(/[^\p{L}\s]/gu, '');
    this.productDescriptionInvalid = value !== sanitized;
    this.product.description = sanitized;
  }

  addItem() {
    if (!this.selectedId) {
      this.notify('Selecione um produto para adicionar à nota.', 'error');
      return;
    }
    if (!Number.isInteger(this.quantity) || this.quantity <= 0) {
      this.notify(
        'Informe uma quantidade válida, maior que zero, para adicionar o produto à nota.',
        'error',
      );
      return;
    }
    const existing = this.items.find(
      (item) => item.productId === this.selectedId,
    );
    if (existing) existing.quantity += this.quantity;
    else
      this.items.push({ productId: this.selectedId, quantity: this.quantity });
    this.selectedId = 0;
    this.quantity = 1;
    this.notify(
      existing
        ? 'Quantidade do item atualizada na nota.'
        : 'Item adicionado à nota.',
      'success',
    );
  }

  removeItem(productId: number) {
    this.items = this.items.filter((item) => item.productId !== productId);
    this.notify('Item removido da nota.', 'success');
  }
  createInvoice() {
    this.invoiceApi.create(this.items).subscribe({
      next: (invoice) => {
        this.items = [];
        this.notify(
          `Nota fiscal #${invoice.number} criada com sucesso.`,
          'success',
        );
        this.loadInvoices(1);
      },
      error: (error) => {
        if (!this.handleUnauthorized(error))
          this.notify(
            `Não foi possível criar a nota: ${error.error?.message || error.error?.detail || 'ocorreu um erro inesperado'}`,
            'error',
          );
      },
    });
  }

  loadInvoices(page = this.invoicePage) {
    this.invoiceApi
      .list(page, this.invoicePageSize, {
        ...this.invoiceFilters,
      })
      .subscribe({
        next: (result) => {
          this.invoices = result.items;
          this.invoiceTotal = result.total;
          this.invoicePage = result.page;
          this.expandedInvoiceNumber = null;
        },
        error: (error) => {
          if (!this.handleUnauthorized(error))
            this.notify(
              'Não foi possível carregar as notas fiscais. Verifique se o serviço de faturamento está ativo.',
              'error',
            );
        },
      });
  }

  applyInvoiceFilters() {
    this.loadInvoices(1);
  }

  clearInvoiceFilters() {
    this.invoiceFilters = {
      status: 'Todos',
      sortBy: 'date',
      sortDirection: 'desc',
      productId: null,
    };
    this.loadInvoices(1);
  }

  formatInvoiceDate(value: string) {
    return new Date(value).toLocaleString('pt-BR');
  }

  changeInvoicePage(page: number) {
    if (
      page >= 1 &&
      page <= this.invoiceTotalPages &&
      page !== this.invoicePage
    )
      this.loadInvoices(page);
  }
  toggleInvoice(number: number) {
    this.expandedInvoiceNumber =
      this.expandedInvoiceNumber === number ? null : number;
  }
  invoiceAction(invoice: Invoice) {
    if (invoice.status === 'Aberta') this.closeInvoice(invoice);
    else if (!this.invoicePrinter.print(invoice, this.products))
      this.notify(
        'Não foi possível abrir a impressão. Verifique se o navegador bloqueou a janela pop-up.',
        'error',
      );
  }
  closeInvoice(invoice: Invoice) {
    this.processing = true;
    this.closingError = '';
    this.invoiceApi.close(invoice.number).subscribe({
      next: () => {
        this.processing = false;
        this.notify(
          `Nota fiscal #${invoice.number} fechada com sucesso.`,
          'success',
        );
        this.loadInvoices();
        this.loadProducts();
      },
      error: (error) => {
        this.processing = false;
        if (!this.handleUnauthorized(error))
          this.closingError =
            error.error?.detail ||
            error.error?.message ||
            'Não foi possível fechar a nota. Tente novamente.';
      },
    });
  }

  notify(message: string, kind: 'success' | 'error' | 'info') {
    window.clearTimeout(this.notificationTimer);
    this.message = message;
    this.notificationKind = kind;
    this.notificationTimer = window.setTimeout(() => (this.message = ''), 5000);
  }
  handleUnauthorized(error: any) {
    if (error.status !== 401) return false;
    this.authSession.clear();
    this.invoices = [];
    this.activePanel = 'menu';
    this.notify('Sua sessão expirou. Entre novamente para continuar.', 'error');
    return true;
  }
  stockBalanceClass(balance: number) {
    if (balance === 0) return 'stock-empty';
    if (balance <= 5) return 'stock-critical';
    if (balance <= 10) return 'stock-warning';
    return 'stock-available';
  }
  productName(id: number) {
    return (
      this.products.find((product) => product.id === id)?.description ||
      `Produto #${id}`
    );
  }
}
