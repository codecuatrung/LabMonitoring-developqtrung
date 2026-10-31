import { Component, OnInit, inject } from '@angular/core';
import { PageTitleService } from '../../core/layout/page-title.service';

/** Landing page for a signed-in user with zero accessible screens (e.g. freshly JIT-provisioned,
 * no role assigned yet) — `screenGuard` redirects here instead of `/dashboard` when blocked, since
 * `/dashboard` itself requires a screen permission and would just bounce back to this same guard. */
@Component({
  selector: 'app-no-access',
  templateUrl: './no-access.html',
})
export class NoAccessComponent implements OnInit {
  private readonly pageTitle = inject(PageTitleService);

  ngOnInit(): void {
    this.pageTitle.set('Không có quyền truy cập');
  }
}
