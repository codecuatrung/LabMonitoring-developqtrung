import {
  DatePicker,
  Form,
  Input,
  InputNumber,
  Modal,
  Select,
  TimePicker,
} from "antd";
import dayjs from "dayjs";
import type { FormInstance } from "antd/es/form";
import type { Collection } from "../store.ts";
import type { RowData } from "./model";
import type { Entity } from "../store.ts";
import { days } from "./model";

export type RecordDialog = {
  collection: Collection;
  title: string;
  record?: Partial<RowData>;
} | null;

type Field = {
  key: string;
  label: string;
  options?: string[];
  kind?: "date" | "time" | "number";
  optional?: boolean;
};

type RecordEditorModalProps = {
  dialog: RecordDialog;
  form: FormInstance;
  teachers: Entity[];
  classes: Entity[];
  rooms: Entity[];
  subjects: Entity[];
  academicTerms: Entity[];
  timeSlots: Entity[];
  roles: string[];
  selfName: string;
  onClose: () => void;
  onSubmit: () => void;
};

export function RecordEditorModal({
  dialog,
  form,
  teachers,
  classes,
  rooms,
  subjects,
  academicTerms,
  timeSlots,
  roles,
  selfName,
  onClose,
  onSubmit,
}: RecordEditorModalProps) {
  const fields: Record<Collection, Field[]> = {
    teachers: [
      { key: "name", label: "Họ và tên" },
      { key: "email", label: "Email" },
      { key: "phone", label: "Số điện thoại" },
      { key: "department", label: "Bộ môn" },
      { key: "employeeCode", label: "Mã viên chức", optional: true },
      { key: "position", label: "Chức danh", optional: true },
      {
        key: "role",
        label: "Vai trò",
        options: ["Giáo viên", "Điều phối", "Kỹ thuật viên"],
      },
      {
        key: "status",
        label: "Trạng thái",
        options: ["Đang hoạt động", "Ngừng hoạt động"],
      },
    ],
    classes: [
      { key: "name", label: "Tên lớp" },
      { key: "code", label: "Mã lớp" },
      {
        key: "teacher",
        label: "Giáo viên chủ nhiệm",
        options: teachers.map((row) => String(row.name)),
      },
      { key: "students", label: "Sĩ số" },
      {
        key: "semester",
        label: "Học kỳ",
        options: academicTerms.map((term) => String(term.name)),
        optional: true,
      },
    ],
    rooms: [
      { key: "name", label: "Tên phòng" },
      { key: "building", label: "Tòa nhà" },
      { key: "capacity", label: "Sức chứa" },
      {
        key: "status",
        label: "Tình trạng",
        options: ["Sẵn sàng", "Bảo trì", "Tạm ngưng"],
      },
    ],
    equipment: [
      { key: "name", label: "Tên thiết bị" },
      {
        key: "room",
        label: "Phòng / khu vực",
        options: rooms.map((row) => String(row.name)),
      },
      {
        key: "status",
        label: "Tình trạng",
        options: ["Sẵn sàng", "Cần kiểm tra", "Đang sửa chữa", "Ngừng sử dụng"],
      },
      { key: "lastChecked", label: "Ngày kiểm tra" },
    ],
    subjects: [
      { key: "name", label: "Tên môn học" },
      { key: "code", label: "Mã môn" },
      { key: "hours", label: "Thời lượng (giờ)" },
    ],
    timeSlots: [
      { key: "name", label: "Tên khung giờ / loại ca" },
      { key: "start", label: "Giờ bắt đầu", kind: "time" },
      { key: "end", label: "Giờ kết thúc", kind: "time" },
      { key: "kind", label: "Mục đích", options: ["Giảng dạy", "Ca trực"] },
    ],
    schedules: [
      { key: "date", label: "Ngày học", kind: "date" },
      {
        key: "timeSlot",
        label: "Khung giờ mẫu",
        options: timeSlots.filter((slot) => slot.kind === "Giảng dạy").map((slot) => String(slot.name)),
        optional: true,
      },
      {
        key: "semester",
        label: "Học kỳ",
        options: academicTerms.map((term) => String(term.name)),
        optional: true,
      },
      { key: "day", label: "Thứ trong tuần", options: days },
      {
        key: "type",
        label: "Loại buổi học",
        options: ["Giảng dạy", "Học bù"],
      },
      {
        key: "repeatMode",
        label: "Chu kỳ",
        options: ["Một lần", "Hàng tuần"],
        optional: true,
      },
      {
        key: "repeatUntil",
        label: "Lặp đến ngày",
        kind: "date",
        optional: true,
      },
      { key: "makeupFor", label: "Buổi cần học bù", optional: true },
      { key: "start", label: "Giờ bắt đầu", kind: "time" },
      { key: "end", label: "Giờ kết thúc", kind: "time" },
      {
        key: "teacher",
        label: "Giáo viên",
        options: teachers.map((row) => String(row.name)),
      },
      {
        key: "className",
        label: "Lớp học",
        options: classes.map((row) => String(row.name)),
      },
      {
        key: "subject",
        label: "Môn học",
        options: subjects.map((row) => String(row.name)),
      },
      {
        key: "room",
        label: "Phòng",
        options: rooms.map((row) => String(row.name)),
      },
      {
        key: "status",
        label: "Trạng thái",
        options: ["Đã xác nhận", "Chờ xác nhận"],
      },
    ],
    shifts: [
      { key: "date", label: "Ngày trực", kind: "date" },
      {
        key: "timeSlot",
        label: "Khung ca mẫu",
        options: timeSlots.filter((slot) => slot.kind === "Ca trực").map((slot) => String(slot.name)),
        optional: true,
      },
      {
        key: "repeatMode",
        label: "Chu kỳ ca",
        options: ["Một lần", "Hàng tuần", "Hàng tháng"],
        optional: true,
      },
      {
        key: "repeatUntil",
        label: "Sinh ca đến ngày",
        kind: "date",
        optional: true,
      },
      { key: "start", label: "Giờ bắt đầu", kind: "time" },
      { key: "end", label: "Giờ kết thúc", kind: "time" },
      {
        key: "room",
        label: "Phòng / khu vực",
        options: rooms.map((row) => String(row.name)),
      },
      { key: "required", label: "Số người trực", kind: "number" },
      {
        key: "status",
        label: "Trạng thái",
        options: ["Chưa phân công", "Đã phân công"],
      },
    ],
    users: [
      { key: "name", label: "Tên người dùng" },
      { key: "email", label: "Email" },
      { key: "role", label: "Vai trò", options: roles },
      { key: "scope", label: "Phạm vi dữ liệu" },
      {
        key: "status",
        label: "Trạng thái",
        options: ["Đang hoạt động", "Tạm khóa"],
      },
    ],
    requests: [
      { key: "from", label: "Người gửi" },
      { key: "date", label: "Ngày ca", kind: "date" },
      { key: "shift", label: "Ca trực" },
      {
        key: "to",
        label: "Người đề xuất thay",
        options: teachers
          .filter((teacher) => teacher.name !== selfName)
          .map((teacher) => String(teacher.name)),
      },
      { key: "reason", label: "Lý do đổi ca" },
    ],
    permissions: [],
    dutyTemplates: [],
    academicTerms: [
      { key: "name", label: "Tên học kỳ" },
      { key: "startDate", label: "Ngày bắt đầu" },
      { key: "endDate", label: "Ngày kết thúc" },
      {
        key: "status",
        label: "Trạng thái",
        options: ["Sắp diễn ra", "Đang diễn ra", "Đã kết thúc"],
      },
    ],
    sessions: [],
    notifications: [],
    auditLogs: [],
  };

  return (
    <Modal
      title={dialog?.title}
      open={Boolean(dialog)}
      onCancel={onClose}
      onOk={onSubmit}
      okText="Lưu thông tin"
      cancelText="Hủy"
      destroyOnClose
    >
      <Form form={form} layout="vertical" className="record-form">
        {dialog &&
          fields[dialog.collection].map((field) => (
            <Form.Item
              key={field.key}
              name={field.key}
              label={field.label}
              rules={[
                {
                  required: !field.optional,
                  message: `Vui lòng nhập ${field.label.toLowerCase()}`,
                },
              ]}
            >
              {field.options ? (
                <Select
                  placeholder={`Chọn ${field.label.toLowerCase()}`}
                  optionFilterProp="label"
                  options={field.options.map((value) => ({
                    value,
                    label: value,
                  }))}
                  showSearch
                  onChange={(value) => {
                    if (field.key !== "timeSlot") return;
                    const slot = timeSlots.find((item) => item.name === value);
                    if (!slot) return;
                    form.setFieldsValue({
                      start: dayjs(`2000-01-01T${String(slot.start)}:00`),
                      end: dayjs(`2000-01-01T${String(slot.end)}:00`),
                    });
                  }}
                />
              ) : field.kind === "date" ? (
                <DatePicker
                  format="DD/MM/YYYY"
                  placeholder="Chọn ngày"
                  style={{ width: "100%" }}
                />
              ) : field.kind === "time" ? (
                <TimePicker
                  format="HH:mm"
                  minuteStep={5}
                  placeholder="Chọn giờ"
                  style={{ width: "100%" }}
                />
              ) : field.kind === "number" ||
                ["students", "capacity", "hours"].includes(field.key) ? (
                <InputNumber
                  min={1}
                  placeholder={field.label}
                  style={{ width: "100%" }}
                />
              ) : (
                <Input placeholder={field.label} />
              )}
            </Form.Item>
          ))}
      </Form>
    </Modal>
  );
}
