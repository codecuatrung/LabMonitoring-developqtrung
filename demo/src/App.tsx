import { startTransition, useEffect, useMemo, useRef, useState } from "react";
import {
  App as AntApp,
  Avatar,
  Badge,
  Button,
  Card,
  Col,
  DatePicker,
  Form,
  Input,
  Layout,
  Modal,
  Progress,
  Row,
  Select,
  Space,
  Statistic,
  Table,
  Tabs,
  Tag,
  Typography,
  message,
} from "antd";
import type { ColumnsType } from "antd/es/table";
import {
  AlertOutlined,
  BellOutlined,
  BookOutlined,
  CalendarOutlined,
  CheckCircleOutlined,
  ClockCircleOutlined,
  DatabaseOutlined,
  DownloadOutlined,
  EnvironmentOutlined,
  MenuOutlined,
  PlusOutlined,
  ReloadOutlined,
  SearchOutlined,
  SafetyCertificateOutlined,
  TeamOutlined,
  WarningOutlined,
} from "@ant-design/icons";
import dayjs from "dayjs";
import {
  openDatabase,
  readRecords,
  removeRecord,
  resetDatabase,
  saveRecord,
  type Collection,
  type Entity,
} from "./store.ts";
import {
  days,
  emptyRows,
  navigation,
  permissionDefinitions,
  permissionMap,
  titles,
  type RowData,
  type Screen,
} from "./app/model.tsx";
import { AppSidebar } from "./app/AppSidebar.tsx";
import { RecordEditorModal } from "./app/RecordEditorModal.tsx";
import { AdminScreen } from "./app/AdminScreen.tsx";
import "./App.css";

function recurrenceDates(
  startDate: string,
  endDate: string,
  repeatMode: string,
): { dates: string[]; overflow: boolean } {
  if (repeatMode === "Một lần" || !endDate) {
    return { dates: [startDate], overflow: false };
  }
  const interval = repeatMode === "Hàng tháng" ? "month" : "week";
  const maxOccurrences = interval === "month" ? 24 : 52;
  const dates = [startDate];
  const start = dayjs(startDate);
  const end = dayjs(endDate);
  for (let index = 1; index < maxOccurrences; index += 1) {
    const date = start.add(index, interval);
    if (date.isAfter(end, "day")) break;
    dates.push(date.format("YYYY-MM-DD"));
  }
  const next = start.add(dates.length, interval);
  return {
    dates,
    overflow: next.isSame(end, "day") || next.isBefore(end, "day"),
  };
}

function makeAuditEntry(
  actor: string,
  role: string,
  action: string,
  entity: string,
  entityId: string,
  details: string,
): Entity {
  const occurredAt = new Date().toISOString();
  return {
    id: `audit-${Date.now()}-${Math.random().toString(36).slice(2, 7)}`,
    action,
    entity,
    entityId,
    details,
    actor,
    role,
    occurredAt,
  };
}

function makeNotificationEntry(
  title: string,
  details: string,
  id = `notice-${Date.now()}-${Math.random().toString(36).slice(2, 7)}`,
): Entity {
  return {
    id,
    title,
    details,
    createdAt: new Date().toISOString(),
    read: false,
  };
}

const screenPaths: Record<Screen, string> = {
  overview: "/",
  schedule: "/schedule",
  myCalendar: "/my-calendar",
  duties: "/duties",
  templates: "/duty-templates",
  people: "/people",
  rooms: "/rooms",
  reports: "/reports",
  admin: "/admin",
};

function screenFromPath(pathname: string): Screen {
  const normalizedPath = pathname.replace(/\/+$/, "") || "/";
  return (
    (Object.entries(screenPaths).find(([, path]) => path === normalizedPath)?.[0] as Screen | undefined) ??
    "overview"
  );
}

