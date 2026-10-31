import { Component, EventEmitter, Input, Output } from '@angular/core';
import { FormsModule } from '@angular/forms';

/**
 * Page-size selector + numbered page buttons, styled after the CoreProject/BSC table template
 * ("Hiển thị X bản ghi/trang" + « 1 2 3 … N »). Reused by every server-paginated list page so the
 * pagination UI/behavior stays identical across the app.
 */
@Component({
  selector: 'app-pagination',
  imports: [FormsModule],
  templateUrl: './pagination.html',
})
export class PaginationComponent {
  @Input({ required: true }) page = 1;
  @Input({ required: true }) pageSize = 20;
  @Input({ required: true }) totalCount = 0;
  @Input() pageSizeOptions: number[] = [10, 20, 50, 100];

  @Output() pageChange = new EventEmitter<number>();
  @Output() pageSizeChange = new EventEmitter<number>();

  totalPages(): number {
    return Math.max(1, Math.ceil(this.totalCount / this.pageSize));
  }

  /** Page numbers to render, with -1 standing in for an ellipsis gap. */
  pageNumbers(): number[] {
    const total = this.totalPages();
    const current = Math.min(Math.max(this.page, 1), total);
    const keep = new Set<number>([1, total]);
    for (let p = current - 2; p <= current + 2; p++) {
      if (p >= 1 && p <= total) keep.add(p);
    }
    const sorted = [...keep].sort((a, b) => a - b);

    const result: number[] = [];
    let prev = 0;
    for (const p of sorted) {
      if (prev && p - prev > 1) result.push(-1);
      result.push(p);
      prev = p;
    }
    return result;
  }

  goTo(target: number): void {
    const clamped = Math.max(1, Math.min(target, this.totalPages()));
    if (clamped !== this.page) this.pageChange.emit(clamped);
  }

  onPageSizeChange(size: number): void {
    if (size !== this.pageSize) this.pageSizeChange.emit(size);
  }
}
