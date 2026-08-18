import { Injectable } from '@angular/core';
import { Invoice, Product } from './models';

@Injectable({ providedIn: 'root' })
export class InvoicePrinterService {
  print(invoice: Invoice, products: Product[]) {
    const printWindow = window.open('', '_blank', 'width=800,height=900');
    if (!printWindow) return false;
    const productName = (id: number) =>
      products.find((product) => product.id === id)?.description ||
      `Produto #${id}`;
    const rows = invoice.items
      .map(
        (item) =>
          `<tr><td>${this.escape(productName(item.productId))}</td><td>${item.quantity}</td></tr>`,
      )
      .join('');
    const createdAt = invoice.createdAt
      ? new Date(invoice.createdAt).toLocaleString('pt-BR')
      : 'Não informado';
    printWindow.document.write(
      `<!doctype html><html lang="pt-BR"><head><meta charset="utf-8"><title>Nota fiscal #${invoice.number}</title><style>body{font-family:Arial,sans-serif;color:#172033;margin:40px}header{border-bottom:2px solid #1d4a5c;margin-bottom:28px;padding-bottom:16px}h1{margin:0;color:#1d4a5c}p{margin:6px 0}table{border-collapse:collapse;width:100%;margin-top:24px}th,td{border:1px solid #cbd5e1;padding:10px;text-align:left}th{background:#edf3f7}@media print{body{margin:20px}}</style></head><body><header><h1>KORP</h1><p>Documento de nota fiscal</p></header><p><strong>Número:</strong> #${invoice.number}</p><p><strong>Status:</strong> ${this.escape(invoice.status)}</p><p><strong>Criada em:</strong> ${createdAt}</p><table><thead><tr><th>Produto</th><th>Quantidade</th></tr></thead><tbody>${rows}</tbody></table></body></html>`,
    );
    printWindow.document.close();
    printWindow.focus();
    window.setTimeout(() => printWindow.print(), 250);
    return true;
  }

  private escape(value: string) {
    return value.replace(
      /[&<>'"]/g,
      (character) =>
        ({
          '&': '&amp;',
          '<': '&lt;',
          '>': '&gt;',
          "'": '&#39;',
          '"': '&quot;',
        })[character]!,
    );
  }
}
