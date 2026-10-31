import { Component, OnInit, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { RouterLink } from '@angular/router';
import { BuildingInput, FacilitiesService, RoomInput } from '../../core/api/facilities.service';
import { PageTitleService } from '../../core/layout/page-title.service';
import { Building, Room } from '../../core/models/facilities.models';
import { PaginationComponent } from '../shared/pagination/pagination';

@Component({
  selector: 'app-catalog',
  imports: [FormsModule, RouterLink, PaginationComponent],
  templateUrl: './catalog.html',
})
export class Catalog implements OnInit {
  private readonly facilities = inject(FacilitiesService);
  private readonly pageTitle = inject(PageTitleService);

  readonly buildings = signal<Building[]>([]);
  readonly buildingsTotal = signal(0);
  buildingsPage = 1;
  buildingsPageSize = 20;

  readonly rooms = signal<Room[]>([]);
  readonly roomsTotal = signal(0);
  roomsPage = 1;
  roomsPageSize = 20;

  /** Full, unpaginated building list for the "new room" form's parent picker. */
  readonly allBuildings = signal<Building[]>([]);

  readonly error = signal<string | null>(null);

  newBuilding: BuildingInput = { name: '', code: '' };
  newRoom: RoomInput = { buildingId: 0, name: '', code: '', capacity: 30, mapImageUrl: null };

  ngOnInit(): void {
    this.pageTitle.set('Quản lý danh mục');
    this.loadBuildings();
    this.loadRooms();
    this.facilities.buildings({ pageSize: 500 }).subscribe({ next: (r) => this.allBuildings.set(r.items) });
  }

  loadBuildings(): void {
    this.facilities.buildings({ page: this.buildingsPage, pageSize: this.buildingsPageSize }).subscribe({
      next: (r) => {
        this.buildings.set(r.items);
        this.buildingsTotal.set(r.totalCount);
      },
      error: (e) => this.fail(e),
    });
  }

  loadRooms(): void {
    this.facilities.rooms({ page: this.roomsPage, pageSize: this.roomsPageSize }).subscribe({
      next: (r) => {
        this.rooms.set(r.items);
        this.roomsTotal.set(r.totalCount);
      },
      error: (e) => this.fail(e),
    });
  }

  onBuildingsPageChange(page: number): void {
    this.buildingsPage = page;
    this.loadBuildings();
  }

  onBuildingsPageSizeChange(pageSize: number): void {
    this.buildingsPageSize = pageSize;
    this.buildingsPage = 1;
    this.loadBuildings();
  }

  onRoomsPageChange(page: number): void {
    this.roomsPage = page;
    this.loadRooms();
  }

  onRoomsPageSizeChange(pageSize: number): void {
    this.roomsPageSize = pageSize;
    this.roomsPage = 1;
    this.loadRooms();
  }

  private reloadAll(): void {
    this.loadBuildings();
    this.loadRooms();
    this.facilities.buildings({ pageSize: 500 }).subscribe({ next: (r) => this.allBuildings.set(r.items) });
  }

  addBuilding(): void {
    this.facilities.createBuilding(this.newBuilding).subscribe({
      next: () => {
        this.newBuilding = { name: '', code: '' };
        this.reloadAll();
      },
      error: (e) => this.fail(e),
    });
  }

  deleteBuilding(b: Building): void {
    this.facilities.deleteBuilding(b.id).subscribe({ next: () => this.reloadAll(), error: (e) => this.fail(e) });
  }

  addRoom(): void {
    this.facilities.createRoom(this.newRoom).subscribe({
      next: () => {
        this.newRoom = { buildingId: this.newRoom.buildingId, name: '', code: '', capacity: 30, mapImageUrl: null };
        this.reloadAll();
      },
      error: (e) => this.fail(e),
    });
  }

  deleteRoom(r: Room): void {
    this.facilities.deleteRoom(r.id).subscribe({ next: () => this.reloadAll(), error: (e) => this.fail(e) });
  }

  readonly confirmRegenerateId = signal(0);

  requestRegenerateCode(r: Room): void {
    this.confirmRegenerateId.set(r.id);
  }

  cancelRegenerateCode(): void {
    this.confirmRegenerateId.set(0);
  }

  confirmRegenerateCode(r: Room): void {
    this.facilities.regenerateRoomCode(r.id).subscribe({
      next: () => {
        this.confirmRegenerateId.set(0);
        this.loadRooms();
      },
      error: (e) => this.fail(e),
    });
  }

  // ---- edit modals (buildings and rooms) ----
  // A room's code doubles as the Agents' registration secret (see "Tạo mã đăng ký mới"), so it is shown but not
  // editable here; a room can't be moved to another building either (the API has no such operation).

  readonly buildingEditor = signal<Building | null>(null);
  buildingForm = { name: '', code: '' };
  readonly roomEditor = signal<Room | null>(null);
  roomForm = { name: '', capacity: 30 };
  readonly modalError = signal<string | null>(null);
  readonly saving = signal(false);

  openEditBuilding(b: Building): void {
    this.buildingForm = { name: b.name, code: b.code };
    this.modalError.set(null);
    this.buildingEditor.set(b);
  }

  closeBuildingEditor(): void {
    this.buildingEditor.set(null);
  }

  saveBuilding(): void {
    const b = this.buildingEditor();
    const name = this.buildingForm.name.trim();
    const code = this.buildingForm.code.trim();
    if (!b || !name || !code) return;

    this.saving.set(true);
    this.facilities.updateBuilding(b.id, { name, code }).subscribe({
      next: () => {
        this.saving.set(false);
        this.buildingEditor.set(null);
        this.reloadAll();
      },
      error: (e) => {
        this.saving.set(false);
        this.modalFail(e);
      },
    });
  }

  openEditRoom(r: Room): void {
    this.roomForm = { name: r.name, capacity: r.capacity };
    this.modalError.set(null);
    this.roomEditor.set(r);
  }

  closeRoomEditor(): void {
    this.roomEditor.set(null);
  }

  saveRoom(): void {
    const r = this.roomEditor();
    const name = this.roomForm.name.trim();
    if (!r || !name || !(this.roomForm.capacity >= 1)) return;

    this.saving.set(true);
    // Code and map image are sent back unchanged: the update endpoint replaces every field.
    this.facilities
      .updateRoom(r.id, { buildingId: r.buildingId, name, code: r.code, capacity: this.roomForm.capacity, mapImageUrl: r.mapImageUrl })
      .subscribe({
        next: () => {
          this.saving.set(false);
          this.roomEditor.set(null);
          this.reloadAll();
        },
        error: (e) => {
          this.saving.set(false);
          this.modalFail(e);
        },
      });
  }

  private modalFail(e: unknown): void {
    const err = e as { error?: { detail?: string; errors?: Record<string, string[]> }; message?: string };
    const validation = err?.error?.errors ? Object.values(err.error.errors).flat().join(' ') : null;
    this.modalError.set(validation ?? err?.error?.detail ?? err?.message ?? 'Không lưu được thay đổi');
  }

  private fail(e: unknown): void {
    const err = e as { error?: { detail?: string }; message?: string };
    this.error.set(err?.error?.detail ?? err?.message ?? 'Thao tác thất bại');
  }
}
