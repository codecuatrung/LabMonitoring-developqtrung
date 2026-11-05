import type { ReactNode } from "react";
import {
  AppstoreOutlined,
  CalendarOutlined,
  ClockCircleOutlined,
  EnvironmentOutlined,
  FileTextOutlined,
  SafetyCertificateOutlined,
  TeamOutlined,
  UserOutlined,
} from "@ant-design/icons";
import type { Collection, Entity } from "../store.ts";

export type Screen =
  | "overview"
  | "schedule"
  | "myCalendar"
  | "duties"
  | "templates"
  | "people"
  | "rooms"
  | "reports"
  | "admin";

export type RowData = Entity & {
  name?: string;
  date?: string;
  start?: string;
  end?: string;
  teacher?: string;
  className?: string;
  subject?: string;
  room?: string;
  status?: string;
  assignee?: string;
  role?: string;
};

export const days = ["Thứ 2", "Thứ 3", "Thứ 4", "Thứ 5", "Thứ 6"];

export type NavigationItem = {
  key: Screen;
  label: string;
  description: string;
  icon: ReactNode;
  badge?: number;
};

export const navigation: NavigationItem[] = [
  {
    key: "overview",
    label: "Tổng quan",
    description: "Hoạt động và việc cần xử lý",
    icon: <AppstoreOutlined />,
  },
  {
    key: "schedule",
    label: "Lịch giảng dạy",
    description: "Tra cứu, xếp lịch và kiểm tra trùng",
    icon: <CalendarOutlined />,
  },
  {
    key: "myCalendar",
    label: "Lịch cá nhân",
    description: "Lịch dạy và ca trực của bạn",
    icon: <UserOutlined />,
  },
  {
    key: "duties",
    label: "Quản lý ca trực",
    description: "Phân công, đổi ca và theo dõi",
    icon: <ClockCircleOutlined />,
  },
  {
    key: "templates",
    label: "Mẫu ca trực",
    description: "Thiết lập khung giờ dùng lại",
    icon: <ClockCircleOutlined />,
  },
  {
    key: "people",
    label: "Giáo viên & lớp học",
    description: "Quản lý hồ sơ và lớp",
    icon: <TeamOutlined />,
  },
  {
    key: "rooms",
    label: "Phòng & thiết bị",
    description: "Danh mục và tình trạng phòng",
    icon: <EnvironmentOutlined />,
  },
  {
    key: "reports",
    label: "Báo cáo",
    description: "Thống kê giờ dạy, ca và phòng",
    icon: <FileTextOutlined />,
  },
  {
    key: "admin",
    label: "Quản trị & phân quyền",
    description: "Tài khoản, vai trò và quyền",
    icon: <SafetyCertificateOutlined />,
  },
];

export const titles: Record<Screen, string> = {
  overview: "Tổng quan vận hành",
  schedule: "Lịch giảng dạy",
  myCalendar: "Lịch cá nhân",
  duties: "Quản lý ca trực",
  templates: "Mẫu ca trực",
  people: "Giáo viên & lớp học",
  rooms: "Phòng & thiết bị",
  reports: "Báo cáo và thống kê",
  admin: "Quản trị hệ thống",
};

export const permissionMap: Record<string, string[]> = {
  "Quản trị hệ thống": [
    "Toàn quyền",
    "Toàn quyền",
    "Toàn quyền",
    "Toàn quyền",
    "Toàn bộ",
  ],
  "Điều phối/giáo vụ": [
    "Xem/sửa theo phân công",
    "Toàn quyền nghiệp vụ",
    "Toàn quyền nghiệp vụ",
    "Duyệt yêu cầu",
    "Theo phạm vi",
  ],
  "Giáo viên": [
    "Xem hồ sơ mình",
    "Gửi yêu cầu",
    "Không tự phân công",
    "Tạo yêu cầu",
    "Cá nhân mình",
  ],
  "Kỹ thuật viên": [
    "Không",
    "Xem",
    "Xem ca liên quan",
    "Không",
    "Theo phạm vi",
  ],
  "Quản lý đơn vị": ["Xem", "Xem", "Xem", "Xem", "Toàn bộ theo đơn vị"],
};

export const permissionDefinitions = [
  {
    key: "teachers.read",
    label: "Xem hồ sơ giáo viên",
    group: "Hồ sơ & danh mục",
  },
  {
    key: "teachers.write",
    label: "Tạo và cập nhật hồ sơ giáo viên",
    group: "Hồ sơ & danh mục",
  },
  { key: "classes.read", label: "Xem lớp học", group: "Hồ sơ & danh mục" },
  { key: "classes.write", label: "Quản lý lớp học", group: "Hồ sơ & danh mục" },
  { key: "subjects.read", label: "Xem môn học", group: "Hồ sơ & danh mục" },
  {
    key: "subjects.write",
    label: "Quản lý môn học",
    group: "Hồ sơ & danh mục",
  },
  {
    key: "rooms.read",
    label: "Xem phòng và thiết bị",
    group: "Vận hành phòng",
  },
  {
    key: "rooms.write",
    label: "Cập nhật phòng và thiết bị",
    group: "Vận hành phòng",
  },
  {
    key: "schedules.read",
    label: "Xem lịch giảng dạy",
    group: "Lịch giảng dạy",
  },
  {
    key: "schedules.write",
    label: "Tạo, sửa, hủy lịch",
    group: "Lịch giảng dạy",
  },
  { key: "duties.read", label: "Xem ca trực", group: "Ca trực" },
  { key: "duties.write", label: "Tạo và điều chỉnh ca trực", group: "Ca trực" },
  { key: "duties.assign", label: "Phân công người trực", group: "Ca trực" },
  { key: "duties.approve", label: "Duyệt yêu cầu đổi ca", group: "Ca trực" },
  { key: "duties.swapRequest", label: "Gửi yêu cầu đổi ca", group: "Ca trực" },
  { key: "reports.read", label: "Xem và xuất báo cáo", group: "Báo cáo" },
  {
    key: "users.write",
    label: "Quản lý tài khoản và vai trò",
    group: "Quản trị",
  },
];

export const emptyRows = (): Record<Collection, RowData[]> => ({
  teachers: [],
  classes: [],
  rooms: [],
  equipment: [],
  subjects: [],
  timeSlots: [],
  schedules: [],
  sessions: [],
  shifts: [],
  requests: [],
  users: [],
  permissions: [],
  dutyTemplates: [],
  academicTerms: [],
  notifications: [],
  auditLogs: [],
});
