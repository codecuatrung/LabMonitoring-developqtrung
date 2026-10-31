import { ComputerStatus } from './facilities.models';
import { CommandResultStatus } from './commands.models';

/** Bootstrap contextual class for a computer status. */
export function statusClass(status: ComputerStatus): string {
  switch (status) {
    case 'Online':
      return 'text-bg-success';
    case 'InUse':
      return 'text-bg-primary';
    case 'Warning':
      return 'text-bg-warning';
    default:
      return 'text-bg-secondary';
  }
}

export function statusLabel(status: ComputerStatus): string {
  switch (status) {
    case 'Online':
      return 'Online';
    case 'InUse':
      return 'Đang dùng';
    case 'Warning':
      return 'Cảnh báo';
    default:
      return 'Offline';
  }
}

/** Bootstrap contextual class for a command result status (SRS FR-4.5). */
export function commandStatusClass(status: CommandResultStatus): string {
  switch (status) {
    case 'Executed':
      return 'text-bg-success';
    case 'Acknowledged':
      return 'text-bg-primary';
    case 'Sent':
      return 'text-bg-secondary';
    case 'Failed':
    case 'Timeout':
      return 'text-bg-danger';
  }
}

export function commandStatusLabel(status: CommandResultStatus): string {
  switch (status) {
    case 'Executed':
      return 'Đã thực thi';
    case 'Acknowledged':
      return 'Đã nhận';
    case 'Sent':
      return 'Đã gửi';
    case 'Failed':
      return 'Thất bại';
    case 'Timeout':
      return 'Hết thời gian';
  }
}

/** Vietnamese label for a command kind, used on buttons and history rows. */
export function commandKindLabel(kind: string): string {
  switch (kind) {
    case 'Lock':
      return 'Khóa máy';
    case 'Restart':
      return 'Khởi động lại';
    case 'Shutdown':
      return 'Tắt máy';
    case 'Message':
      return 'Gửi thông báo';
    case 'OpenApp':
      return 'Mở ứng dụng';
    case 'OpenUrl':
      return 'Mở website';
    case 'UpdateAgent':
      return 'Cập nhật Agent';
    case 'StartPresentation':
      return 'Bắt đầu trình chiếu';
    case 'StopPresentation':
      return 'Kết thúc trình chiếu';
    case 'PushFile':
      return 'Gửi file';
    case 'CollectFiles':
      return 'Thu file';
    default:
      return kind;
  }
}