function App() {
  const [screen, setScreen] = useState<Screen>(() =>
    screenFromPath(window.location.pathname),
  );
  const [rows, setRows] = useState(emptyRows);
  const [ready, setReady] = useState(false);
  const [startupError, setStartupError] = useState<string | null>(null);
  const [startupAttempt, setStartupAttempt] = useState(0);
  const [dialog, setDialog] = useState<{
    collection: Collection;
    title: string;
    record?: Partial<RowData>;
  } | null>(null);
  const [role, setRole] = useState(
    () => sessionStorage.getItem("labmonitoring:demo-role") ?? "Quản trị hệ thống",
  );
  const [mobileNavOpen, setMobileNavOpen] = useState(false);
  const [isMobile, setIsMobile] = useState(false);
  const [notificationsOpen, setNotificationsOpen] = useState(false);
  const [calendarView, setCalendarView] = useState<"week" | "month">("week");
  const [calendarAnchor, setCalendarAnchor] = useState(dayjs());
  const [calendarActivity, setCalendarActivity] = useState("Tất cả hoạt động");
  const [scheduleAnchor, setScheduleAnchor] = useState(dayjs());
  const [selectedSemester, setSelectedSemester] = useState("Học kỳ I · 2026–2027");
  const [reportMonth, setReportMonth] = useState(dayjs().format("YYYY-MM"));
  const reminderIds = useRef(new Set<string>());
  const [roleCreateOpen, setRoleCreateOpen] = useState(false);
  const [newRoleName, setNewRoleName] = useState("");
  const [query, setQuery] = useState("");
  const [form] = Form.useForm();
  const [toast, holder] = message.useMessage();
  const navigateToScreen = (nextScreen: Screen, replace = false) => {
    const path = screenPaths[nextScreen];
    startTransition(() => setScreen(nextScreen));
    if (window.location.pathname === path) return;
    const method = replace ? "replaceState" : "pushState";
    window.history[method]({ screen: nextScreen }, "", path);
  };
  const changeRole = (nextRole: string) => {
    sessionStorage.setItem("labmonitoring:demo-role", nextRole);
    setRole(nextRole);
  };
  useEffect(() => {
    const syncFromHistory = () => setScreen(screenFromPath(window.location.pathname));
    window.addEventListener("popstate", syncFromHistory);
    return () => window.removeEventListener("popstate", syncFromHistory);
  }, []);
  useEffect(() => {
    const expectedPath = screenPaths[screen];
    if (window.location.pathname !== expectedPath) {
      window.history.replaceState({ screen }, "", expectedPath);
    }
  }, [screen]);
  const refresh = () =>
    setRows({
      teachers: readRecords("teachers"),
      classes: readRecords("classes"),
      rooms: readRecords("rooms"),
      equipment: readRecords("equipment"),
      subjects: readRecords("subjects"),
      timeSlots: readRecords("timeSlots"),
      schedules: readRecords("schedules"),
      sessions: readRecords("sessions"),
      shifts: readRecords("shifts"),
      requests: readRecords("requests"),
      users: readRecords("users"),
      permissions: readRecords("permissions"),
      dutyTemplates: readRecords("dutyTemplates"),
      academicTerms: readRecords("academicTerms"),
      notifications: readRecords("notifications"),
      auditLogs: readRecords("auditLogs"),
    });
  const recordAudit = async (
    action: string,
    entity: string,
    entityId: string,
    details: string,
  ) => {
    await saveRecord(
      "auditLogs",
      makeAuditEntry(selfName, role, action, entity, entityId, details),
    );
  };
  const addNotification = async (title: string, details: string) => {
    await saveRecord("notifications", makeNotificationEntry(title, details));
  };
  const durationHours = (row: RowData) => {
    const [startHour, startMinute] = String(row.start ?? "0:0")
      .split(":")
      .map(Number);
    const [endHour, endMinute] = String(row.end ?? "0:0")
      .split(":")
      .map(Number);
    return Math.max(0, endHour * 60 + endMinute - startHour * 60 - startMinute) / 60;
  };
  useEffect(() => {
    let cancelled = false;
    openDatabase()
      .then(() => {
        if (cancelled) return;
        setRows({
          teachers: readRecords("teachers"),
          classes: readRecords("classes"),
          rooms: readRecords("rooms"),
          equipment: readRecords("equipment"),
          subjects: readRecords("subjects"),
          timeSlots: readRecords("timeSlots"),
          schedules: readRecords("schedules"),
          sessions: readRecords("sessions"),
          shifts: readRecords("shifts"),
          requests: readRecords("requests"),
          users: readRecords("users"),
          permissions: readRecords("permissions"),
          dutyTemplates: readRecords("dutyTemplates"),
          academicTerms: readRecords("academicTerms"),
          notifications: readRecords("notifications"),
          auditLogs: readRecords("auditLogs"),
        });
        setReady(true);
      })
      .catch((error: unknown) => {
        if (cancelled) return;
        const detail =
          error instanceof Error
            ? error.message
            : "Không kết nối được SQLite API server";
        setStartupError(detail);
        toast.error(detail);
      });
    return () => {
      cancelled = true;
    };
  }, [startupAttempt, toast]);
  useEffect(() => {
    const onApiError = (event: Event) =>
      toast.error((event as CustomEvent<string>).detail);
    window.addEventListener("labmonitoring:api-error", onApiError);
    return () =>
      window.removeEventListener("labmonitoring:api-error", onApiError);
  }, [toast]);
  useEffect(() => {
    const checkReminders = () => {
      const now = dayjs();
      const reminders = [
        ...rows.schedules
          .filter((item) => item.status !== "Đã hủy")
          .map((item) => ({ ...item, reminderType: "giảng dạy" })),
        ...rows.shifts
          .filter((item) => item.status !== "Đã hủy")
          .map((item) => ({ ...item, reminderType: "ca trực" })),
      ];
      for (const schedule of reminders) {
        const startsAt = dayjs(`${schedule.date}T${schedule.start}:00`);
        const minutesUntil = startsAt.diff(now, "minute");
        for (const leadMinutes of [1440, 30]) {
          const reminderId = `reminder-${schedule.id}-${leadMinutes}`;
          if (
            minutesUntil > 0 &&
            minutesUntil <= leadMinutes &&
            minutesUntil > leadMinutes - 5 &&
            !reminderIds.current.has(reminderId) &&
            !rows.notifications.some((item) => item.id === reminderId)
          ) {
            reminderIds.current.add(reminderId);
            void saveRecord("notifications", {
              ...makeNotificationEntry(
                leadMinutes === 30
                  ? `Sắp đến giờ ${schedule.reminderType}`
                  : `Nhắc ${schedule.reminderType} ngày mai`,
                `${schedule.date} ${schedule.start} · ${schedule.className ?? schedule.room} · ${schedule.room}`,
                reminderId,
              ),
            }).then(refresh);
          }
        }
      }
    };
    checkReminders();
    const timer = window.setInterval(checkReminders, 60_000);
    return () => window.clearInterval(timer);
  }, [rows.schedules, rows.shifts, rows.notifications]);
  useEffect(() => {
    const updateViewport = () =>
      setIsMobile(window.matchMedia("(max-width: 767px)").matches);
    updateViewport();
    window.addEventListener("resize", updateViewport);
    return () => window.removeEventListener("resize", updateViewport);
  }, []);
  const unassigned = rows.shifts.filter(
    (row) => row.status === "Chưa phân công",
  ).length;
  const pending = rows.requests.filter(
    (row) => row.status === "Chờ duyệt",
  ).length;
  const recordsUsed = Object.values(rows).reduce(
    (total, collection) => total + collection.length,
    0,
  );
  const scheduleWeekStart = scheduleAnchor
    .startOf("day")
    .subtract((scheduleAnchor.day() + 6) % 7, "day");
  const scheduleWeekEnd = scheduleWeekStart.add(6, "day");
  const filteredSchedules = useMemo(
    () =>
      rows.schedules.filter((row) =>
        String(row.date) >= scheduleWeekStart.format("YYYY-MM-DD") &&
        String(row.date) <= scheduleWeekEnd.format("YYYY-MM-DD") &&
        (selectedSemester === "Tất cả học kỳ" || !row.semester || row.semester === selectedSemester) &&
        `${row.teacher} ${row.className} ${row.room} ${row.subject}`
          .toLowerCase()
          .includes(query.toLowerCase()),
      ),
    [rows.schedules, query, scheduleWeekStart, scheduleWeekEnd, selectedSemester],
  );
  const hasPermission = (permission: string) =>
    role === "Quản trị hệ thống" ||
    Boolean(
      rows.permissions.find((item) => item.id === `${role}::${permission}`)
        ?.enabled,
    );
  const isTeacher = role === "Giáo viên";
  const canManage = hasPermission("schedules.write");
  const canAssign = hasPermission("duties.assign");
  const canApprove = hasPermission("duties.approve");
  const canCreateShift = hasPermission("duties.write");
  const canManageUsers = hasPermission("users.write");
  const canViewReports = hasPermission("reports.read");
  const roles = Array.from(
    new Set([
      ...Object.keys(permissionMap),
      ...rows.permissions.map((item) => String(item.role)),
    ]),
  );
  const visibleNavigation = navigation.filter((item) => {
    if (item.key === "overview") return true;
    if (item.key === "myCalendar")
      return isTeacher;
    if (item.key === "schedule") return hasPermission("schedules.read");
    if (item.key === "duties") return hasPermission("duties.read") || canAssign;
    if (item.key === "templates") return canCreateShift;
    if (item.key === "people") return hasPermission("teachers.read");
    if (item.key === "rooms") return hasPermission("rooms.read");
    if (item.key === "reports") return canViewReports && !isTeacher;
    if (item.key === "admin") return canManageUsers;
    return false;
  });
  const selfName =
    role === "Quản trị hệ thống"
      ? "Admin Demo"
      : isTeacher
        ? "Nguyễn Minh Anh"
        : role === "Kỹ thuật viên"
          ? "Vũ Khánh Linh"
          : "Phạm Đức Long";
    const teacherUnstartedShifts = isTeacher
      ? rows.shifts.filter(
          (row) =>
            String(row.assignee ?? "").split(";").includes(selfName) &&
            row.status === "Đã phân công",
        ).length
      : 0;
    const dutyBadgeCount = isTeacher
      ? teacherUnstartedShifts
      : canAssign || canApprove
        ? unassigned + pending
        : 0;
    const dutyBadgeLabel = isTeacher
      ? `${teacherUnstartedShifts} ca đã phân công chưa nhận`
      : `${unassigned} ca chưa phân công, ${pending} yêu cầu đổi ca chờ duyệt`;
    const sidebarNavigation = visibleNavigation.map((item) =>
      item.key === "duties" && isTeacher
        ? {
            ...item,
            label: "Ca trực của tôi",
            description: "Ca được giao, nhận ca và xin nghỉ",
          }
        : item,
    );
  const visibleSchedules = isTeacher
    ? filteredSchedules.filter((row) => row.teacher === selfName)
    : filteredSchedules;

  const openForm = (
    collection: Collection,
    title: string,
    record?: Partial<RowData>,
  ) => {
    setDialog({ collection, title, record });
    const formValues = { ...(record ?? {}) } as Record<string, unknown>;
    for (const key of ["date", "repeatUntil"]) {
      if (typeof formValues[key] === "string")
        formValues[key] = dayjs(String(formValues[key]), "YYYY-MM-DD");
    }
    if (collection === "schedules" && !formValues.semester) {
      formValues.semester =
        rows.academicTerms.find((term) => term.status === "Đang diễn ra")?.name ??
        rows.academicTerms[0]?.name;
    }
    for (const key of ["start", "end"]) {
      if (typeof formValues[key] === "string")
        formValues[key] = dayjs(`2000-01-01T${String(formValues[key])}:00`);
    }
    form.setFieldsValue(formValues);
  };
  const saveForm = async () => {
    const rawValues = (await form.validateFields()) as Record<string, unknown>;
    const values = Object.fromEntries(
      Object.entries(rawValues).map(([key, value]) => [
        key,
        dayjs.isDayjs(value)
          ? value.format(
              ["date", "repeatUntil"].includes(key) ? "YYYY-MM-DD" : "HH:mm",
            )
          : value,
      ]),
    ) as Record<string, string | number>;
    if (!dialog) return;
    if (
      dialog.collection === "requests" &&
      !hasPermission("duties.swapRequest")
    ) {
      toast.error("Vai trò hiện tại không được gửi yêu cầu đổi ca");
      return;
    }
    if (
      dialog.collection !== "requests" &&
      !hasPermission(
        dialog.collection === "equipment"
          ? "rooms.write"
          : dialog.collection === "timeSlots"
            ? "subjects.write"
          : dialog.collection === "academicTerms"
            ? "users.write"
            : `${dialog.collection}.write`,
      ) &&
      !(dialog.collection === "shifts" && canCreateShift)
    ) {
      toast.error("Vai trò hiện tại không có quyền cập nhật dữ liệu này");
      return;
    }
    if (dialog.collection === "schedules") {
      const start = String(values.start);
      const end = String(values.end);
      if (
        values.repeatMode === "Hàng tuần" &&
        (!values.repeatUntil || String(values.repeatUntil) < String(values.date))
      ) {
        toast.error("Lịch lặp cần ngày kết thúc sau ngày bắt đầu");
        return;
      }
      if (start >= end) {
        toast.error("Giờ kết thúc phải sau giờ bắt đầu");
        return;
      }
      const occurrenceDates: string[] = [String(values.date)];
      if (
        !dialog.record?.id &&
        values.repeatMode === "Hàng tuần" &&
        values.repeatUntil
      ) {
        const recurrence = recurrenceDates(
          String(values.date),
          String(values.repeatUntil),
          String(values.repeatMode),
        );
        if (recurrence.overflow) {
          toast.error("Lịch lặp tối đa 52 buổi; hãy chọn ngày kết thúc gần hơn");
          return;
        }
        occurrenceDates.splice(0, occurrenceDates.length, ...recurrence.dates);
      }
      const weekDays = [
        "Chủ nhật",
        "Thứ 2",
        "Thứ 3",
        "Thứ 4",
        "Thứ 5",
        "Thứ 6",
        "Thứ 7",
      ];
      const occurrences = occurrenceDates.map((date, index) => ({
        ...values,
        date,
        day: weekDays[dayjs(date).day()],
        repeatMode: undefined,
        repeatUntil: undefined,
        id:
          index === 0 && dialog.record?.id
            ? String(dialog.record.id)
            : `schedule-${Date.now()}-${index}`,
      }));
      for (const occurrence of occurrences) {
        const conflict = rows.schedules.find(
          (item) =>
            item.id !== dialog.record?.id &&
            item.date === occurrence.date &&
            item.status !== "Đã hủy" &&
            start < String(item.end) &&
            String(item.start) < end &&
            (item.teacher === values.teacher ||
              item.room === values.room ||
              item.className === values.className),
        );
        if (conflict) {
          toast.error(
            `Trùng lịch ngày ${occurrence.date} với ${conflict.teacher}, ${conflict.start}–${conflict.end}`,
          );
          return;
        }
        const dutyConflict = rows.shifts.find(
          (item) =>
            item.date === occurrence.date &&
            item.status !== "Đã hủy" &&
            start < String(item.end) &&
            String(item.start) < end &&
            (item.room === values.room ||
              String(item.assignee ?? "").split(";").includes(String(values.teacher))),
        );
        if (dutyConflict) {
          toast.error(
            `Trùng ca trực ngày ${occurrence.date}, ${dutyConflict.start}–${dutyConflict.end} tại ${dutyConflict.room}`,
          );
          return;
        }
      }
      await Promise.all(
        occurrences.map((occurrence) =>
          saveRecord("schedules", occurrence as RowData),
        ),
      );
      await recordAudit(
        dialog.record?.id ? "Cập nhật lịch" : "Tạo lịch",
        "Lịch giảng dạy",
        String(occurrences[0]?.id ?? ""),
        `Đã lưu ${occurrences.length} buổi; ${String(values.type ?? "Giảng dạy")}`,
      );
      await addNotification("Lịch giảng dạy được cập nhật", `${occurrences.length} buổi · ${String(values.className)}`);
      refresh();
      setDialog(null);
      form.resetFields();
      toast.success(`Đã lưu ${occurrences.length} buổi học`);
      return;
    }
    if (dialog.collection === "shifts") {
      const start = String(values.start);
      const end = String(values.end);
      if (
        values.repeatMode &&
        values.repeatMode !== "Một lần" &&
        (!values.repeatUntil || String(values.repeatUntil) < String(values.date))
      ) {
        toast.error("Ca lặp cần ngày kết thúc sau ngày bắt đầu");
        return;
      }
      if (start >= end) {
        toast.error("Giờ kết thúc phải sau giờ bắt đầu");
        return;
      }
      const recurrence = recurrenceDates(
        String(values.date),
        String(values.repeatUntil ?? ""),
        String(values.repeatMode ?? "Một lần"),
      );
      if (recurrence.overflow) {
        toast.error("Ca lặp tối đa 52 tuần hoặc 24 tháng; hãy chọn ngày kết thúc gần hơn");
        return;
      }
      for (const date of recurrence.dates) {
        const conflict = rows.shifts.find(
          (item) =>
            item.id !== dialog.record?.id &&
            item.date === date &&
            item.status !== "Đã hủy" &&
            start < String(item.end) &&
            String(item.start) < end &&
            item.room === values.room,
        );
        if (conflict) {
          toast.error(`Phòng đã có ca ngày ${date}, ${conflict.start}–${conflict.end}`);
          return;
        }
      }
      const occurrences = recurrence.dates.map((date, index) => ({
        ...values,
        date,
        repeatMode: undefined,
        repeatUntil: undefined,
        id: `shift-${Date.now()}-${index}`,
      }));
      await Promise.all(
        occurrences.map((occurrence) => saveRecord("shifts", occurrence as RowData)),
      );
      await recordAudit("Sinh ca trực", "Ca trực", String(occurrences[0]?.id ?? ""), `Đã tạo ${occurrences.length} ca`);
      await addNotification("Ca trực được tạo", `Đã tạo ${occurrences.length} ca trực theo mẫu lặp`);
      refresh();
      setDialog(null);
      form.resetFields();
      toast.success(`Đã tạo ${occurrences.length} ca trực`);
      return;
    }
    if (dialog.collection === "requests") {
      const shift =
        rows.shifts.find((item) => item.id === dialog.record?.shiftId) ??
        rows.shifts.find(
          (item) =>
            item.date === values.date &&
            String(dialog.record?.shift).includes(`${item.start}–${item.end}`),
        );
      const conflict =
        rows.schedules.some(
          (item) =>
            item.date === values.date &&
            item.status !== "Đã hủy" &&
            item.teacher === values.to &&
            String(shift?.start) < String(item.end) &&
            String(item.start) < String(shift?.end),
        ) ||
        rows.shifts.some(
          (item) =>
            item.id !== shift?.id &&
            item.date === values.date &&
            item.status !== "Đã hủy" &&
            String(item.assignee ?? "").split(";").includes(String(values.to)) &&
            String(shift?.start) < String(item.end) &&
            String(item.start) < String(shift?.end),
        );
      if (conflict) {
        toast.error("Người được đề xuất đang bận trong khung giờ này");
        return;
      }
    }
    const savedId = String(dialog.record?.id ?? `${dialog.collection}-${Date.now()}`);
    await saveRecord(dialog.collection, {
      ...dialog.record,
      ...values,
      id: savedId,
    });
    const changedFields = Object.entries(values)
      .filter(
        ([key, value]) =>
          !["email", "phone"].includes(key) &&
          String(dialog.record?.[key] ?? "") !== String(value ?? ""),
      )
      .map(
        ([key, value]) =>
          `${key}: ${String(dialog.record?.[key] ?? "—")} → ${String(value)}`,
      );
    await recordAudit(
      dialog.record?.id ? "Cập nhật" : "Tạo mới",
      dialog.collection,
      savedId,
      changedFields.join("; ") || String(values.name ?? values.className ?? values.date ?? ""),
    );
    if (["shifts", "requests"].includes(dialog.collection)) {
      await addNotification("Dữ liệu ca trực vừa được cập nhật", `${dialog.title} · ${String(values.date ?? "")}`);
    }
    refresh();
    setDialog(null);
    form.resetFields();
    toast.success("Đã ghi vào SQLite server RAM");
  };
  const remove = (collection: Collection, id: string) =>
    Modal.confirm({
      title: "Xóa dữ liệu này?",
      content: "Bản ghi được xóa khỏi SQLite RAM của instance đang phục vụ.",
      okText: "Xóa",
      cancelText: "Hủy",
      okButtonProps: { danger: true },
      onOk: async () => {
        await removeRecord(collection, id);
        await recordAudit("Xóa", collection, id, "Đã xóa bản ghi");
        refresh();
        toast.success("Đã xóa");
      },
    });
  const exportCsv = (data: RowData[], filename: string) => {
    if (!data.length) return toast.warning("Không có dữ liệu để xuất");
    const keys = Object.keys(data[0]);
    const csv = [
      keys.join(","),
      ...data.map((row) =>
        keys
          .map((key) => `"${String(row[key] ?? "").replaceAll('"', '""')}"`)
          .join(","),
      ),
    ].join("\n");
    const link = document.createElement("a");
    link.href = URL.createObjectURL(
      new Blob(["\ufeff" + csv], { type: "text/csv;charset=utf-8" }),
    );
    link.download = `${filename}.csv`;
    link.click();
    URL.revokeObjectURL(link.href);
  };
  const statusTag = (status?: string) => (
    <Tag
      color={
        [
          "Đã xác nhận",
          "Đã phân công",
          "Hoàn thành",
          "Đã duyệt",
          "Sẵn sàng",
          "Đang hoạt động",
        ].includes(status ?? "")
          ? "green"
          : status === "Đang diễn ra"
            ? "blue"
            : status === "Chờ duyệt" || status === "Chờ xác nhận"
              ? "gold"
              : status === "Chưa phân công" || status === "Bảo trì"
                ? "volcano"
                : "default"
      }
    >
      {status || "—"}
    </Tag>
  );
  const rowActions = (collection: Collection, row: RowData) =>
    hasPermission(
      collection === "equipment"
        ? "rooms.write"
        : collection === "timeSlots"
          ? "subjects.write"
        : collection === "academicTerms"
          ? "users.write"
          : `${collection}.write`,
    ) ||
    (collection === "users" && hasPermission("users.write")) ||
    (collection === "shifts" && hasPermission("duties.write")) ? (
      <Space size={2}>
        <Button
          type="link"
          size="small"
          onClick={() => openForm(collection, "Cập nhật thông tin", row)}
        >
          Sửa
        </Button>
        <Button
          type="link"
          danger
          size="small"
          onClick={() => remove(collection, String(row.id))}
        >
          Xóa
        </Button>
      </Space>
    ) : null;
  const assign = (row: RowData) => {
    if (!canAssign) {
      toast.error("Chỉ điều phối viên hoặc quản trị viên được phân công ca");
      return;
    }
    let selected: string[] = String(row.assignee ?? "")
      .split(";")
      .filter(Boolean);
    Modal.confirm({
      title: `Phân công ca ${row.start}–${row.end}`,
      content: (
        <Select
          autoFocus
          style={{ width: "100%", marginTop: 16 }}
          placeholder="Chọn người trực"
          mode="multiple"
          defaultValue={selected}
          onChange={(value) => {
            selected = value;
          }}
          options={rows.teachers
            .filter(
              (teacher) => {
                if (teacher.status === "Ngừng hoạt động") return false;
                const hasScheduleConflict = rows.schedules.some(
                  (schedule) =>
                    schedule.date === row.date &&
                    schedule.teacher === teacher.name &&
                    String(row.start) < String(schedule.end) &&
                    String(schedule.start) < String(row.end),
                );
                const hasShiftConflict = rows.shifts.some(
                  (shift) =>
                    shift.id !== row.id &&
                    shift.date === row.date &&
                    String(shift.assignee ?? "").split(";").includes(String(teacher.name)) &&
                    String(row.start) < String(shift.end) &&
                    String(shift.start) < String(row.end),
                );
                return (
                  selected.includes(String(teacher.name)) ||
                  (!hasScheduleConflict && !hasShiftConflict)
                );
              },
            )
            .map((teacher) => ({ label: teacher.name, value: teacher.name }))}
        />
      ),
      okText: "Phân công",
      cancelText: "Hủy",
      onOk: () => {
        const required = Number(row.required ?? 1);
        if (selected.length < required)
          return Promise.reject(
            new Error(`Ca này cần tối thiểu ${required} người trực`),
          );
        void saveRecord("shifts", {
          ...row,
          assignee: selected.join(";"),
          status: "Đã phân công",
        }).then(async () => {
          await recordAudit("Phân công ca", "Ca trực", String(row.id), selected.join(", "));
          await addNotification("Ca trực được phân công", `${row.date} · ${selected.join(", ")}`);
          refresh();
          toast.success("Đã phân công ca trực");
        });
      },
    });
  };
  const shiftSwap = async (request: RowData, approve: boolean) => {
    if (!canApprove) {
      toast.error("Chỉ điều phối viên hoặc quản trị viên được duyệt đổi ca");
      return;
    }
    const nextStatus = approve ? "Đã duyệt" : "Đã từ chối";
    const shift =
      rows.shifts.find((item) => item.id === request.shiftId) ??
      rows.shifts.find(
        (item) =>
          item.date === request.date &&
          String(request.shift).includes(`${item.start}–${item.end}`),
      );
    if (approve && shift && request.to) {
      const replacementBusy =
        rows.schedules.some(
          (item) =>
            item.date === shift.date &&
            item.teacher === request.to &&
            String(shift.start) < String(item.end) &&
            String(item.start) < String(shift.end),
        ) ||
        rows.shifts.some(
          (item) =>
            item.id !== shift.id &&
            item.date === shift.date &&
            String(item.assignee ?? "").split(";").includes(String(request.to)) &&
            String(shift.start) < String(item.end) &&
            String(item.start) < String(shift.end),
        );
      if (replacementBusy) {
        toast.error("Người thay đang có lịch dạy hoặc ca trực trùng giờ");
        return;
      }
    }
    const writes: Promise<void>[] = [
      saveRecord("requests", { ...request, status: nextStatus }),
    ];
    if (approve && shift) {
      const current = String(shift.assignee ?? "")
        .split(";")
        .filter((name) => name && name !== request.from);
      if (request.requestType === "Xin nghỉ ca") {
        if (request.to) current.push(String(request.to));
        writes.push(saveRecord("shifts", {
          ...shift,
          assignee: current.join(";"),
          status: current.length >= Number(shift.required ?? 1)
            ? "Đã phân công"
            : "Chưa phân công",
        }));
      } else {
        current.push(String(request.to));
        writes.push(saveRecord("shifts", {
          ...shift,
          assignee: Array.from(new Set(current)).join(";"),
          status: "Đã phân công",
        }));
      }
    }
    await Promise.all(writes);
    await recordAudit(
      approve ? "Duyệt yêu cầu" : "Từ chối yêu cầu",
      "Yêu cầu ca trực",
      String(request.id),
      String(request.reason ?? ""),
    );
    await addNotification(
      approve ? "Yêu cầu ca trực đã được duyệt" : "Yêu cầu ca trực bị từ chối",
      `${request.from} · ${request.date}`,
    );
    refresh();
    toast.success(approve ? "Đã duyệt yêu cầu ca trực" : "Đã từ chối yêu cầu ca trực");
  };
  const updateShiftProgress = async (row: RowData, start: boolean) => {
    const handoverNote = start
      ? ""
      : window.prompt("Ghi chú bàn giao (nếu có)")?.trim() ?? "";
    await saveRecord("shifts", {
      ...row,
      status: start ? "Đang diễn ra" : "Hoàn thành",
      ...(start
        ? { checkedInAt: new Date().toISOString() }
        : { checkedOutAt: new Date().toISOString(), handoverNote }),
    });
    await recordAudit(
      start ? "Nhận ca" : "Kết thúc ca",
      "Ca trực",
      String(row.id),
      handoverNote || `${row.date} · ${row.start}–${row.end}`,
    );
    await addNotification(
      start ? "Ca trực đã bắt đầu" : "Ca trực đã hoàn thành",
      `${row.date} · ${row.room}`,
    );
    refresh();
    toast.success(start ? "Đã ghi nhận nhận ca" : "Đã ghi nhận kết thúc ca");
  };
  const requestShiftLeave = async (row: RowData) => {
    const reason = window.prompt("Lý do xin nghỉ ca")?.trim();
    if (!reason) return;
    const replacement = window.prompt("Người đề xuất thay (để trống nếu chưa có)")?.trim() ?? "";
    const request = {
      id: `request-${Date.now()}`,
      requestType: "Xin nghỉ ca",
      shiftId: String(row.id),
      from: selfName,
      to: replacement,
      date: row.date,
      shift: `${row.start}–${row.end} · ${row.room}`,
      reason,
      status: "Chờ duyệt",
    };
    await saveRecord("requests", request);
    await recordAudit("Gửi yêu cầu nghỉ ca", "Ca trực", String(row.id), reason);
    await addNotification("Có yêu cầu nghỉ ca mới", `${selfName} · ${row.date}`);
    refresh();
    toast.success("Đã gửi yêu cầu nghỉ ca chờ duyệt");
  };
  const createRole = async () => {
    const name = newRoleName.trim();
    if (!name || roles.includes(name)) {
      toast.error("Nhập tên vai trò mới, chưa tồn tại");
      return;
    }
    await Promise.all(
      permissionDefinitions.map((item) =>
        saveRecord("permissions", {
          id: `${name}::${item.key}`,
          role: name,
          permission: item.key,
          enabled: false,
        }),
      ),
    );
    refresh();
    changeRole(name);
    setNewRoleName("");
    setRoleCreateOpen(false);
    toast.success("Đã tạo vai trò mới với quyền mặc định bị tắt");
  };
  const addShiftTemplate = () => {
    const name = window.prompt("Tên mẫu ca trực")?.trim() ?? "";
    if (!name) return;
    const start = window.prompt("Giờ bắt đầu (HH:mm)", "07:30")?.trim() ?? "";
    const end = window.prompt("Giờ kết thúc (HH:mm)", "11:30")?.trim() ?? "";
    if (!start || !end || start >= end) {
      toast.error("Khung giờ mẫu chưa hợp lệ");
      return;
    }
    saveRecord("dutyTemplates", {
      id: `template-${Date.now()}`,
      name,
      start,
      end,
      required: 1,
      active: true,
    });
    refresh();
    toast.success("Đã lưu mẫu ca trực trong SQLite");
  };
  const togglePermission = async (
    targetRole: string,
    permission: string,
    enabled: boolean,
  ) => {
    await saveRecord("permissions", {
      id: `${targetRole}::${permission}`,
      role: targetRole,
      permission,
      enabled,
    });
    await recordAudit(
      enabled ? "Bật quyền demo" : "Tắt quyền demo",
      "Quyền",
      `${targetRole}::${permission}`,
      `${targetRole} · ${permission}`,
    );
    refresh();
  };
  const allowedScreen =
    screen === "overview" ||
    (screen === "myCalendar" && isTeacher) ||
    (screen === "schedule" && hasPermission("schedules.read")) ||
    (screen === "duties" && (hasPermission("duties.read") || canAssign)) ||
    (screen === "templates" && canCreateShift) ||
    (screen === "people" && hasPermission("teachers.read")) ||
    (screen === "rooms" && hasPermission("rooms.read")) ||
    (screen === "reports" && canViewReports && !isTeacher) ||
    (screen === "admin" && canManageUsers);
  useEffect(() => {
    if (ready && !allowedScreen) navigateToScreen("overview", true);
  }, [ready, allowedScreen]);
  const scheduleColumns: ColumnsType<RowData> = [
    {
      title: "Ngày & giờ",
      key: "when",
      render: (_, row) => (
        <>
          <strong>
            {row.day}, {row.date}
          </strong>
          <div className="muted">
            {row.start} – {row.end}
          </div>
        </>
      ),
    },
    {
      title: "Lớp / môn học",
      key: "class",
      render: (_, row) => (
        <>
          <strong>{row.className}</strong>
          <div className="muted">{row.subject}</div>
        </>
      ),
    },
    { title: "Giáo viên", dataIndex: "teacher" },
    { title: "Phòng", dataIndex: "room" },
    {
      title: "Ghi chú lịch",
      render: (_, row) =>
        row.status === "Đã hủy"
          ? `Lý do hủy: ${String(row.cancelReason ?? "")}`
          : row.type === "Học bù"
            ? `Học bù: ${String(row.makeupFor ?? "Buổi đã nghỉ")}`
            : row.type,
    },
    { title: "Trạng thái", dataIndex: "status", render: statusTag },
    {
      title: "",
      key: "actions",
      render: (_, row) => (
        <Space wrap>
          {canManage && row.status !== "Đã hủy" && (
            <Button
              type="link"
              size="small"
              onClick={() => openForm("schedules", "Cập nhật lịch", row)}
            >
              Sửa
            </Button>
          )}
          {canManage && row.status !== "Đã hủy" && (
            <Button
              type="link"
              size="small"
              danger
              onClick={async () => {
                const reason = window.prompt("Lý do hủy lịch")?.trim();
                if (!reason) return;
                await saveRecord("schedules", {
                  ...row,
                  status: "Đã hủy",
                  cancelReason: reason,
                  cancelledAt: new Date().toISOString(),
                });
                await recordAudit("Hủy lịch", "Lịch giảng dạy", String(row.id), reason);
                await addNotification("Lịch giảng dạy đã bị hủy", `${row.date} · ${row.className} · ${reason}`);
                refresh();
                toast.success("Đã hủy lịch và lưu lý do");
              }}
            >
              Hủy lịch
            </Button>
          )}
          {(canManage || (isTeacher && row.teacher === selfName)) &&
            !rows.sessions.some((session) => session.scheduleId === row.id) &&
            row.status !== "Đã hủy" && (
              <Button
                type="link"
                size="small"
                onClick={async () => {
                  const sessionId = `session-${row.id}-${Date.now()}`;
                  await saveRecord("sessions", {
                    id: sessionId,
                    scheduleId: String(row.id),
                    date: row.date,
                    startedAt: new Date().toISOString(),
                    status: "Đang diễn ra",
                    room: row.room,
                    className: row.className,
                    teacher: row.teacher,
                  });
                  await recordAudit("Bắt đầu phiên thực hành", "Phiên", sessionId, `${row.className} · ${row.date}`);
                  await addNotification("Phiên thực hành đã bắt đầu", `${row.className} · ${row.room}`);
                  refresh();
                  toast.success("Đã liên kết lịch với phiên thực hành");
                }}
              >
                Bắt đầu phiên
              </Button>
            )}
        </Space>
      ),
    },
  ];
  const shiftColumns: ColumnsType<RowData> = [
    {
      title: "Ngày",
      dataIndex: "date",
      render: (value) => <strong>{value}</strong>,
    },
    { title: "Khung giờ", render: (_, row) => `${row.start} – ${row.end}` },
    { title: "Phòng / khu vực", dataIndex: "room" },
    {
      title: "Nhận ca",
      dataIndex: "checkedInAt",
      render: (value) => value ? dayjs(String(value)).format("DD/MM HH:mm") : "—",
    },
    {
      title: "Kết thúc",
      dataIndex: "checkedOutAt",
      render: (value) => value ? dayjs(String(value)).format("DD/MM HH:mm") : "—",
    },
    { title: "Bàn giao", dataIndex: "handoverNote" },
    {
      title: "Người trực",
      dataIndex: "assignee",
      render: (value) =>
        value ? String(value).split(";").join(", ") : <span className="unassigned">Chưa có người</span>,
    },
    { title: "Trạng thái", dataIndex: "status", render: statusTag },
    {
      title: "Thao tác",
      render: (_, row) => (
        <Space>
          {canAssign && (
            <Button
              size="small"
              type="primary"
              ghost
              onClick={() => assign(row)}
            >
              {row.assignee ? "Đổi người" : "Phân công"}
            </Button>
          )}
          {(canManage || String(row.assignee ?? "").split(";").includes(selfName)) &&
            row.status !== "Hoàn thành" && (
            <Button
              size="small"
              onClick={() => updateShiftProgress(row, row.status !== "Đang diễn ra")}
            >
              {row.status === "Đang diễn ra" ? "Kết thúc ca" : "Bắt đầu ca"}
            </Button>
          )}
          {isTeacher && String(row.assignee ?? "").split(";").includes(selfName) && (
            <Button
              size="small"
              onClick={() => requestShiftLeave(row)}
            >
              Xin nghỉ ca
            </Button>
          )}
        </Space>
      ),
    },
  ];
  const peopleView = (collection: "teachers" | "classes") => {
    const columns: ColumnsType<RowData> =
      collection === "teachers"
        ? [
            {
              title: "Giáo viên",
              render: (_, row) => (
                <Space>
                  <Avatar style={{ background: "#dcece8", color: "#176a5b" }}>
                    {String(row.name).split(" ").at(-1)?.[0]}
                  </Avatar>
                  <div>
                    <strong>{row.name}</strong>
                    <div className="muted">{row.email}</div>
                  </div>
                </Space>
              ),
            },
            { title: "Bộ môn", dataIndex: "department" },
            { title: "Điện thoại", dataIndex: "phone" },
            { title: "Mã viên chức", dataIndex: "employeeCode" },
            { title: "Chức danh", dataIndex: "position" },
            { title: "Vai trò", dataIndex: "role" },
            { title: "Trạng thái", dataIndex: "status", render: statusTag },
            {
              title: "",
              render: (_, row) => hasPermission("teachers.write") ? (
                <Space>
                  <Button type="link" size="small" onClick={() => openForm("teachers", "Cập nhật hồ sơ giáo viên", row)}>Sửa</Button>
                  <Button
                    type="link"
                    size="small"
                    onClick={async () => {
                      const status = row.status === "Ngừng hoạt động" ? "Đang hoạt động" : "Ngừng hoạt động";
                      await saveRecord("teachers", { ...row, status });
                      await recordAudit(status === "Ngừng hoạt động" ? "Ngừng hoạt động hồ sơ" : "Kích hoạt hồ sơ", "Giáo viên", String(row.id), String(row.name));
                      await addNotification("Trạng thái hồ sơ giáo viên đã đổi", `${row.name} · ${status}`);
                      refresh();
                    }}
                  >
                    {row.status === "Ngừng hoạt động" ? "Kích hoạt" : "Ngừng hoạt động"}
                  </Button>
                </Space>
              ) : null,
            },
          ]
        : [
            {
              title: "Lớp học",
              dataIndex: "name",
              render: (value) => <strong>{value}</strong>,
            },
            { title: "Mã lớp", dataIndex: "code" },
            { title: "Giáo viên chủ nhiệm", dataIndex: "teacher" },
            { title: "Học kỳ", dataIndex: "semester" },
            { title: "Sĩ số", dataIndex: "students" },
            { title: "", render: (_, row) => rowActions(collection, row) },
          ];
    return (
      <Table
        rowKey="id"
        columns={columns}
        dataSource={rows[collection].filter((row) =>
          JSON.stringify(row).toLowerCase().includes(query.toLowerCase()),
        )}
        pagination={{ pageSize: 6, showSizeChanger: false }}
      />
    );
  };
  const inventoryView = (collection: "rooms" | "subjects") => (
    <Table
      rowKey="id"
      dataSource={rows[collection]}
      pagination={false}
      columns={
        collection === "rooms"
          ? [
              {
                title: "Phòng / khu vực",
                dataIndex: "name",
                render: (value) => (
                  <Space>
                    <EnvironmentOutlined className="table-icon" />
                    <strong>{value}</strong>
                  </Space>
                ),
              },
              { title: "Tòa nhà", dataIndex: "building" },
              {
                title: "Sức chứa",
                dataIndex: "capacity",
                render: (value) => `${value} chỗ`,
              },
              { title: "Tình trạng", dataIndex: "status", render: statusTag },
              { title: "", render: (_, row) => rowActions(collection, row) },
            ]
          : [
              { title: "Môn học", dataIndex: "name" },
              { title: "Mã môn", dataIndex: "code" },
              {
                title: "Thời lượng",
                dataIndex: "hours",
                render: (value) => `${value} giờ`,
              },
              { title: "", render: (_, row) => rowActions(collection, row) },
            ]
      }
    />
  );
  const personalItems: RowData[] = [
    ...rows.schedules
      .filter((row) => row.teacher === selfName)
      .map((row) => ({
        ...row,
        activity: "Giảng dạy",
        location: row.room,
        detail: `${row.className} · ${row.subject}`,
      })),
    ...rows.shifts
      .filter((row) => String(row.assignee ?? "").split(";").includes(selfName))
      .map((row) => ({
        ...row,
        activity: "Ca trực",
        location: row.room,
        detail: "Trực phòng thực hành",
      })),
  ].sort((first, second) =>
    `${first.date} ${first.start}`.localeCompare(
      `${second.date} ${second.start}`,
    ),
  );
  const personalWeekStart = calendarAnchor
    .startOf("day")
    .subtract((calendarAnchor.day() + 6) % 7, "day");
  const personalRangeStart = (
    calendarView === "week"
      ? personalWeekStart
      : calendarAnchor.startOf("month")
  ).format("YYYY-MM-DD");
  const personalRangeEnd = (
    calendarView === "week"
      ? personalWeekStart.add(6, "day")
      : calendarAnchor.endOf("month")
  ).format("YYYY-MM-DD");
  const filteredPersonalItems = personalItems.filter((item) =>
    String(item.date) >= personalRangeStart &&
    String(item.date) <= personalRangeEnd &&
    (calendarActivity === "Tất cả hoạt động" || item.activity === calendarActivity),
  );
  const visibleDutyShifts = isTeacher
    ? rows.shifts.filter((row) =>
        String(row.assignee ?? "").split(";").includes(selfName),
      )
    : rows.shifts;
  const visibleDutyRequests = isTeacher
    ? rows.requests.filter(
        (request) => request.from === selfName || request.to === selfName,
      )
    : rows.requests;
  const visibleUnassignedDuties = visibleDutyShifts.filter(
    (row) => !row.assignee,
  ).length;
  const reportSchedules = rows.schedules.filter(
    (row) => String(row.date).startsWith(reportMonth) && row.status !== "Đã hủy",
  );
  const reportShifts = rows.shifts.filter((row) => String(row.date).startsWith(reportMonth));
  const reportWorkDays = Array.from(
    { length: dayjs(`${reportMonth}-01`).daysInMonth() },
    (_, index) => dayjs(`${reportMonth}-01`).add(index, "day"),
  ).filter((date) => date.day() !== 0 && date.day() !== 6).length;
  const roomReport = rows.rooms.map((room) => {
    const hours = [...reportSchedules, ...reportShifts]
      .filter((entry) => entry.room === room.name)
      .reduce((total, entry) => total + durationHours(entry), 0);
    const availableHours = reportWorkDays * 8;
    return {
      ...room,
      usedHours: hours,
      utilization: availableHours
        ? Math.min(100, Math.round((hours / availableHours) * 100))
        : 0,
    };
  });
  const cancelledScheduleCount = rows.schedules.filter(
    (row) => String(row.date).startsWith(reportMonth) && row.status === "Đã hủy",
  ).length;
  const pendingScheduleCount = rows.schedules.filter(
    (row) => String(row.date).startsWith(reportMonth) && row.status === "Chờ xác nhận",
  ).length;
  const unreadNotifications = rows.notifications.filter((item) => !item.read).length;
  const todayKey = dayjs().format("YYYY-MM-DD");
  const weekStart = dayjs().startOf("day").subtract((dayjs().day() + 6) % 7, "day");
  const dashboardWeekSchedules = rows.schedules.filter(
    (row) =>
      row.status !== "Đã hủy" &&
      String(row.date) >= weekStart.format("YYYY-MM-DD") &&
      String(row.date) <= weekStart.add(6, "day").format("YYYY-MM-DD"),
  );
  const todaySchedules = rows.schedules.filter(
    (row) => row.date === todayKey && row.status !== "Đã hủy",
  );
  const todayRoomHours = [
    ...todaySchedules,
    ...rows.shifts.filter((row) => row.date === todayKey),
  ].reduce((total, row) => total + durationHours(row), 0);
  const roomUtilizationToday = rows.rooms.length
    ? Math.min(100, Math.round((todayRoomHours / (rows.rooms.length * 8)) * 100))
    : 0;
  const todayLabel = `${["CHỦ NHẬT", "THỨ 2", "THỨ 3", "THỨ 4", "THỨ 5", "THỨ 6", "THỨ 7"][dayjs().day()]}, ${dayjs().format("DD [THÁNG] MM, YYYY")}`;
  const isTechnician = role === "Kỹ thuật viên";
  const isUnitManager = role === "Quản lý đơn vị";
  const overviewTitle = isTeacher
    ? "Tổng quan cá nhân"
    : isTechnician
      ? "Tổng quan kỹ thuật viên"
      : isUnitManager
        ? "Tổng quan đơn vị"
        : role === "Điều phối/giáo vụ"
          ? "Tổng quan điều phối"
          : "Tổng quan hệ thống";
  const overviewDescription = isTeacher
    ? `Lịch dạy, ca trực và yêu cầu của ${selfName}.`
    : isTechnician
      ? `Ca phụ trách, phiên thực hành và thiết bị cần theo dõi.`
      : isUnitManager
        ? "Thống kê hoạt động đào tạo và sử dụng cơ sở ở phạm vi đơn vị."
        : "Tình hình lịch dạy, ca trực và các mục cần điều phối.";
  const teacherWeekSchedules = dashboardWeekSchedules.filter(
    (row) => row.teacher === selfName,
  );
  const teacherTodaySchedules = todaySchedules.filter(
    (row) => row.teacher === selfName,
  );
  const teacherPendingRequests = rows.requests.filter(
    (row) => row.from === selfName && row.status === "Chờ duyệt",
  );
  const technicianSessions = rows.sessions.filter(
    (session) => session.status === "Đang diễn ra",
  );
  const equipmentNeedsAttention = rows.equipment.filter(
    (item) => item.status !== "Sẵn sàng",
  );
  const dashboardAssignedShifts = rows.shifts.filter(
    (row) => row.assignee && row.status !== "Đã hủy",
  );

  const header = (
    eyebrow: string,
    description: string,
    action?: React.ReactNode,
    titleOverride?: string,
  ) => (
    <div className="page-toolbar">
      <div>
        <div className="eyebrow">{eyebrow}</div>
        <Typography.Title level={2}>{titleOverride ?? titles[screen]}</Typography.Title>
        <Typography.Text type="secondary">{description}</Typography.Text>
      </div>
      {action}
    </div>
  );

  const renderContent = () => {
    if (!ready && startupError)
      return (
        <div className="loading-state">
          <Space direction="vertical" align="center">
            <Typography.Text type="danger">{startupError}</Typography.Text>
            <Button
              icon={<ReloadOutlined />}
              onClick={() => {
                setStartupError(null);
                setStartupAttempt((attempt) => attempt + 1);
              }}
            >
              Thử kết nối lại
            </Button>
          </Space>
        </div>
      );
    if (!ready)
      return <div className="loading-state">Đang mở cơ sở dữ liệu cục bộ…</div>;
    if (!allowedScreen)
      return (
        <Card className="panel-card">
          <Typography.Title level={4}>
            Vai trò hiện tại chưa được cấp quyền
          </Typography.Title>
          <Typography.Text>
            Chọn vai trò khác hoặc cập nhật quyền trong Quản trị.
          </Typography.Text>
        </Card>
      );
    if (screen === "admin")
      return (
        <AdminScreen
          role={role}
          roles={roles}
          users={rows.users}
          permissions={rows.permissions}
          auditLogs={rows.auditLogs}
          canManageUsers={canManageUsers}
          hasPermission={hasPermission}
          onRoleChange={changeRole}
          onCreateRole={() => setRoleCreateOpen(true)}
          onAddUser={() => openForm("users", "Thêm tài khoản")}
          onEditUser={(user) => openForm("users", "Cập nhật tài khoản", user)}
          onRemoveUser={(user) => remove("users", String(user.id))}
          onTogglePermission={togglePermission}
          onResetDemo={() =>
            Modal.confirm({
              title: "Khôi phục dữ liệu mẫu?",
              content:
                "Các bản ghi RAM trên instance đang phục vụ sẽ được nạp lại từ dữ liệu mẫu.",
              okText: "Khôi phục",
              cancelText: "Hủy",
              onOk: () => {
                resetDatabase();
                refresh();
                changeRole("Điều phối/giáo vụ");
                toast.success("Đã khôi phục dữ liệu mẫu");
              },
            })
          }
        />
      );
    if (screen === "overview" && isTeacher)
      return (
        <>
          {header(
            todayLabel,
            overviewDescription,
            <Button
              type="primary"
              onClick={() => navigateToScreen("myCalendar")}
            >
              Mở lịch cá nhân
            </Button>,
            overviewTitle,
          )}
          <Row gutter={[14, 14]} className="metric-row">
            <Col xs={12} xl={8}>
              <Card className="metric-card">
                <Statistic title="Lịch dạy hôm nay" value={teacherTodaySchedules.length} prefix={<BookOutlined />} />
                <div className="metric-note">Các buổi được xếp cho bạn</div>
              </Card>
            </Col>
            <Col xs={12} xl={8}>
              <Card className="metric-card">
                <Statistic title="Ca chưa nhận" value={teacherUnstartedShifts} prefix={<ClockCircleOutlined />} />
                <div className="metric-note">Ca trực đã phân công cho bạn</div>
              </Card>
            </Col>
            <Col xs={12} xl={8}>
              <Card className="metric-card metric-alert">
                <Statistic title="Yêu cầu chờ xử lý" value={teacherPendingRequests.length} prefix={<WarningOutlined />} />
                <div className="metric-note">Yêu cầu đổi hoặc nghỉ ca của bạn</div>
              </Card>
            </Col>
          </Row>
          <Row gutter={[16, 16]} className="dashboard-grid">
            <Col xs={24} xl={14}>
              <Card
                title="Lịch dạy tuần này của tôi"
                extra={<Button type="link" onClick={() => navigateToScreen("myCalendar")}>Mở lịch cá nhân →</Button>}
                className="panel-card"
              >
                <Table
                  rowKey="id"
                  size="small"
                  pagination={false}
                  dataSource={teacherWeekSchedules}
                  locale={{ emptyText: "Tuần này bạn chưa có lịch dạy" }}
                  columns={[
                    { title: "Ngày", dataIndex: "date" },
                    { title: "Giờ", render: (_, row) => `${row.start}–${row.end}` },
                    { title: "Lớp / môn", render: (_, row) => `${row.className} · ${row.subject}` },
                    { title: "Phòng", dataIndex: "room" },
                    { title: "Trạng thái", dataIndex: "status", render: statusTag },
                  ]}
                />
              </Card>
            </Col>
            <Col xs={24} xl={10}>
              <Card
                title="Ca trực được giao"
                extra={<Button type="link" onClick={() => navigateToScreen("duties")}>Mở ca trực →</Button>}
                className="panel-card"
              >
                <Table
                  rowKey="id"
                  size="small"
                  pagination={false}
                  dataSource={visibleDutyShifts.slice(0, 5)}
                  locale={{ emptyText: "Bạn chưa được phân công ca" }}
                  columns={[
                    { title: "Ngày", dataIndex: "date" },
                    { title: "Giờ", render: (_, row) => `${row.start}–${row.end}` },
                    { title: "Phòng", dataIndex: "room" },
                    { title: "Trạng thái", dataIndex: "status", render: statusTag },
                  ]}
                />
              </Card>
              <Card className="quick-card">
                <span className="eyebrow">THAO TÁC CỦA TÔI</span>
                <div className="quick-actions">
                  <Button icon={<CalendarOutlined />} onClick={() => navigateToScreen("myCalendar")}>Lịch cá nhân</Button>
                  <Button icon={<ClockCircleOutlined />} onClick={() => navigateToScreen("duties")}>Ca trực của tôi</Button>
                </div>
              </Card>
            </Col>
          </Row>
        </>
      );
    if (screen === "overview" && isTechnician)
      return (
        <>
          {header(
            todayLabel,
            overviewDescription,
            <Button onClick={() => navigateToScreen("rooms")}>Mở vận hành phòng</Button>,
            overviewTitle,
          )}
          <Row gutter={[14, 14]} className="metric-row">
            <Col xs={12} xl={6}><Card className="metric-card"><Statistic title="Ca được giao" value={visibleDutyShifts.length} prefix={<ClockCircleOutlined />} /><div className="metric-note">Ca có tên bạn phụ trách</div></Card></Col>
            <Col xs={12} xl={6}><Card className="metric-card"><Statistic title="Phiên đang diễn ra" value={technicianSessions.length} prefix={<BookOutlined />} /><div className="metric-note">Phiên thực hành trong hệ thống</div></Card></Col>
            <Col xs={12} xl={6}><Card className="metric-card metric-alert"><Statistic title="Thiết bị cần kiểm tra" value={equipmentNeedsAttention.length} prefix={<WarningOutlined />} /><div className="metric-note">Không ở trạng thái sẵn sàng</div></Card></Col>
            <Col xs={12} xl={6}><Card className="metric-card"><Statistic title="Phòng bảo trì" value={rows.rooms.filter((room) => room.status !== "Sẵn sàng").length} prefix={<EnvironmentOutlined />} /><div className="metric-note">Tình trạng danh mục phòng</div></Card></Col>
          </Row>
          <Row gutter={[16, 16]} className="dashboard-grid">
            <Col xs={24} xl={14}>
              <Card title="Ca trực liên quan" className="panel-card" extra={<Button type="link" onClick={() => navigateToScreen("duties")}>Xem ca →</Button>}>
                <Table
                  rowKey="id"
                  size="small"
                  pagination={false}
                  dataSource={visibleDutyShifts}
                  locale={{ emptyText: "Bạn chưa có ca trực được giao" }}
                  columns={[
                    { title: "Ngày", dataIndex: "date" },
                    { title: "Thời gian", render: (_, row) => `${row.start}–${row.end}` },
                    { title: "Phòng", dataIndex: "room" },
                    { title: "Trạng thái", dataIndex: "status", render: statusTag },
                  ]}
                />
              </Card>
            </Col>
            <Col xs={24} xl={10}>
              <Card title="Thiết bị cần theo dõi" className="panel-card">
                <Table
                  rowKey="id"
                  size="small"
                  pagination={false}
                  dataSource={equipmentNeedsAttention}
                  locale={{ emptyText: "Thiết bị đều sẵn sàng" }}
                  columns={[
                    { title: "Thiết bị", dataIndex: "name" },
                    { title: "Phòng", dataIndex: "room" },
                    { title: "Tình trạng", dataIndex: "status", render: statusTag },
                  ]}
                />
              </Card>
            </Col>
          </Row>
        </>
      );
    if (screen === "overview" && isUnitManager)
      return (
        <>
          {header(
            todayLabel,
            overviewDescription,
            <Button onClick={() => navigateToScreen("reports")}>Mở báo cáo</Button>,
            overviewTitle,
          )}
          <Row gutter={[14, 14]} className="metric-row">
            <Col xs={12} xl={6}><Card className="metric-card"><Statistic title="Giáo viên hoạt động" value={rows.teachers.filter((teacher) => teacher.status === "Đang hoạt động").length} prefix={<TeamOutlined />} /><div className="metric-note">Theo danh mục hiện tại</div></Card></Col>
            <Col xs={12} xl={6}><Card className="metric-card"><Statistic title="Lịch dạy hôm nay" value={todaySchedules.length} prefix={<BookOutlined />} /><div className="metric-note">Toàn đơn vị</div></Card></Col>
            <Col xs={12} xl={6}><Card className="metric-card"><Statistic title="Ca đã phân công" value={dashboardAssignedShifts.length} prefix={<ClockCircleOutlined />} /><div className="metric-note">Ca có người phụ trách</div></Card></Col>
            <Col xs={12} xl={6}><Card className="metric-card"><Statistic title="Sử dụng phòng hôm nay" value={roomUtilizationToday} suffix="%" prefix={<EnvironmentOutlined />} /><div className="metric-note">Ước tính từ lịch và ca</div></Card></Col>
          </Row>
          <Row gutter={[16, 16]} className="dashboard-grid">
            <Col xs={24} xl={15}>
              <Card title="Lịch dạy trong tuần" className="panel-card" extra={<Button type="link" onClick={() => navigateToScreen("schedule")}>Xem lịch →</Button>}>
                <Table
                  rowKey="id"
                  size="small"
                  pagination={{ pageSize: 5 }}
                  dataSource={dashboardWeekSchedules}
                  locale={{ emptyText: "Tuần này chưa có lịch dạy" }}
                  columns={[
                    { title: "Ngày", dataIndex: "date" },
                    { title: "Giờ", render: (_, row) => `${row.start}–${row.end}` },
                    { title: "Lớp / môn", render: (_, row) => `${row.className} · ${row.subject}` },
                    { title: "Giáo viên", dataIndex: "teacher" },
                    { title: "Phòng", dataIndex: "room" },
                  ]}
                />
              </Card>
            </Col>
            <Col xs={24} xl={9}>
              <Card title="Tình trạng cơ sở" className="panel-card">
                <Table
                  rowKey="id"
                  size="small"
                  pagination={false}
                  dataSource={roomReport}
                  columns={[
                    { title: "Phòng", dataIndex: "name" },
                    { title: "Trạng thái", dataIndex: "status", render: statusTag },
                    { title: "Sử dụng", dataIndex: "utilization", render: (value) => `${value}%` },
                  ]}
                />
              </Card>
            </Col>
          </Row>
        </>
      );
    if (screen === "overview" && role === "Quản trị hệ thống")
      return (
        <>
          {header(
            todayLabel,
            "Tình trạng dữ liệu demo, tài khoản và hoạt động quản trị.",
            <Button
              type="primary"
              icon={<SafetyCertificateOutlined />}
              onClick={() => navigateToScreen("admin")}
            >
              Mở quản trị
            </Button>,
            overviewTitle,
          )}
          <Row gutter={[14, 14]} className="metric-row">
            <Col xs={12} xl={6}>
              <Card className="metric-card">
                <Statistic
                  title="Tài khoản hoạt động"
                  value={rows.users.filter((user) => user.status === "Đang hoạt động").length}
                  prefix={<TeamOutlined />}
                />
                <div className="metric-note">Trong danh sách tài khoản demo</div>
              </Card>
            </Col>
            <Col xs={12} xl={6}>
              <Card className="metric-card">
                <Statistic title="Vai trò" value={roles.length} prefix={<SafetyCertificateOutlined />} />
                <div className="metric-note">Vai trò cấu hình trên giao diện</div>
              </Card>
            </Col>
            <Col xs={12} xl={6}>
              <Card className="metric-card">
                <Statistic title="Bản ghi demo" value={recordsUsed} prefix={<DatabaseOutlined />} />
                <div className="metric-note">SQLite RAM theo instance</div>
              </Card>
            </Col>
            <Col xs={12} xl={6}>
              <Card className="metric-card metric-alert">
                <Statistic title="Thông báo chưa đọc" value={unreadNotifications} prefix={<BellOutlined />} />
                <div className="metric-note">Thông báo trong ứng dụng</div>
              </Card>
            </Col>
          </Row>
          <Row gutter={[16, 16]} className="dashboard-grid">
            <Col xs={24} xl={9}>
              <Card title="Tình trạng hệ thống demo" className="panel-card">
                <Space direction="vertical" size={12}>
                  <Space><Badge status="success" /><strong>API nội bộ sẵn sàng</strong></Space>
                  <Typography.Text type="secondary">Lưu trữ tạm trong RAM; dữ liệu có thể khác giữa các instance và mất khi khởi động lại.</Typography.Text>
                  <Space wrap>
                    <Button onClick={() => navigateToScreen("admin")}>Tài khoản & vai trò</Button>
                    <Button onClick={() => navigateToScreen("rooms")}>Phòng & thiết bị</Button>
                    <Button onClick={() => navigateToScreen("reports")}>Báo cáo</Button>
                  </Space>
                </Space>
              </Card>
            </Col>
            <Col xs={24} xl={15}>
              <Card title="Thao tác quản trị gần đây" className="panel-card">
                <Table
                  rowKey="id"
                  size="small"
                  pagination={false}
                  dataSource={[...rows.auditLogs]
                    .sort((first, second) => String(second.occurredAt).localeCompare(String(first.occurredAt)))
                    .slice(0, 6)}
                  locale={{ emptyText: "Chưa có thao tác được ghi nhận" }}
                  columns={[
                    { title: "Thời điểm", dataIndex: "occurredAt", render: (value) => value ? dayjs(String(value)).format("DD/MM HH:mm") : "—" },
                    { title: "Người thực hiện", dataIndex: "actor" },
                    { title: "Thao tác", dataIndex: "action" },
                    { title: "Đối tượng", dataIndex: "entity" },
                  ]}
                />
              </Card>
            </Col>
          </Row>
        </>
      );
    if (screen === "overview")
      return (
        <>
          {header(
            todayLabel,
            overviewDescription,
            <Button
              icon={<DownloadOutlined />}
              disabled={!canViewReports}
              onClick={() => exportCsv(rows.schedules, "lich-giang-day")}
            >
              Xuất báo cáo
            </Button>,
            overviewTitle,
          )}
          <Row gutter={[14, 14]} className="metric-row">
            <Col xs={12} xl={6}>
              <Card className="metric-card">
                <Statistic
                  title="Giáo viên hoạt động"
                  value={rows.teachers.length}
                  prefix={<TeamOutlined />}
                />
                <div className="metric-note">Trong danh mục hiện tại</div>
              </Card>
            </Col>
            <Col xs={12} xl={6}>
              <Card className="metric-card">
                <Statistic
                  title="Lịch dạy hôm nay"
                  value={todaySchedules.length}
                  prefix={<BookOutlined />}
                />
                <div className="metric-note positive">
                  <CheckCircleOutlined /> Lịch đã xác nhận
                </div>
              </Card>
            </Col>
            <Col xs={12} xl={6}>
              <Card className="metric-card">
                <Statistic
                  title="Ca trực tuần này"
                  value={rows.shifts.length}
                  prefix={<ClockCircleOutlined />}
                />
                <div className="metric-note">
                  {rows.shifts.filter((row) => row.assignee).length} ca đã có
                  người
                </div>
              </Card>
            </Col>
            <Col xs={12} xl={6}>
              <Card className="metric-card metric-alert">
                <Statistic
                  title="Cần xử lý"
                  value={unassigned + pending}
                  prefix={<WarningOutlined />}
                />
                <div className="metric-note">
                  {unassigned} ca trống · {pending} yêu cầu đổi ca
                </div>
              </Card>
            </Col>
          </Row>
          <Row gutter={[16, 16]} className="dashboard-grid">
            <Col xs={24} xl={15}>
              <Card
                title="Lịch trong tuần"
                extra={
                  <Button type="link" onClick={() => navigateToScreen("schedule")}>
                    Mở lịch →
                  </Button>
                }
                className="panel-card"
              >
                <div className="week-strip">
                  {days.map((day, index) => {
                    const date = weekStart.add(index, "day");
                    const dateKey = date.format("YYYY-MM-DD");
                    return (
                      <div
                        key={day}
                        className={`week-day ${dateKey === todayKey ? "current" : ""}`}
                      >
                        <span>{day}</span>
                        <strong>{date.date()}</strong>
                        <i>
                          {dashboardWeekSchedules.filter((item) => item.date === dateKey).length || "·"}
                        </i>
                      </div>
                    );
                  })}
                </div>
                <div className="event-list">
                  {dashboardWeekSchedules.slice(0, 4).map((item) => (
                    <div className="event-row" key={item.id}>
                      <div className="event-time">
                        {item.start}
                        <span>{item.end}</span>
                      </div>
                      <div className="event-mark" />
                      <div className="event-copy">
                        <strong>{item.className}</strong>
                        <span>
                          {item.subject} · {item.teacher}
                        </span>
                      </div>
                      <Tag>{item.room?.split(" ").at(-1)}</Tag>
                    </div>
                  ))}
                </div>
              </Card>
            </Col>
            <Col xs={24} xl={9}>
              <Card
                title="Cần chú ý"
                extra={<Badge count={unassigned + pending} />}
                className="panel-card"
              >
                <div className="attention-list">
                  {unassigned > 0 && (
                    <div className="attention-item">
                      <span className="attention-icon amber">
                        <AlertOutlined />
                      </span>
                      <div>
                        <strong>{unassigned} ca trực chưa phân công</strong>
                        <span>Kiểm tra lịch trực trong tuần</span>
                      </div>
                      <Button type="link" onClick={() => navigateToScreen("duties")}>
                        Xem
                      </Button>
                    </div>
                  )}
                  {pending > 0 && (
                    <div className="attention-item">
                      <span className="attention-icon coral">
                        <ReloadOutlined />
                      </span>
                      <div>
                        <strong>{pending} yêu cầu đổi ca</strong>
                        <span>Cần điều phối viên phê duyệt</span>
                      </div>
                      <Button type="link" onClick={() => navigateToScreen("duties")}>
                        Duyệt
                      </Button>
                    </div>
                  )}
                  <div className="room-usage">
                    <div className="room-usage-heading">
                      <strong>Sử dụng phòng hôm nay</strong>
                      <span>{roomUtilizationToday}%</span>
                    </div>
                    <Progress
                      percent={roomUtilizationToday}
                      showInfo={false}
                      strokeColor="#287d70"
                      trailColor="#e9efed"
                    />
                    <span className="muted">
                      {todayRoomHours.toFixed(1)} giờ đã đặt trên {rows.rooms.length * 8} giờ khả dụng
                    </span>
                  </div>
                </div>
              </Card>
              <Card className="quick-card">
                <span className="eyebrow">THAO TÁC NHANH</span>
                <div className="quick-actions">
                  {canManage && (
                    <Button
                      icon={<PlusOutlined />}
                      onClick={() =>
                        openForm("schedules", "Tạo lịch giảng dạy")
                      }
                    >
                      Tạo lịch
                    </Button>
                  )}
                  {canCreateShift && (
                    <Button
                      icon={<ClockCircleOutlined />}
                      onClick={() => openForm("shifts", "Tạo ca trực")}
                    >
                      Tạo ca trực
                    </Button>
                  )}
                </div>
              </Card>
            </Col>
          </Row>
        </>
      );
    if (screen === "schedule")
      return (
        <>
          {header(
            selectedSemester === "Tất cả học kỳ" ? "TẤT CẢ HỌC KỲ" : selectedSemester.toUpperCase(),
            "Lịch lặp theo tuần; kiểm tra trùng giáo viên, phòng và lớp.",
            <Space>
              <Button
                icon={<DownloadOutlined />}
                onClick={() => exportCsv(visibleSchedules, "lich-giang-day")}
              >
                Xuất CSV
              </Button>
              {canManage && (
                <Button
                  type="primary"
                  icon={<PlusOutlined />}
                  onClick={() => openForm("schedules", "Tạo lịch giảng dạy")}
                >
                  Tạo lịch
                </Button>
              )}
            </Space>,
          )}
          {isTeacher && (
            <div className="permission-notice">
              Đang xem lịch cá nhân của {selfName}. Lịch chung được giới hạn
              theo vai trò.
            </div>
          )}
          <Card className="panel-card">
            <div className="table-toolbar">
              <Space>
                <Button onClick={() => setScheduleAnchor((date) => date.subtract(1, "week"))}>‹</Button>
                <Button onClick={() => setScheduleAnchor(dayjs())}>Tuần hiện tại</Button>
                <Button onClick={() => setScheduleAnchor((date) => date.add(1, "week"))}>›</Button>
                <Select
                  value={selectedSemester}
                  onChange={setSelectedSemester}
                  options={[
                    { value: "Tất cả học kỳ", label: "Tất cả học kỳ" },
                    ...rows.academicTerms.map((term) => ({
                      value: String(term.name),
                      label: String(term.name),
                    })),
                  ]}
                  style={{ minWidth: 190 }}
                />
                <Typography.Text strong>
                  {scheduleWeekStart.format("DD")} – {scheduleWeekEnd.format("DD MMMM YYYY")}
                </Typography.Text>
              </Space>
              <Input
                allowClear
                prefix={<SearchOutlined />}
                placeholder="Tìm giáo viên, lớp, phòng…"
                value={query}
                onChange={(event) => setQuery(event.target.value)}
                className="search-box"
              />
            </div>
            <Table
              rowKey="id"
              columns={scheduleColumns}
              dataSource={visibleSchedules}
              pagination={{ pageSize: 7, showSizeChanger: false }}
            />
          </Card>
        </>
      );
    if (screen === "myCalendar")
      return (
        <>
          {header(
            "LỊCH CÁ NHÂN",
            `Lịch giảng dạy và ca trực của ${selfName}, hiển thị cùng một dòng thời gian.`,
            <Button
              icon={<DownloadOutlined />}
              onClick={() => exportCsv(filteredPersonalItems, "lich-ca-nhan")}
            >
              Xuất CSV
            </Button>,
          )}
          <Card className="panel-card">
            <div className="table-toolbar">
              <Space wrap>
                <Select
                  value={calendarView}
                  onChange={setCalendarView}
                  options={[
                    { value: "week", label: "Xem theo tuần" },
                    { value: "month", label: "Xem theo tháng" },
                  ]}
                />
                <DatePicker
                  picker={calendarView}
                  value={calendarAnchor}
                  onChange={(value) => value && setCalendarAnchor(value)}
                  allowClear={false}
                />
                <Select
                  value={calendarActivity}
                  onChange={setCalendarActivity}
                  options={[
                    { value: "Tất cả hoạt động", label: "Tất cả hoạt động" },
                    { value: "Giảng dạy", label: "Chỉ lịch dạy" },
                    { value: "Ca trực", label: "Chỉ ca trực" },
                  ]}
                />
              </Space>
            </div>
            <div className="personal-legend">
              <Tag color="blue">Giảng dạy</Tag>
              <Tag color="orange">Ca trực</Tag>
              <span className="muted">
                {calendarView === "week"
                  ? `${personalWeekStart.format("DD")} – ${personalWeekStart.add(6, "day").format("DD MMMM YYYY")}`
                  : calendarAnchor.format("MMMM YYYY")}
              </span>
            </div>
            <Table
              rowKey="id"
              dataSource={filteredPersonalItems}
              pagination={false}
              columns={[
                { title: "Ngày", dataIndex: "date" },
                {
                  title: "Thời gian",
                  render: (_, row) => `${row.start} – ${row.end}`,
                },
                {
                  title: "Loại hoạt động",
                  dataIndex: "activity",
                  render: (value) => (
                    <Tag color={value === "Ca trực" ? "orange" : "blue"}>
                      {value}
                    </Tag>
                  ),
                },
                { title: "Nội dung", dataIndex: "detail" },
                { title: "Phòng", dataIndex: "location" },
                { title: "Trạng thái", dataIndex: "status", render: statusTag },
                {
                  title: "Thao tác",
                  render: (_, row) =>
                    row.activity === "Ca trực" && String(row.assignee ?? "").split(";").includes(selfName) ? (
                      <Space>
                        <Button
                          size="small"
                          disabled={row.status === "Hoàn thành"}
                          onClick={() => updateShiftProgress(row, row.status !== "Đang diễn ra")}
                        >
                          {row.status === "Đang diễn ra"
                            ? "Kết thúc ca"
                            : "Nhận ca"}
                        </Button>
                        <Button
                          size="small"
                          onClick={() => requestShiftLeave(row)}
                        >
                          Xin nghỉ ca
                        </Button>
                      </Space>
                    ) : null,
                },
              ]}
              locale={{
                emptyText: "Chưa có hoạt động được phân công trong tuần này",
              }}
            />
          </Card>
        </>
      );
    if (screen === "templates")
      return (
        <>
          {header(
            "CA TRỰC · CẤU HÌNH",
            "Tạo mẫu ca để tái sử dụng khi lập ca trực theo ngày.",
            <Button
              type="primary"
              icon={<PlusOutlined />}
              disabled={!canCreateShift}
              onClick={addShiftTemplate}
            >
              Tạo mẫu ca
            </Button>,
          )}
          <Card className="panel-card">
            <Table
              rowKey="id"
              dataSource={rows.dutyTemplates}
              pagination={false}
              columns={[
                {
                  title: "Tên mẫu",
                  dataIndex: "name",
                  render: (value) => <strong>{value}</strong>,
                },
                { title: "Bắt đầu", dataIndex: "start" },
                { title: "Kết thúc", dataIndex: "end" },
                { title: "Số người cần", dataIndex: "required" },
                {
                  title: "Trạng thái",
                  dataIndex: "active",
                  render: (active) =>
                    statusTag(active ? "Đang hoạt động" : "Tạm ngưng"),
                },
                {
                  title: "Thao tác",
                  render: (_, row) =>
                    canCreateShift ? (
                      <Space>
                        <Button
                          size="small"
                          onClick={() => {
                            saveRecord("dutyTemplates", {
                              ...row,
                              active: !row.active,
                            });
                            refresh();
                          }}
                        >
                          {row.active ? "Tạm ngưng" : "Kích hoạt"}
                        </Button>
                        <Button
                          size="small"
                          type="primary"
                          ghost
                          onClick={() =>
                            openForm("shifts", "Tạo ca trực từ mẫu", {
                              date: "2026-11-07",
                              start: row.start,
                              end: row.end,
                              required: row.required,
                              status: "Chưa phân công",
                            })
                          }
                        >
                          Tạo ca
                        </Button>
                        {rowActions("dutyTemplates", row)}
                      </Space>
                    ) : null,
                },
              ]}
            />
          </Card>
        </>
      );
    if (screen === "duties")
      return (
        <>
          {header(
            "PHÒNG THỰC HÀNH · TUẦN 45",
            "Phân công nhân sự, theo dõi xác nhận và duyệt yêu cầu đổi ca.",
            <Space>
              <Button
                icon={<DownloadOutlined />}
                disabled={!hasPermission("duties.read")}
                onClick={() => exportCsv(visibleDutyShifts, "ca-truc")}
              >
                Xuất danh sách
              </Button>
              {canCreateShift && (
                <Button
                  type="primary"
                  icon={<PlusOutlined />}
                  onClick={() => openForm("shifts", "Tạo ca trực")}
                >
                  Tạo ca trực
                </Button>
              )}
            </Space>,
          )}
          <Row gutter={[14, 14]} className="duty-summary">
            <Col span={8}>
              <Card>
                <Statistic title="Tổng ca trực" value={visibleDutyShifts.length} />
              </Card>
            </Col>
            <Col span={8}>
              <Card>
                <Statistic
                  title="Đã phân công"
                  value={visibleDutyShifts.filter((row) => row.assignee).length}
                  valueStyle={{ color: "#287d70" }}
                />
              </Card>
            </Col>
            <Col span={8}>
              <Card>
                <Statistic
                  title="Chưa có người"
                  value={visibleUnassignedDuties}
                  valueStyle={{ color: "#d36b45" }}
                />
              </Card>
            </Col>
          </Row>
          <Card title={isTeacher ? "Ca trực được giao cho tôi" : "Ca trực theo ngày"} className="panel-card table-card">
            <Table
              rowKey="id"
              columns={shiftColumns}
              dataSource={visibleDutyShifts}
              pagination={false}
              locale={{ emptyText: "Bạn chưa được phân công ca trực nào" }}
            />
          </Card>
          <Card title={isTeacher ? "Yêu cầu ca trực của tôi" : "Yêu cầu đổi ca"} className="panel-card table-card">
            <Table
              rowKey="id"
              dataSource={visibleDutyRequests}
              pagination={false}
              columns={[
                { title: "Loại yêu cầu", dataIndex: "requestType", render: (value) => value || "Đổi ca" },
                { title: "Ngày", dataIndex: "date" },
                { title: "Ca trực", dataIndex: "shift" },
                { title: "Người yêu cầu", dataIndex: "from" },
                { title: "Người thay thế", dataIndex: "to" },
                { title: "Lý do", dataIndex: "reason" },
                { title: "Trạng thái", dataIndex: "status", render: statusTag },
                {
                  title: "",
                  render: (_, row) =>
                    row.status === "Chờ duyệt" && canApprove ? (
                      <Space>
                        <Button type="link" onClick={() => void shiftSwap(row, true)}>
                          Duyệt
                        </Button>
                        <Button type="link" danger onClick={() => void shiftSwap(row, false)}>
                          Từ chối
                        </Button>
                      </Space>
                    ) : (
                      "—"
                    ),
                },
              ]}
            />
          </Card>
        </>
      );
    if (screen === "people")
      return (
        <>
          {header(
            "DANH MỤC ĐÀO TẠO",
            "Hồ sơ dùng chung với tài khoản, lớp và lịch giảng dạy.",
            <Button
              type="primary"
              icon={<PlusOutlined />}
              disabled={!hasPermission("teachers.write")}
              onClick={() => openForm("teachers", "Thêm giáo viên")}
            >
              Thêm giáo viên
            </Button>,
          )}
          <Card className="panel-card">
            <Tabs
              items={[
                {
                  key: "teachers",
                  label: `Giáo viên (${rows.teachers.length})`,
                  children: peopleView("teachers"),
                },
                {
                  key: "classes",
                  label: `Lớp học (${rows.classes.length})`,
                  children: (
                    <>
                      <div className="inner-action">
                        <Button
                          icon={<PlusOutlined />}
                          disabled={!hasPermission("classes.write")}
                          onClick={() => openForm("classes", "Thêm lớp học")}
                        >
                          Thêm lớp
                        </Button>
                      </div>
                      {peopleView("classes")}
                    </>
                  ),
                },
                {
                  key: "subjects",
                  label: `Môn học (${rows.subjects.length})`,
                  children: (
                    <>
                      <div className="inner-action">
                        <Button
                          icon={<PlusOutlined />}
                          disabled={!hasPermission("subjects.write")}
                          onClick={() => openForm("subjects", "Thêm môn học")}
                        >
                          Thêm môn
                        </Button>
                      </div>
                      {inventoryView("subjects")}
                    </>
                  ),
                },
                {
                  key: "timeSlots",
                  label: `Khung giờ (${rows.timeSlots.length})`,
                  children: (
                    <>
                      <div className="inner-action">
                        <Button
                          icon={<PlusOutlined />}
                          disabled={!hasPermission("subjects.write")}
                          onClick={() => openForm("timeSlots", "Thêm khung giờ")}
                        >
                          Thêm khung giờ
                        </Button>
                      </div>
                      <Table
                        rowKey="id"
                        dataSource={rows.timeSlots}
                        pagination={false}
                        columns={[
                          { title: "Tên khung giờ / loại ca", dataIndex: "name" },
                          { title: "Bắt đầu", dataIndex: "start" },
                          { title: "Kết thúc", dataIndex: "end" },
                          { title: "Mục đích", dataIndex: "kind" },
                          { title: "Thao tác", render: (_, row) => rowActions("timeSlots", row) },
                        ]}
                      />
                    </>
                  ),
                },
                {
                  key: "terms",
                  label: `Học kỳ (${rows.academicTerms.length})`,
                  children: (
                    <>
                      <div className="inner-action">
                        <Button
                          icon={<PlusOutlined />}
                          disabled={!canManageUsers}
                          onClick={() => openForm("academicTerms", "Thêm học kỳ")}
                        >
                          Thêm học kỳ
                        </Button>
                      </div>
                      <Table
                        rowKey="id"
                        dataSource={rows.academicTerms}
                        pagination={false}
                        columns={[
                          { title: "Học kỳ", dataIndex: "name" },
                          { title: "Bắt đầu", dataIndex: "startDate" },
                          { title: "Kết thúc", dataIndex: "endDate" },
                          { title: "Trạng thái", dataIndex: "status", render: statusTag },
                          {
                            title: "Thao tác",
                            render: (_, row) => canManageUsers ? (
                              <Space>
                                <Button type="link" onClick={() => openForm("academicTerms", "Cập nhật học kỳ", row)}>Sửa</Button>
                                <Button type="link" danger onClick={() => remove("academicTerms", String(row.id))}>Xóa</Button>
                              </Space>
                            ) : null,
                          },
                        ]}
                      />
                    </>
                  ),
                },
              ]}
            />
          </Card>
        </>
      );
    if (screen === "rooms")
      return (
        <>
          {header(
            "VẬN HÀNH CƠ SỞ",
            "Danh mục phòng học, sức chứa và trạng thái vận hành.",
            <Button
              type="primary"
              icon={<PlusOutlined />}
              disabled={!hasPermission("rooms.write")}
              onClick={() => openForm("rooms", "Thêm phòng học")}
            >
              Thêm phòng
            </Button>,
          )}
          <Card className="panel-card">
            <Tabs
              items={[
                {
                  key: "rooms",
                  label: `Phòng (${rows.rooms.length})`,
                  children: inventoryView("rooms"),
                },
                {
                  key: "equipment",
                  label: `Thiết bị (${rows.equipment.length})`,
                  children: (
                    <>
                      <div className="inner-action">
                        <Button
                          icon={<PlusOutlined />}
                          disabled={!hasPermission("rooms.write")}
                          onClick={() => openForm("equipment", "Thêm thiết bị")}
                        >
                          Thêm thiết bị
                        </Button>
                      </div>
                      <Table
                        rowKey="id"
                        dataSource={rows.equipment}
                        pagination={{ pageSize: 6 }}
                        columns={[
                          { title: "Thiết bị", dataIndex: "name" },
                          { title: "Phòng", dataIndex: "room" },
                          { title: "Tình trạng", dataIndex: "status", render: statusTag },
                          { title: "Kiểm tra gần nhất", dataIndex: "lastChecked" },
                          { title: "Thao tác", render: (_, row) => rowActions("equipment", row) },
                        ]}
                      />
                    </>
                  ),
                },
                {
                  key: "sessions",
                  label: `Phiên thực hành (${rows.sessions.length})`,
                  children: (
                    <Table
                      rowKey="id"
                      dataSource={rows.sessions}
                      pagination={{ pageSize: 6 }}
                      locale={{ emptyText: "Chưa có phiên thực hành" }}
                      columns={[
                        { title: "Lớp", dataIndex: "className" },
                        { title: "Giáo viên", dataIndex: "teacher" },
                        { title: "Phòng", dataIndex: "room" },
                        { title: "Ngày", dataIndex: "date" },
                        { title: "Bắt đầu", dataIndex: "startedAt" },
                        { title: "Kết thúc", dataIndex: "endedAt" },
                        { title: "Trạng thái", dataIndex: "status", render: statusTag },
                        {
                          title: "Thao tác",
                          render: (_, session) => session.status === "Đang diễn ra" ? (
                            <Button
                              size="small"
                              onClick={async () => {
                                await saveRecord("sessions", {
                                  ...session,
                                  status: "Hoàn thành",
                                  endedAt: new Date().toISOString(),
                                });
                                await recordAudit("Kết thúc phiên thực hành", "Phiên", String(session.id), String(session.note ?? ""));
                                await addNotification("Phiên thực hành đã kết thúc", `${session.className} · ${session.room}`);
                                refresh();
                              }}
                            >
                              Kết thúc phiên
                            </Button>
                          ) : null,
                        },
                      ]}
                    />
                  ),
                },
              ]}
            />
          </Card>
        </>
      );
    if (screen === "reports")
      return (
        <>
          {header(
            "TỔNG HỢP HOẠT ĐỘNG",
            "Giờ dạy và giờ trực được theo dõi riêng, có thể xuất CSV.",
            <Select
              value={reportMonth}
              onChange={setReportMonth}
              options={Array.from({ length: 6 }, (_, index) => {
                const month = dayjs().subtract(index, "month");
                return {
                  value: month.format("YYYY-MM"),
                  label: month.format("[Tháng] MM, YYYY"),
                };
              })}
            />,
          )}
          <Row gutter={[16, 16]}>
            <Col xs={24} lg={12}>
              <Card title="Tổng hợp giờ dạy" className="panel-card">
                <Table
                  rowKey="id"
                  pagination={false}
                  dataSource={rows.teachers.map((teacher) => ({
                    ...teacher,
                    total:
                      reportSchedules.filter(
                        (item) => item.teacher === teacher.name,
                      ).reduce((total, item) => total + durationHours(item), 0),
                  }))}
                  columns={[
                    { title: "Giáo viên", dataIndex: "name" },
                    {
                      title: "Số buổi",
                      dataIndex: "total",
                      render: (_, row) =>
                        reportSchedules.filter((item) => item.teacher === row.name).length,
                    },
                    {
                      title: "Tổng giờ",
                      dataIndex: "total",
                      render: (value) => `${value} giờ`,
                    },
                  ]}
                />
                <Button
                  className="export-wide"
                  icon={<DownloadOutlined />}
                  onClick={() => exportCsv(reportSchedules, "bao-cao-gio-day")}
                >
                  Xuất chi tiết giờ dạy
                </Button>
              </Card>
            </Col>
            <Col xs={24} lg={12}>
              <Card title="Tổng hợp ca trực" className="panel-card">
                <Table
                  rowKey="id"
                  pagination={false}
                  dataSource={rows.teachers.map((teacher) => ({
                    ...teacher,
                    total: reportShifts
                      .filter((item) => String(item.assignee ?? "").split(";").includes(String(teacher.name)))
                      .reduce((hours, item) => hours + durationHours(item), 0),
                  }))}
                  columns={[
                    { title: "Người trực", dataIndex: "name" },
                    {
                      title: "Số ca",
                      render: (_, row) =>
                        reportShifts.filter((item) => String(item.assignee ?? "").split(";").includes(String(row.name))).length,
                    },
                    {
                      title: "Thời lượng",
                      dataIndex: "total",
                      render: (value) => `${Number(value).toFixed(1)} giờ`,
                    },
                  ]}
                />
                <Button
                  className="export-wide"
                  icon={<DownloadOutlined />}
                  onClick={() => exportCsv(reportShifts, "bao-cao-ca-truc")}
                >
                  Xuất chi tiết ca trực
                </Button>
              </Card>
            </Col>
          </Row>
          <Row gutter={[16, 16]} style={{ marginTop: 2 }}>
            <Col xs={24} xl={12}>
              <Card title="Cân bằng phân công" className="panel-card">
                <Table
                  rowKey="id"
                  pagination={false}
                  dataSource={rows.teachers.map((teacher) => {
                    const teachingHours = reportSchedules
                      .filter((item) => item.teacher === teacher.name)
                      .reduce((total, item) => total + durationHours(item), 0);
                    const dutyHours = reportShifts
                      .filter((item) => String(item.assignee ?? "").split(";").includes(String(teacher.name)))
                      .reduce((total, item) => total + durationHours(item), 0);
                    return { ...teacher, teachingHours, dutyHours };
                  })}
                  columns={[
                    { title: "Giáo viên", dataIndex: "name" },
                    { title: "Giờ dạy", dataIndex: "teachingHours", render: (value) => `${Number(value).toFixed(1)} giờ` },
                    { title: "Giờ trực", dataIndex: "dutyHours", render: (value) => `${Number(value).toFixed(1)} giờ` },
                    {
                      title: "Tổng giờ",
                      render: (_, row) => `${(Number(row.teachingHours) + Number(row.dutyHours)).toFixed(1)} giờ`,
                    },
                  ]}
                />
              </Card>
            </Col>
            <Col xs={24} xl={12}>
              <Card title="Tình trạng lịch và sử dụng phòng" className="panel-card">
                <Space wrap style={{ marginBottom: 14 }}>
                  <Statistic title="Lịch được xếp" value={reportSchedules.length} />
                  <Statistic title="Chờ xác nhận" value={pendingScheduleCount} />
                  <Statistic title="Đã hủy" value={cancelledScheduleCount} />
                </Space>
                <Table
                  rowKey="id"
                  pagination={false}
                  dataSource={roomReport}
                  columns={[
                    { title: "Phòng", dataIndex: "name" },
                    { title: "Giờ sử dụng", dataIndex: "usedHours", render: (value) => `${Number(value).toFixed(1)} giờ` },
                    {
                      title: "Tỷ lệ sử dụng",
                      dataIndex: "utilization",
                      render: (value) => <Progress percent={Number(value)} size="small" />,
                    },
                  ]}
                />
              </Card>
            </Col>
          </Row>
        </>
      );
    return null;
  };

  return (
    <AntApp>
      {holder}
      <Layout className="app-shell">
        <AppSidebar
          role={role}
          selfName={selfName}
          screen={screen}
          visibleNavigation={sidebarNavigation}
          recordsUsed={recordsUsed}
          dutyBadgeCount={dutyBadgeCount}
          dutyBadgeLabel={dutyBadgeLabel}
          isMobile={isMobile}
          mobileOpen={mobileNavOpen}
          onScreenChange={navigateToScreen}
          onToggle={() => setMobileNavOpen(false)}
          onCloseMobile={() => setMobileNavOpen(false)}
          onResetDemo={() =>
            Modal.confirm({
              title: "Khôi phục dữ liệu mẫu?",
              content: "Thao tác này sẽ thay thế dữ liệu demo hiện tại bằng dữ liệu mẫu.",
              okText: "Khôi phục",
              cancelText: "Hủy",
              onOk: async () => {
                await resetDatabase();
                refresh();
                toast.success("Đã khôi phục dữ liệu mẫu");
              },
            })
          }
        />
        <Layout>
          <Layout.Header className="topbar">
            <div className="topbar-left">
              {isMobile && <Button
                type="text"
                icon={<MenuOutlined />}
                aria-label={mobileNavOpen ? "Đóng menu" : "Mở menu"}
                title={mobileNavOpen ? "Đóng menu" : "Mở menu"}
                onClick={() => setMobileNavOpen((open) => !open)}
              />}
              <span className="breadcrumb">Vận hành</span>
              <span className="crumb-divider">/</span>
              <strong>{screen === "overview" ? overviewTitle : titles[screen]}</strong>
            </div>
            <Space size={14}>
              <Tag className="demo-chip">SERVER RAM</Tag>
              <Button
                type="text"
                icon={<Badge count={unreadNotifications} size="small"><BellOutlined /></Badge>}
                aria-label="Thông báo"
                onClick={() => setNotificationsOpen(true)}
              />
              <span className="top-role">Thử vai trò</span>
              <Select
                size="small"
                value={role}
                onChange={changeRole}
                options={roles.map((value) => ({
                  value,
                  label: value,
                }))}
                style={{ width: 220 }}
                aria-label="Chọn vai trò demo"
              />
            </Space>
          </Layout.Header>
          <Layout.Content className="main-content">
            {renderContent()}
            <footer className="app-footer">
              <span>LABMONITORING</span>
              <span>
                SQLite server RAM · dữ liệu có thể mất khi instance khởi động
                lại
              </span>
            </footer>
          </Layout.Content>
        </Layout>
        <RecordEditorModal
          dialog={dialog}
          form={form}
          teachers={rows.teachers}
          classes={rows.classes}
          rooms={rows.rooms}
          subjects={rows.subjects}
          academicTerms={rows.academicTerms}
          timeSlots={rows.timeSlots}
          roles={roles}
          selfName={selfName}
          onClose={() => {
            setDialog(null);
            form.resetFields();
          }}
          onSubmit={saveForm}
        />
        <Modal
          title="Thông báo trong ứng dụng"
          open={notificationsOpen}
          onCancel={() => setNotificationsOpen(false)}
          footer={null}
        >
          <div className="inner-action">
            <Button
              disabled={!unreadNotifications}
              onClick={async () => {
                await Promise.all(
                  rows.notifications
                    .filter((item) => !item.read)
                    .map((item) => saveRecord("notifications", { ...item, read: true })),
                );
                refresh();
              }}
            >
              Đánh dấu tất cả đã đọc
            </Button>
          </div>
          <Table
            rowKey="id"
            size="small"
            pagination={{ pageSize: 5 }}
            dataSource={[...rows.notifications].sort((first, second) =>
              String(second.createdAt).localeCompare(String(first.createdAt)),
            )}
            locale={{ emptyText: "Chưa có thông báo" }}
            columns={[
              { title: "Nội dung", dataIndex: "title" },
              { title: "Chi tiết", dataIndex: "details" },
              {
                title: "Thời điểm",
                dataIndex: "createdAt",
                render: (value) =>
                  value ? dayjs(String(value)).format("DD/MM/YYYY HH:mm") : "—",
              },
              {
                title: "Trạng thái",
                dataIndex: "read",
                render: (value) => value ? "Đã đọc" : "Chưa đọc",
              },
              {
                title: "",
                render: (_, item) => !item.read ? (
                  <Button
                    type="link"
                    size="small"
                    onClick={async () => {
                      await saveRecord("notifications", { ...item, read: true });
                      refresh();
                    }}
                  >
                    Đã đọc
                  </Button>
                ) : null,
              },
            ]}
          />
        </Modal>
        <Modal
          title="Tạo vai trò mới"
          open={roleCreateOpen}
          onCancel={() => {
            setRoleCreateOpen(false);
            setNewRoleName("");
          }}
          onOk={createRole}
          okText="Tạo vai trò"
          cancelText="Hủy"
        >
          <Form layout="vertical">
            <Form.Item label="Tên vai trò" required>
              <Input
                autoFocus
                value={newRoleName}
                onChange={(event) => setNewRoleName(event.target.value)}
                onPressEnter={createRole}
                placeholder="Ví dụ: Trưởng bộ môn"
              />
            </Form.Item>
            <Typography.Text type="secondary">
              Vai trò mới được tạo với mọi quyền tắt. Bật quyền trong ma trận
              sau khi tạo.
            </Typography.Text>
          </Form>
        </Modal>
      </Layout>
    </AntApp>
  );
}

export default App;
