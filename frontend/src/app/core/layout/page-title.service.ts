import { Injectable, signal } from '@angular/core';

/** The current page's title, shown in the Shell header bar instead of each page rendering its own
 * <h1>. Routed page components call `set()` — statically in ngOnInit for a fixed title, or once
 * their data loads for a dynamic one (room name, class name, ...). */
@Injectable({ providedIn: 'root' })
export class PageTitleService {
  private readonly _title = signal('');
  readonly title = this._title.asReadonly();

  set(title: string): void {
    this._title.set(title);
  }
}
