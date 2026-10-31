import { Routes } from '@angular/router';
import { authGuard } from './core/auth/auth.guard';
import { screenGuard } from './core/auth/screen.guard';
import { Shell } from './layout/shell/shell';

export const routes: Routes = [
  {
    path: '',
    component: Shell,
    canActivate: [authGuard],
    children: [
      { path: '', pathMatch: 'full', redirectTo: 'dashboard' },
      {
        path: 'no-access',
        loadComponent: () => import('./features/no-access/no-access').then((m) => m.NoAccessComponent),
      },
      {
        path: 'dashboard',
        canActivate: [screenGuard],
        data: { screenCode: 'dashboard' },
        loadComponent: () => import('./features/dashboard/dashboard').then((m) => m.Dashboard),
      },
      {
        path: 'rooms',
        canActivate: [screenGuard],
        data: { screenCode: 'rooms' },
        loadComponent: () => import('./features/rooms/rooms-list/rooms-list').then((m) => m.RoomsList),
      },
      {
        path: 'rooms/:id',
        canActivate: [screenGuard],
        data: { screenCode: 'rooms' },
        loadComponent: () =>
          import('./features/rooms/room-detail/room-detail').then((m) => m.RoomDetailComponent),
      },
      {
        path: 'computers/:id',
        canActivate: [screenGuard],
        data: { screenCode: 'rooms' },
        loadComponent: () =>
          import('./features/computers/computer-detail/computer-detail').then((m) => m.ComputerDetailComponent),
      },
      {
        path: 'catalog',
        canActivate: [screenGuard],
        data: { screenCode: 'catalog' },
        loadComponent: () => import('./features/catalog/catalog').then((m) => m.Catalog),
      },
      {
        path: 'classes',
        canActivate: [screenGuard],
        data: { screenCode: 'classes' },
        loadComponent: () => import('./features/classes/classes-list/classes-list').then((m) => m.ClassesList),
      },
      {
        path: 'classes/:id',
        canActivate: [screenGuard],
        data: { screenCode: 'classes' },
        loadComponent: () => import('./features/classes/class-detail/class-detail').then((m) => m.ClassDetailComponent),
      },
      {
        path: 'sessions',
        canActivate: [screenGuard],
        data: { screenCode: 'sessions' },
        loadComponent: () => import('./features/sessions/sessions-list/sessions-list').then((m) => m.SessionsList),
      },
      {
        path: 'sessions/:id/report',
        canActivate: [screenGuard],
        data: { screenCode: 'sessions' },
        loadComponent: () =>
          import('./features/sessions/session-report/session-report').then((m) => m.SessionReportComponent),
      },
      {
        path: 'sessions/:id',
        canActivate: [screenGuard],
        data: { screenCode: 'sessions' },
        loadComponent: () =>
          import('./features/sessions/session-detail/session-detail').then((m) => m.SessionDetailComponent),
      },
      {
        path: 'reports',
        canActivate: [screenGuard],
        data: { screenCode: 'reports.usage' },
        loadComponent: () => import('./features/reports/usage-report/usage-report').then((m) => m.UsageReportComponent),
      },
      {
        path: 'users',
        canActivate: [screenGuard],
        data: { screenCode: 'users' },
        loadComponent: () => import('./features/users/users-list/users-list').then((m) => m.UsersListComponent),
      },
      {
        path: 'roles',
        canActivate: [screenGuard],
        data: { screenCode: 'roles' },
        loadComponent: () => import('./features/roles/roles-list/roles-list').then((m) => m.RolesListComponent),
      },
      {
        path: 'screens',
        canActivate: [screenGuard],
        data: { screenCode: 'screens' },
        loadComponent: () => import('./features/screens/screens-list/screens-list').then((m) => m.ScreensListComponent),
      },
      {
        path: 'audit-logs',
        canActivate: [screenGuard],
        data: { screenCode: 'audit-logs' },
        loadComponent: () =>
          import('./features/audit-logs/audit-logs-list/audit-logs-list').then((m) => m.AuditLogsListComponent),
      },
      {
        path: 'file-transfer',
        canActivate: [screenGuard],
        data: { screenCode: 'file-transfer' },
        loadComponent: () =>
          import('./features/file-transfer/file-transfer').then((m) => m.FileTransferComponent),
      },
      {
        path: 'agent-updates',
        canActivate: [screenGuard],
        data: { screenCode: 'agent-updates' },
        loadComponent: () =>
          import('./features/system/agent-updates-list/agent-updates-list').then((m) => m.AgentUpdatesListComponent),
      },
    ],
  },
  { path: '**', redirectTo: '' },
];
